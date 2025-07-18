using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SoftfyWeb.Dtos;
using SoftfyWeb.Modelos;
using SoftfyWeb.Modelos.Dtos;
using SoftfyWeb.Models;
using System;
using System.Diagnostics;
using System.IdentityModel.Tokens.Jwt;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Security.Claims;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

namespace SoftfyWeb.Controllers
{
    public class VistasAuthController : Controller
    {
        private readonly IHttpClientFactory _httpClientFactory;

        public VistasAuthController(IHttpClientFactory httpClientFactory)
            => _httpClientFactory = httpClientFactory;

        // Helper para inyectar el token JWT desde la cookie
        private HttpClient ObtenerClienteConToken()
        {
            var client = _httpClientFactory.CreateClient("SoftfyApi");
            var jwt = User.FindFirst("jwt")?.Value;
            if (!string.IsNullOrEmpty(jwt))
                client.DefaultRequestHeaders.Authorization =
                    new AuthenticationHeaderValue("Bearer", jwt);
            return client;
        }
        private ErrorViewModel CrearErrorModel()
        {
            string id = Activity.Current?.Id ?? HttpContext.TraceIdentifier;
            return new ErrorViewModel { RequestId = id };
        }

        [HttpGet]
        public IActionResult ForgotPassword() => View();

        [HttpPost]
        public async Task<IActionResult> ForgotPassword(ForgotPasswordDto dto)
        {
            if (!IsValidEmail(dto.Email))
                ModelState.AddModelError(nameof(dto.Email), "Correo inválido.");
            if (!ModelState.IsValid)
                return View(dto);

            var client = _httpClientFactory.CreateClient();
            var resp = await client.PostAsync(
                "https://localhost:7003/api/auth/forgot-password",
                new StringContent(JsonSerializer.Serialize(dto), Encoding.UTF8, "application/json")
            );
            var raw = await resp.Content.ReadAsStringAsync();
            if (!resp.IsSuccessStatusCode)
            {
                ModelState.AddModelError("", raw);
                return View(dto);
            }
            TempData["Info"] = "Si el correo existe, recibirás instrucciones para restablecer tu contraseña.";
            return RedirectToAction(nameof(Login));
        }

        [HttpGet]
        public IActionResult ResetPassword(string userId, string token, string email)
            => View(new ResetPasswordDto { Email = email, Token = token });

        [HttpPost]
        public async Task<IActionResult> ResetPassword(ResetPasswordDto dto)
        {
            if (string.IsNullOrWhiteSpace(dto.NewPassword) || dto.NewPassword.Length < 6)
                ModelState.AddModelError(nameof(dto.NewPassword), "La contraseña debe tener al menos 6 caracteres.");
            if (!ModelState.IsValid)
                return View(dto);

            var client = _httpClientFactory.CreateClient();
            var resp = await client.PostAsync(
                "https://localhost:7003/api/auth/reset-password",
                new StringContent(JsonSerializer.Serialize(dto), Encoding.UTF8, "application/json")
            );
            var raw = await resp.Content.ReadAsStringAsync();
            if (!resp.IsSuccessStatusCode)
            {
                ModelState.AddModelError("", raw);
                return View(dto);
            }
            TempData["Info"] = "Contraseña restablecida correctamente. Ahora puedes iniciar sesión.";
            return RedirectToAction(nameof(Login));
        }

        [HttpPost]
        public async Task<IActionResult> Logout()
        {
            // Cierra la sesión en la cookie 'auth_cookie'
            await HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
            return RedirectToAction(nameof(Login));
        }

        [HttpGet]
        public IActionResult RegistroArtista() => View();

        [HttpGet]
        public IActionResult Registro() => View(new UsuarioRegistroDto());

