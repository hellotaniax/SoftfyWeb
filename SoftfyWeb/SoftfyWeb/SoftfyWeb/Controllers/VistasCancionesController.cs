using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Newtonsoft.Json;
using SoftfyWeb.Dtos;
using SoftfyWeb.Modelos;
using SoftfyWeb.Modelos.Dtos;
using SoftfyWeb.Models;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.NetworkInformation;
using System.Text.Json;
using System.Threading.Tasks;

namespace SoftfyWeb.Controllers
{
    [Authorize]
    public class VistasCancionesController : Controller
    {
        private readonly IHttpClientFactory _httpClientFactory;

        public VistasCancionesController(IHttpClientFactory httpClientFactory)
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

        private HttpClient ObtenerCliente()
        {
            return _httpClientFactory.CreateClient("SoftfyApi");
        }

        private ErrorViewModel CrearErrorModel()
        {
            string id = Activity.Current?.Id ?? HttpContext.TraceIdentifier;
            return new ErrorViewModel { RequestId = id };
        }

        // 1) Listar todas las canciones (para oyentes)
        [AllowAnonymous]
        public async Task<IActionResult> Index()
        {
            HttpClient client = ObtenerClienteConToken();
            HttpResponseMessage response = await client.GetAsync("canciones/canciones");
            if (!response.IsSuccessStatusCode)
                return View("Error", CrearErrorModel());

            var json = await response.Content.ReadAsStringAsync();
            var opciones = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
            var lista = JsonConvert.DeserializeObject<List<CancionRespuestaDto>>(json);

            return View(lista);
        }


        // 2) Listar mis canciones (solo Artista)
        // --- MisCanciones (ARTISTA sólo) ---
        [Authorize(Roles = "Artista")]
        public async Task<IActionResult> MisCanciones()
        {
            var client = ObtenerClienteConToken();
            var response = await client.GetAsync("canciones/mis-canciones");
            if (!response.IsSuccessStatusCode)
                return View("Error", CrearErrorModel());

            var json = await response.Content.ReadAsStringAsync();
            var opts = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
            var lista = JsonConvert.DeserializeObject<List<CancionRespuestaDto>>(json);

            return View(lista);
        }



        // 3) Formulario de creación
        [Authorize(Roles = "Artista")]
        public IActionResult CrearCancion()
        {
            return View(new CancionCrearDto());
        }

        // --- CrearCancion POST ---
        [HttpPost, Authorize(Roles = "Artista"), ValidateAntiForgeryToken]
        public async Task<IActionResult> CrearCancion(CancionCrearDto dto, IFormFile archivoCancion)
        {
            if (!ModelState.IsValid) return View(dto);

            if (archivoCancion == null || archivoCancion.Length == 0)
            {
                ModelState.AddModelError("", "Debes seleccionar un archivo.");
                return View(dto);
            }

            var form = new MultipartFormDataContent();
            form.Add(new StringContent(dto.Titulo), nameof(dto.Titulo));
            form.Add(new StringContent(dto.Genero ?? ""), nameof(dto.Genero));
            form.Add(new StringContent(dto.FechaLanzamiento.ToString("o")),
                     nameof(dto.FechaLanzamiento));

            var stream = new StreamContent(archivoCancion.OpenReadStream());
            stream.Headers.ContentType =
                new MediaTypeHeaderValue(archivoCancion.ContentType);
            form.Add(stream, "archivoCancion", archivoCancion.FileName);

            var client = ObtenerClienteConToken();
            var response = await client.PostAsync("canciones/crear", form);
            if (response.IsSuccessStatusCode)
                return RedirectToAction("MisCanciones");

            var err = await response.Content.ReadAsStringAsync();
            ModelState.AddModelError("", err);
            return View(dto);
        }
        [HttpPost]
        public async Task<IActionResult> EliminarCancion(int id)
        {
            var client = ObtenerClienteConToken(); // Llamar al método actualizado
            var response = await client.DeleteAsync($"https://localhost:7003/api/canciones/eliminar/{id}");

            if (!response.IsSuccessStatusCode)
                TempData["Error"] = "No se pudo eliminar la canción.";

            return RedirectToAction("MisCanciones");
        }

