using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SoftfyWeb.Dtos;
using SoftfyWeb.Modelos.Dtos;
using SoftfyWeb.Models;
using System.Diagnostics;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text.Json;

namespace SoftfyWeb.Controllers
{
    [Authorize]
    public class VistasSuscripcionesController : Controller
    {
        private readonly IHttpClientFactory _httpClientFactory;

        public VistasSuscripcionesController(IHttpClientFactory httpClientFactory)
        {
            _httpClientFactory = httpClientFactory;
        }

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
        public IActionResult AccesoDenegado()
        {
            return View();
        }

        // Reutilizable: carga estado y miembros
        private async Task<SuscripcionEstadoDto> CargarEstadoYMiembrosAsync()
        {
            var client = ObtenerClienteConToken();
            var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };

            // Trae estado
            var estado = await client.GetFromJsonAsync<SuscripcionEstadoDto>("suscripciones/estado");
            if (estado == null)
                return null;

            // Si es Premium, trae miembros
            if (estado.Tipo == "Premium")
            {
                var miembros = await client.GetFromJsonAsync<List<MiembroDto>>("suscripciones/miembros");
                estado.Miembros = miembros ?? new List<MiembroDto>();
            }
            return estado;
        }

        // GET: /VistasSuscripciones/Index
        public IActionResult Index() => RedirectToAction(nameof(Estado));

        // GET: /VistasSuscripciones/Estado
        public async Task<IActionResult> Estado()
        {
            // Verifica si el usuario es Artista
            var roles = User.FindAll(ClaimTypes.Role).Select(r => r.Value).ToList();
            if (roles.Contains("Artista"))
            {
                TempData["Error"] = "Los artistas no tienen acceso al estado de suscripción.";
                return RedirectToAction("AccesoDenegado", "VistasSuscripciones"); // O una vista personalizada de acceso denegado
            }

            var model = await CargarEstadoYMiembrosAsync();
            if (model == null)
                return View("Error", CrearErrorModel());

            var currentEmail = User.FindFirstValue(ClaimTypes.Email) ?? User.Identity.Name;
            ViewBag.CurrentEmail = currentEmail;

            return View(model);
        }

        // GET: /VistasSuscripciones/ActivarSuscripcion
        [HttpGet]
        public async Task<IActionResult> ActivarSuscripcion()
        {
            var client = ObtenerClienteConToken();
            var response = await client.GetAsync("planes");
            if (!response.IsSuccessStatusCode)
                return View("Error", CrearErrorModel());

            var planes = await response.Content.ReadFromJsonAsync<List<PlanDto>>();
            return View(planes ?? new List<PlanDto>());
        }

        // POST: /VistasSuscripciones/ActivarSuscripcion
        [HttpPost]
        public async Task<IActionResult> ActivarSuscripcion(int planId)
        {
            var client = ObtenerClienteConToken();
            var response = await client.PostAsJsonAsync("suscripciones/activar", planId);
            if (!response.IsSuccessStatusCode)
                return View("Error", CrearErrorModel());

            // 2) Forzamos relogin limpiando la cookie protegida
            await HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
            TempData["Info"] = "Suscripción activada. Por favor, inicia sesión de nuevo para actualizar tus permisos.";
            return RedirectToAction("Login", "VistasAuth");
        }

        // POST: /VistasSuscripciones/AgregarMiembro
        [HttpPost]
        public async Task<IActionResult> AgregarMiembro(string email)
        {
            var client = ObtenerClienteConToken();
            var dto = new AgregarMiembroDto { Email = email };
            var response = await client.PostAsJsonAsync("suscripciones/agregar-miembro", dto);

            if (!response.IsSuccessStatusCode)
            {
                var errorJson = await response.Content.ReadAsStringAsync();
                var mensaje = "Error al agregar miembro";
                try
                {
                    using var doc = JsonDocument.Parse(errorJson);
                    mensaje = doc.RootElement.GetProperty("mensaje").GetString() ?? mensaje;
                }
                catch { }

                var model = await CargarEstadoYMiembrosAsync();
                ViewBag.ErrorMiembro = mensaje;
                return View("Estado", model);
            }
            return RedirectToAction(nameof(Estado));
        }

        // POST: /VistasSuscripciones/EliminarMiembro
        [HttpPost]
        public async Task<IActionResult> EliminarMiembro(string email)
        {
            var client = ObtenerClienteConToken();
            var dto = new EliminarMiembroDto { Email = email };
            var request = new HttpRequestMessage(HttpMethod.Delete, new Uri(client.BaseAddress + "suscripciones/eliminar-miembro"))
            {
                Content = JsonContent.Create(dto)
            };
            var response = await client.SendAsync(request);
            if (!response.IsSuccessStatusCode)
            {
                var errorJson = await response.Content.ReadAsStringAsync();
                var mensaje = "Error al eliminar miembro";
                try
                {
                    using var doc = JsonDocument.Parse(errorJson);
                    mensaje = doc.RootElement.GetProperty("mensaje").GetString() ?? mensaje;
                }
                catch { }

                var model = await CargarEstadoYMiembrosAsync();
                ViewBag.ErrorMiembro = mensaje;
                return View("Estado", model);
            }
            return RedirectToAction(nameof(Estado));
        }

        // POST: /VistasSuscripciones/SalirDeSuscripcion
        [HttpPost]
        public async Task<IActionResult> SalirDeSuscripcion()
        {
            var client = ObtenerClienteConToken();
            var response = await client.PostAsync("suscripciones/salir-de-suscripcion", null);
            if (!response.IsSuccessStatusCode)
            {
                // ... manejar error ...
                return View("Estado", await CargarEstadoYMiembrosAsync());
            }

            await HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
            TempData["Info"] = "Suscripción cancelada. Por seguridad, inicia sesión de nuevo.";
            return RedirectToAction("Login", "VistasAuth");
        }

        // GET: /VistasSuscripciones/CancelarSuscripcion
        [HttpGet]
        public IActionResult CancelarSuscripcion() => View();

        // POST: /VistasSuscripciones/CancelarSuscripcion
        [HttpPost, ActionName("CancelarSuscripcion")]
        public async Task<IActionResult> ConfirmarCancelarSuscripcion()
        {
            var client = ObtenerClienteConToken();
            var response = await client.PostAsync("suscripciones/cancelar", null);
            if (!response.IsSuccessStatusCode)
            {
                // ... manejar error ...
                return View("Estado", await CargarEstadoYMiembrosAsync());
            }

            await HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
            TempData["Info"] = "Suscripción cancelada. Por seguridad, inicia sesión de nuevo.";
            return RedirectToAction("Login", "VistasAuth");
        }
    }
}