        [HttpPost]
        public async Task<IActionResult> Registro(UsuarioRegistroDto dto)
        {
            if (!EsContrasenaSegura(dto.Password))
                ModelState.AddModelError(nameof(dto.Password), "Debe tener ≥6 caracteres, 1 mayúscula y 1 número.");
            if (!IsValidEmail(dto.Email))
                ModelState.AddModelError(nameof(dto.Email), "Correo inválido.");
            if (!ModelState.IsValid)
                return View(dto);

            var client = _httpClientFactory.CreateClient();
            var url = dto.TipoUsuario == "Artista"
                ? "https://localhost:7003/api/auth/registro-artista"
                : "https://localhost:7003/api/auth/registro";
            var resp = await client.PostAsync(
                url,
                new StringContent(JsonSerializer.Serialize(dto), Encoding.UTF8, "application/json")
            );
            var raw = await resp.Content.ReadAsStringAsync();
            if (!resp.IsSuccessStatusCode)
            {
                ModelState.AddModelError("", raw);
                return View(dto);
            }
            TempData["RegistroOk"] = "¡Registro exitoso! Revisa tu correo y luego inicia sesión.";
            return RedirectToAction(nameof(Login));
        }

        [HttpGet]
        public IActionResult Login(string returnUrl = null)
        {
            if (User.Identity.IsAuthenticated)
                return RedirectToAction("Index", "Home");

            ViewBag.ReturnUrl = returnUrl;
            ViewBag.Info = TempData["RegistroOk"];
            return View(new UsuarioLoginDto());
        }

        [HttpPost]
        public async Task<IActionResult> Login(UsuarioLoginDto dto, string returnUrl = null)
        {
            var client = _httpClientFactory.CreateClient();
            var resp = await client.PostAsJsonAsync("https://localhost:7003/api/auth/login", dto);
            var raw = await resp.Content.ReadAsStringAsync();

            if (!resp.IsSuccessStatusCode)
            {
                var errorResponse = JsonDocument.Parse(raw).RootElement;
                var errorMessage = errorResponse.GetProperty("error").GetString();  // Extraemos el mensaje de error

                // Aquí mostramos el error de confirmación de correo o credenciales inválidas
                ViewBag.Error = errorMessage;
                return View(dto);
            }

            // Si las credenciales son correctas, procesamos el JWT
            var token = JsonDocument.Parse(raw).RootElement.GetProperty("token").GetString();
            var handler = new JwtSecurityTokenHandler();
            var jwtToken = handler.ReadJwtToken(token);

            // Creamos la identidad y el principal del usuario
            var identity = new ClaimsIdentity(
                jwtToken.Claims,
                CookieAuthenticationDefaults.AuthenticationScheme
            );
            identity.AddClaim(new Claim("jwt", token));

            var principal = new ClaimsPrincipal(identity);

            // Iniciamos la sesión
            await HttpContext.SignInAsync(
                CookieAuthenticationDefaults.AuthenticationScheme,
                principal,
                new AuthenticationProperties
                {
                    ExpiresUtc = DateTimeOffset.UtcNow.AddHours(2),
                    IsPersistent = false
                }
            );

            var role = jwtToken.Claims.FirstOrDefault(c => c.Type == ClaimTypes.Role)?.Value;
            if (!string.IsNullOrEmpty(returnUrl) && Url.IsLocalUrl(returnUrl))
                return Redirect(returnUrl);
            if (role == "Artista")
                return RedirectToAction(nameof(BienvenidoArtista));
            if (role == "Oyente" || role == "OyentePremium")
                return RedirectToAction(nameof(BienvenidoOyente));

            return RedirectToAction(nameof(Bienvenido));
        }




        [HttpGet]
        public async Task<IActionResult> ConfirmEmail(string userId, string token)
        {
            var client = _httpClientFactory.CreateClient();
            var resp = await client.GetAsync(
                $"https://localhost:7003/api/auth/confirmar-email?userId={userId}&token={token}"
            );
            var raw = await resp.Content.ReadAsStringAsync();
            if (!resp.IsSuccessStatusCode)
            {
                TempData["Error"] = "Hubo un error al confirmar tu correo.";
                return RedirectToAction(nameof(Login));
            }
            TempData["Info"] = "Tu correo ha sido confirmado correctamente. Ahora puedes iniciar sesión.";
            return RedirectToAction(nameof(Login));
        }