        [HttpGet]
        [Authorize(Roles = "Artista")]
        public async Task<IActionResult> EditarCancion(int id)
        {
            var client = ObtenerClienteConToken();
            var response = await client.GetAsync($"canciones/{id}");
            if (!response.IsSuccessStatusCode)
                return RedirectToAction("MisCanciones");

            var json = await response.Content.ReadAsStringAsync();
            var cancion = Newtonsoft.Json.JsonConvert.DeserializeObject<CancionCrearDto>(json);
            var dto = new CancionCrearDto   
            {
                Titulo = cancion.Titulo,
                Genero = cancion.Genero,
                FechaLanzamiento = cancion.FechaLanzamiento
            };
            ViewBag.CancionId = id;
            return View("Editar", dto);
        }

        [HttpPost]
        [Authorize(Roles = "Artista")]
        public async Task<IActionResult> ActualizarCancion(int id, [FromForm] CancionCrearDto song, IFormFile? nuevoArchivo)
        {
            if (song == null || string.IsNullOrEmpty(song.Titulo) || string.IsNullOrEmpty(song.Genero))
            {
                ModelState.AddModelError("", "Por favor, complete todos los campos.");
                return View("Editar", song);  // Regresar a la vista con los datos actuales
            }

            var client = ObtenerClienteConToken();
            var formData = new MultipartFormDataContent();
            formData.Add(new StringContent(song.Titulo), "Titulo");
            formData.Add(new StringContent(song.Genero), "Genero");
            formData.Add(new StringContent(song.FechaLanzamiento.ToString("yyyy-MM-dd")), "FechaLanzamiento");

            if (nuevoArchivo != null)
            {
                var streamContent = new StreamContent(nuevoArchivo.OpenReadStream());
                streamContent.Headers.Add("Content-Type", nuevoArchivo.ContentType);
                formData.Add(streamContent, "nuevoArchivo", nuevoArchivo.FileName);
            }

            var response = await client.PutAsync($"canciones/editar/{id}", formData); // Aquí usamos PUT ya que es la acción de actualización en la API

            if (!response.IsSuccessStatusCode)
            {
                ViewBag.Error = "Hubo un problema al actualizar la canción.";
                return View("Editar", song);  // Regresar a la vista si hay error
            }

            ViewBag.Mensaje = "Canción actualizada correctamente.";
            return RedirectToAction("MisCanciones");  // Redirigir a la página de Mis Canciones
        }
        [AllowAnonymous]
        [HttpGet]
        public async Task<IActionResult> VerCanciones(int id)
        {
            var client = ObtenerClienteConToken();

            var cancionesResponse = await client.GetAsync($"https://localhost:7003/api/playlists/{id}/canciones");

            var playlistResponse = await client.GetAsync($"https://localhost:7003/api/Playlists/{id}");

            if (!cancionesResponse.IsSuccessStatusCode || !playlistResponse.IsSuccessStatusCode)
            {
                ViewBag.Error = "No se pudieron obtener las canciones de la playlist o los detalles del álbum.";
                return View(new List<Cancion>());
            }
            var cancionesContenido = await cancionesResponse.Content.ReadAsStringAsync();
            var canciones = System.Text.Json.JsonSerializer.Deserialize<List<Cancion>>(cancionesContenido,
                new System.Text.Json.JsonSerializerOptions { PropertyNameCaseInsensitive = true });

            var playlistContenido = await playlistResponse.Content.ReadAsStringAsync();
            var playlist = System.Text.Json.JsonSerializer.Deserialize<Playlist>(playlistContenido, new System.Text.Json.JsonSerializerOptions { PropertyNameCaseInsensitive = true });

            if (canciones == null || playlist == null)
            {
                ViewBag.Error = "No se encontraron canciones o la playlist no existe.";
                return View(new List<Cancion>());
            }

            ViewBag.PlaylistNombre = playlist.Nombre;
            ViewBag.PlaylistId = id;

            foreach (var cancion in canciones)
            {
                if (string.IsNullOrWhiteSpace(cancion.UrlArchivo))
                {
                    cancion.UrlArchivo = "#";
                }
            }

            return View("VerCanciones", canciones);
        }

        [AllowAnonymous]
        public IActionResult Error()
        {
            return View(CrearErrorModel());
        }
    }
}
