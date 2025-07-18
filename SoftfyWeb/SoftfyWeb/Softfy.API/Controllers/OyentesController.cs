using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SoftfyWeb.Data;
using SoftfyWeb.Modelos;
using SoftfyWeb.Modelos.Dtos;

namespace Softfy.API.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class OyentesController : ControllerBase
    {
        private readonly ApplicationDbContext _context;
        private readonly UserManager<Usuario> _userManager;

        public OyentesController(
            ApplicationDbContext context,
            UserManager<Usuario> userManager)
        {
            _context = context;
            _userManager = userManager;
        }

        [HttpGet("mi-perfil")]
        public async Task<IActionResult> ObtenerMiPerfil()
        {
            // 1) Obtener el Usuario ASP.NET Identity actual
            var usuario = await _userManager.GetUserAsync(User);
            if (usuario == null)
                return Unauthorized(new { mensaje = "No autenticado." });

            // 2) Buscar la entidad Artista asociada
            var oyente = await _context.Users
                .FirstOrDefaultAsync(a => a.Id == usuario.Id);

            if (oyente == null)
                return NotFound(new { mensaje = "Perfil de artista no encontrado." });

            // 3) Devolver solo los campos que interesan
            return Ok(new
            {
                usuario.Nombre,
                usuario.Apellido,
                usuario.TipoUsuario
            });
        }

        [Authorize(Roles = "Oyente, OyentePremium")]
        [HttpPost("actualizar")]
        public async Task<IActionResult> ActualizarPerfil(ActualizarPerfilOyenteDto data)
        {
            var email = User.Identity?.Name;
            if (email == null)
                return BadRequest("El usuario no está autenticado.");

            var usuario = await _userManager.FindByEmailAsync(email);
            if (usuario == null)
                return NotFound("Oyente no encontrado.");

            if (usuario.TipoUsuario != "Oyente" && usuario.TipoUsuario != "OyentePremium")
                return Unauthorized(new { mensaje = "No autorizado para modificar este perfil." });
            usuario.Nombre = data.Nombre;
            usuario.Apellido = data.Apellido;

            var resultado = await _userManager.UpdateAsync(usuario);
            if (!resultado.Succeeded)
                return BadRequest(resultado.Errors);

            return Ok(new
            {
                mensaje = "Perfil actualizado correctamente.",
                nombre = usuario.Nombre,
                apellido = usuario.Apellido
            });
        }




        [HttpGet]
        public async Task<ActionResult<IEnumerable<PerfilOyenteDto>>> ObtenerTodosLosOyentes()
        {
            var oyentes = await _context.Users
                .Include(u => u.Suscripcion)
                    .ThenInclude(s => s.Plan)
                .Where(u => u.TipoUsuario == "Oyente" || u.TipoUsuario == "OyentePremium")
                .ToListAsync();

            var resultado = oyentes.Select(u => new PerfilOyenteDto
            {
                Nombre = u.Nombre,
                Apellido = u.Apellido,
                TipoUsuario = u.TipoUsuario,
                Email = u.Email // ← ¡AQUÍ estaba faltando!
            }).ToList();

            return Ok(resultado);
        }
    }
}