        [Authorize(Roles = "Artista")]
        public async Task<IActionResult> BienvenidoArtista()
        {
            var client = ObtenerClienteConToken();
            string nombreArtistico = User.Identity?.Name ?? "Artista";
            ViewBag.Canciones = new List<CancionRespuestaDto>();
            ViewBag.Playlists = new List<PlaylistDto>();


            var respPerfil = await client.GetAsync("artistas/mi-perfil");
            if (respPerfil.IsSuccessStatusCode)
            {
                var raw = await respPerfil.Content.ReadAsStringAsync();
                var perfil = JsonSerializer.Deserialize<PerfilArtistaDto>(raw, new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true
                });
                if (perfil is not null)
                    nombreArtistico = perfil.NombreArtistico;
            }

            var respCanciones = await client.GetAsync("canciones/mis-canciones");
            if (respCanciones.IsSuccessStatusCode)
            {
                var raw = await respCanciones.Content.ReadAsStringAsync();
                var canciones = JsonSerializer.Deserialize<List<CancionRespuestaDto>>(raw, new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true
                });
                ViewBag.Canciones = canciones ?? new List<CancionRespuestaDto>();
            }

            var respPlaylists = await client.GetAsync("playlists/mis-playlists");
            if (respPlaylists.IsSuccessStatusCode)
            {
                var raw = await respPlaylists.Content.ReadAsStringAsync();
                var playlists = JsonSerializer.Deserialize<List<PlaylistDto>>(raw, new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true
                });
                ViewBag.Playlists = playlists ?? new List<PlaylistDto>();
            }

            ViewBag.ArtistaNombre = nombreArtistico;
            return View();
        }

        public async Task<IActionResult> BienvenidoOyente()
        {
            var nombreOyente = User.Identity.Name;

            var client = ObtenerClienteConToken();
            var resp = await client.GetAsync("oyentes/mi-perfil");

            if (resp.IsSuccessStatusCode)
            {
                var raw = await resp.Content.ReadAsStringAsync();
                var perfil = JsonSerializer.Deserialize<PerfilOyenteDto>(raw,
                    new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

                if (perfil != null)
                {
                    nombreOyente = $"{perfil.Nombre} {perfil.Apellido}";
                }
            }

            ViewBag.OyenteNombre = nombreOyente;

            var canciones = new List<CancionRespuestaDto>();
            var respCanciones = await client.GetAsync("https://localhost:7003/api/Canciones/canciones");

            if (respCanciones.IsSuccessStatusCode)
            {
                var json = await respCanciones.Content.ReadAsStringAsync();
                canciones = JsonSerializer.Deserialize<List<CancionRespuestaDto>>(json,
                    new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

                canciones = canciones
                    .Where(c => !string.IsNullOrWhiteSpace(c.UrlArchivo))
                    .ToList();
            }
            var playlists = new List<PlaylistDto>();
            var respPlaylists = await client.GetAsync("https://localhost:7003/api/Playlists/todas/artistas");

            if (respPlaylists.IsSuccessStatusCode)
            {
                var json = await respPlaylists.Content.ReadAsStringAsync();
                playlists = JsonSerializer.Deserialize<List<PlaylistDto>>(json,
                    new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
            }

            var artistas = new List<Artista>();
            var respArtistas = await client.GetAsync("https://localhost:7003/api/Artistas");

            if (respArtistas.IsSuccessStatusCode)
            {
                var artistasJson = await respArtistas.Content.ReadAsStringAsync();
                artistas = JsonSerializer.Deserialize<List<Artista>>(artistasJson, new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true
                });

                foreach (var artista in artistas)
                {
                    if (!string.IsNullOrEmpty(artista.FotoUrl))
                    {
                        artista.FotoUrl = $"https://localhost:7003/api/artistas/foto/{artista.FotoUrl}";
                    }
                }

                ViewBag.Artistas = artistas;
            }

            ViewBag.TodasCanciones = canciones;
            ViewBag.TodasPlaylists = playlists;

            return View();
        }



        public IActionResult Bienvenido() => View();

        // Métodos auxiliares
        private bool EsContrasenaSegura(string pwd) =>
            !string.IsNullOrEmpty(pwd)
            && pwd.Length >= 6
            && pwd.Any(char.IsUpper)
            && pwd.Any(char.IsDigit);

        private bool IsValidEmail(string em)
        {
            try
            {
                var addr = new System.Net.Mail.MailAddress(em);
                return addr.Address == em;
            }
            catch { return false; }
        }

        [HttpGet]
        [AllowAnonymous]
        public async Task<IActionResult> Buscar(string termino)
        {
            if (string.IsNullOrWhiteSpace(termino))
            {
                ViewBag.Error = "Por favor, ingrese un término de búsqueda.";
                return View();
            }

            var client = _httpClientFactory.CreateClient();

            var cancionesResponse = await client.GetAsync($"https://localhost:7003/api/Canciones/por-nombre/{termino}");
            var artistasResponse = await client.GetAsync($"https://localhost:7003/api/Artistas/perfil/{termino}");

            // CANCIONES
            if (cancionesResponse.IsSuccessStatusCode)
            {
                var cancionesJson = await cancionesResponse.Content.ReadAsStringAsync();
                var canciones = JsonSerializer.Deserialize<List<CancionRespuestaDto>>(cancionesJson, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

                if (canciones != null && canciones.Any())
                {
                    // Actualizar URL para el reproductor
                    foreach (var cancion in canciones)
                    {
                        var nombreArchivo = Path.GetFileName(cancion.UrlArchivo);
                        cancion.UrlArchivo = $"https://localhost:7003/api/canciones/reproducir/{nombreArchivo}";
                    }

                    ViewBag.Canciones = canciones;
                }
                else
                {
                    ViewBag.Error = "No se encontraron canciones.";
                }
            }

            // ARTISTA
            if (artistasResponse.IsSuccessStatusCode)
            {
                var artistasJson = await artistasResponse.Content.ReadAsStringAsync();
                var artista = JsonSerializer.Deserialize<Artista>(artistasJson, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

                if (artista != null)
                {
                    if (!string.IsNullOrEmpty(artista.FotoUrl))
                    {
                        artista.FotoUrl = $"https://localhost:7003/api/artistas/foto/{artista.FotoUrl}";
                    }

                    ViewBag.Artista = artista;
                }
                else
                {
                    ViewBag.Error = "No se encontró el artista.";
                }
            }

            return View();
        }


        [HttpGet]
        public async Task<IActionResult> VerPerfil()
        {
            var client = ObtenerClienteConToken();
            var rol = User.Claims.FirstOrDefault(c => c.Type == ClaimTypes.Role)?.Value;

            if (rol == "Artista")
            {
                var response = await client.GetAsync("https://localhost:7003/api/Artistas/mi-perfil");
                if (response.IsSuccessStatusCode)
                {
                    var raw = await response.Content.ReadAsStringAsync();
                    var perfil = JsonSerializer.Deserialize<PerfilArtistaDto>(raw,
                        new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

                    if (perfil != null)
                    {
                        ViewBag.TipoUsuario = "Artista";
                        ViewBag.NombreArtistico = perfil.NombreArtistico;
                        ViewBag.FotoUrl = perfil.FotoUrl;
                        ViewBag.Biografia = perfil.Biografia;
                        ViewBag.Email = perfil.UsuarioEmail;
                        return View("VerPerfilArtista");
                    }
                }
            }

            if (rol == "Oyente" || rol == "OyentePremium")
            {
                var response = await client.GetAsync("https://localhost:7003/api/Oyentes/mi-perfil");
                if (response.IsSuccessStatusCode)
                {
                    var raw = await response.Content.ReadAsStringAsync();
                    var perfil = JsonSerializer.Deserialize<PerfilOyenteDto>(raw,
                        new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

                    if (perfil != null)
                    {
                        ViewBag.TipoUsuario = perfil.TipoUsuario;
                        ViewBag.Nombre = perfil.Nombre;
                        ViewBag.Apellido = perfil.Apellido;
                        return View("VerPerfilOyente");
                    }
                }
            }

            return NotFound("Perfil no encontrado.");
        }
        [HttpGet]
        [AllowAnonymous]
        public async Task<IActionResult> VerPerfilpublico(int id)
        {
            var client = _httpClientFactory.CreateClient();
            var responsePerfil = await client.GetAsync($"https://localhost:7003/api/Artistas/{id}");

            if (!responsePerfil.IsSuccessStatusCode)
                return NotFound("Perfil del artista no encontrado");

            var rawPerfil = await responsePerfil.Content.ReadAsStringAsync();
            var perfil = JsonSerializer.Deserialize<PerfilArtistaDto>(rawPerfil,
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

            var responseCanciones = await client.GetAsync($"https://localhost:7003/api/Artistas/{id}/canciones");
            List<CancionDto> canciones = new List<CancionDto>();
            if (responseCanciones.IsSuccessStatusCode)
            {
                var rawCanciones = await responseCanciones.Content.ReadAsStringAsync();
                canciones = JsonSerializer.Deserialize<List<CancionDto>>(rawCanciones, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

            }

            if (!string.IsNullOrEmpty(perfil.FotoUrl))
            {
                perfil.FotoUrl = $"https://localhost:7003/api/artistas/foto/{perfil.FotoUrl}";
            }

            ViewBag.ArtistaId = id;
            ViewBag.NombreArtistico = perfil.NombreArtistico;
            ViewBag.FotoUrl = perfil.FotoUrl;
            ViewBag.Biografia = perfil.Biografia;
            ViewBag.Email = perfil.UsuarioEmail;
            ViewBag.Canciones = canciones;

            return View("VerPerfilPublicoArtista");
        }

        [HttpPost]
        public async Task<IActionResult> ActualizarPerfilArtista(string NombreArtistico, string Biografia, IFormFile Foto)
        {
            var client = ObtenerClienteConToken();

            var form = new MultipartFormDataContent();
            form.Add(new StringContent(NombreArtistico ?? ""), "nombreArtistico");
            form.Add(new StringContent(Biografia ?? ""), "biografia");

            if (Foto != null && Foto.Length > 0)
            {
                var streamContent = new StreamContent(Foto.OpenReadStream());
                streamContent.Headers.ContentType = new MediaTypeHeaderValue(Foto.ContentType);
                form.Add(streamContent, "foto", Foto.FileName);
            }

            var response = await client.PutAsync("https://localhost:7003/api/artistas/actualizar", form);

            if (response.IsSuccessStatusCode)
            {
                TempData["Mensaje"] = "Perfil actualizado correctamente.";
                return RedirectToAction("VerPerfil");
            }

            TempData["Error"] = "Error al actualizar perfil.";
            return RedirectToAction("VerPerfil");
        }


        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> ActualizarPerfilOyente(string Nombre, string Apellido)
        {
            // Verificar que los datos estén llegando al controlador
            Console.WriteLine($"Nombre: {Nombre}, Apellido: {Apellido}");

            if (string.IsNullOrEmpty(Nombre) || string.IsNullOrEmpty(Apellido))
            {
                TempData["Error"] = "El nombre y apellido no pueden estar vacíos.";
                return RedirectToAction("VerPerfil");
            }

            // Obtener el cliente con el token JWT
            var client = ObtenerClienteConToken();

            var jsonBody = new
            {
                nombre = Nombre,
                apellido = Apellido
            };

            var content = new StringContent(JsonSerializer.Serialize(jsonBody), Encoding.UTF8, "application/json");

            var response = await client.PutAsync("https://localhost:7003/api/oyentes/actualizar", content);
            var respuestaTexto = await response.Content.ReadAsStringAsync();

            if (response.IsSuccessStatusCode)
            {
                TempData["Mensaje"] = "Perfil actualizado correctamente.";
                return RedirectToAction("VerPerfil");
            }

            TempData["Error"] = $"Error al actualizar perfil: {respuestaTexto}";
            return RedirectToAction("VerPerfil");
        }





    }
}