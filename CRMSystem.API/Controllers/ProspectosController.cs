using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using CRMSystem.API.Data;
using CRMSystem.API.Models;
using System.Text.RegularExpressions;

namespace CRMSystem.API.Controllers
{
    /// <summary>
    /// Controlador para la gestión de prospectos (leads) en el sistema CRM
    /// </summary>
    [ApiController]
    [Route("api/[controller]")]
    [Produces("application/json", "text/html")]
    public class ProspectosController : Controller
    {
        private readonly ContextoBDCRM _context;

        public ProspectosController(ContextoBDCRM context)
        {
            _context = context;
        }

        /// <summary>
        /// Obtiene la lista de prospectos con filtros opcionales
        /// </summary>
        /// <param name="sucursalId">ID de la sucursal para filtrar</param>
        /// <param name="fuenteId">ID de la fuente de prospecto para filtrar</param>
        /// <param name="estado">Estado del prospecto (Nuevo, Contactado, Calificado, etc.)</param>
        /// <param name="vendedorId">ID del vendedor asignado</param>
        /// <param name="busqueda">Término de búsqueda en nombre, empresa o email</param>
        /// <param name="pagina">Número de página para paginación</param>
        /// <param name="tamañoPagina">Cantidad de registros por página</param>
        /// <returns>Lista de prospectos o vista parcial HTML para HTMX</returns>
        [HttpGet]
        public async Task<IActionResult> ObtenerProspectos(
            [FromQuery] int? sucursalId = null,
            [FromQuery] int? fuenteId = null,
            [FromQuery] string? estado = null,
            [FromQuery] int? vendedorId = null,
            [FromQuery] string? busqueda = null,
            [FromQuery] int pagina = 1,
            [FromQuery] int tamañoPagina = 50)
        {
            var query = _context.Prospectos
                .Include(p => p.Fuente)
                .Include(p => p.VendedorAsignado)
                .Include(p => p.Sucursal)
                .AsQueryable();

            // Aplicar filtros
            if (sucursalId.HasValue)
                query = query.Where(p => p.SucursalId == sucursalId.Value);

            if (fuenteId.HasValue)
                query = query.Where(p => p.FuenteId == fuenteId.Value);

            if (!string.IsNullOrEmpty(estado))
                query = query.Where(p => p.EstadoProspecto == estado);

            if (vendedorId.HasValue)
                query = query.Where(p => p.VendedorAsignadoId == vendedorId.Value);

            if (!string.IsNullOrEmpty(busqueda))
            {
                query = query.Where(p => 
                    p.NombreContacto.Contains(busqueda) ||
                    p.ApellidoContacto!.Contains(busqueda) ||
                    p.NombreEmpresa.Contains(busqueda) ||
                    p.Email!.Contains(busqueda));
            }

            // Aplicar paginación
            var totalRegistros = await query.CountAsync();
            var prospectos = await query
                .OrderByDescending(p => p.FechaCreacion)
                .Skip((pagina - 1) * tamañoPagina)
                .Take(tamañoPagina)
                .ToListAsync();

            // Agregar headers de paginación
            Response.Headers.Add("X-Total-Count", totalRegistros.ToString());
            Response.Headers.Add("X-Page", pagina.ToString());
            Response.Headers.Add("X-Page-Size", tamañoPagina.ToString());

            // Si la petición es HTMX, devolver vista parcial
            if (Request.Headers["HX-Request"] == "true")
            {
                return PartialView("~/Pages/Partials/_ProspectosList.cshtml", prospectos);
            }

            // Si es petición API normal, devolver JSON
            return Ok(prospectos);
        }

        /// <summary>
        /// Obtiene un prospecto específico por su ID
        /// </summary>
        /// <param name="id">ID del prospecto</param>
        /// <returns>Datos del prospecto o vista parcial HTML para HTMX</returns>
        [HttpGet("{id}")]
        public async Task<IActionResult> ObtenerProspecto(int id)
        {
            var prospecto = await _context.Prospectos
                .Include(p => p.Fuente)
                .Include(p => p.VendedorAsignado)
                .Include(p => p.Sucursal)
                .Include(p => p.Cotizaciones)
                .Include(p => p.Visitas)
                .Include(p => p.Tareas)
                .FirstOrDefaultAsync(p => p.Id == id);

            if (prospecto == null)
            {
                return NotFound(new { mensaje = "Prospecto no encontrado" });
            }

            // Si la petición es HTMX, devolver vista parcial de detalles
            if (Request.Headers["HX-Request"] == "true")
            {
                return PartialView("~/Pages/Partials/_ProspectoDetalle.cshtml", prospecto);
            }

            return Ok(prospecto);
        }

        /// <summary>
        /// Crea un nuevo prospecto en el sistema
        /// </summary>
        /// <param name="dto">Datos del nuevo prospecto</param>
        /// <returns>Prospecto creado con código generado automáticamente</returns>
        [HttpPost]
        public async Task<IActionResult> CrearProspecto([FromBody] CrearProspectoDto dto)
        {
            if (!ModelState.IsValid)
            {
                return BadRequest(ModelState);
            }

            // Validar que existan las entidades relacionadas
            var fuente = await _context.FuentesProspecto.FindAsync(dto.FuenteProspectoId);
            if (fuente == null)
                return BadRequest(new { error = "La fuente especificada no existe" });

            var sucursal = await _context.Sucursales.FindAsync(dto.SucursalId);
            if (sucursal == null)
                return BadRequest(new { error = "La sucursal especificada no existe" });

            if (dto.VendedorAsignadoId.HasValue)
            {
                var vendedor = await _context.Usuarios.FindAsync(dto.VendedorAsignadoId.Value);
                if (vendedor == null)
                    return BadRequest(new { error = "El vendedor especificado no existe" });
            }

            // Generar código de prospecto automáticamente
            var año = DateTime.UtcNow.Year;
            var ultimoProspecto = await _context.Prospectos
                .Where(p => p.CodigoProspecto.StartsWith($"PROS-{año}-"))
                .OrderByDescending(p => p.CodigoProspecto)
                .FirstOrDefaultAsync();

            int siguienteNumero = 1;
            if (ultimoProspecto != null)
            {
                var partes = ultimoProspecto.CodigoProspecto.Split('-');
                if (partes.Length == 3 && int.TryParse(partes[2], out int numero))
                {
                    siguienteNumero = numero + 1;
                }
            }

            // Crear el prospecto desde el DTO
            var prospecto = new Prospecto
            {
                CodigoProspecto = $"PROS-{año}-{siguienteNumero:D3}",
                NombreEmpresa = dto.NombreEmpresa,
                NombreContacto = dto.NombreContacto,
                ApellidoContacto = dto.ApellidoContacto,
                Email = dto.Email,
                Telefono = dto.Telefono,
                FuenteId = dto.FuenteProspectoId,
                SucursalId = dto.SucursalId,
                VendedorAsignadoId = dto.VendedorAsignadoId,
                EstadoProspecto = dto.EstadoProspecto,
                Prioridad = dto.Prioridad,
                ValorEstimado = dto.ValorEstimado ?? 0,
                ProbabilidadCierre = dto.ProbabilidadCierre ?? 50,
                Notas = dto.Notas,
                FechaCreacion = DateTime.UtcNow,
                FechaActualizacion = DateTime.UtcNow
            };

            if (string.IsNullOrEmpty(dto.Prioridad))
            {
                prospecto.Prioridad = "Media";
            }


            _context.Prospectos.Add(prospecto);
            await _context.SaveChangesAsync();

            // Cargar las relaciones para la respuesta
            await _context.Entry(prospecto)
                .Reference(p => p.Fuente)
                .LoadAsync();
            await _context.Entry(prospecto)
                .Reference(p => p.Sucursal)
                .LoadAsync();
            if (prospecto.VendedorAsignadoId.HasValue)
            {
                await _context.Entry(prospecto)
                    .Reference(p => p.VendedorAsignado)
                    .LoadAsync();
            }

            // Si la petición es HTMX, devolver lista actualizada
            if (Request.Headers["HX-Request"] == "true")
            {
                var prospectos = await _context.Prospectos
                    .Include(p => p.Fuente)
                    .Include(p => p.VendedorAsignado)
                    .Include(p => p.Sucursal)
                    .OrderByDescending(p => p.FechaCreacion)
                    .Take(50)
                    .ToListAsync();

                Response.Headers.Append("X-Success-Message", "Prospecto creado exitosamente");
                return PartialView("~/Pages/Partials/_ProspectosList.cshtml", prospectos);
            }

            return CreatedAtAction(nameof(ObtenerProspecto), new { id = prospecto.Id }, prospecto);
        }

        /// <summary>
        /// Actualiza un prospecto existente
        /// </summary>
        /// <param name="id">ID del prospecto a actualizar</param>
        /// <param name="prospecto">Datos actualizados del prospecto</param>
        /// <returns>Prospecto actualizado</returns>
        [HttpPut("{id}")]
        public async Task<IActionResult> ActualizarProspecto(int id, [FromBody] Prospecto prospecto)
        {
            if (id != prospecto.Id)
            {
                return BadRequest(new { mensaje = "El ID del prospecto no coincide" });
            }

            if (!ModelState.IsValid)
            {
                return BadRequest(ModelState);
            }

            var prospectoExistente = await _context.Prospectos.FindAsync(id);
            if (prospectoExistente == null)
            {
                return NotFound(new { mensaje = "Prospecto no encontrado" });
            }

            // Actualizar campos
            prospectoExistente.NombreEmpresa = prospecto.NombreEmpresa;
            prospectoExistente.NombreContacto = prospecto.NombreContacto;
            prospectoExistente.ApellidoContacto = prospecto.ApellidoContacto;
            prospectoExistente.CargoContacto = prospecto.CargoContacto;
            prospectoExistente.Email = prospecto.Email;
            prospectoExistente.Telefono = prospecto.Telefono;
            prospectoExistente.Industria = prospecto.Industria;
            prospectoExistente.TamanoEmpresa = prospecto.TamanoEmpresa;
            prospectoExistente.Direccion = prospecto.Direccion;
            prospectoExistente.Ciudad = prospecto.Ciudad;
            prospectoExistente.EstadoDireccion = prospecto.EstadoDireccion;
            prospectoExistente.Pais = prospecto.Pais;
            prospectoExistente.FuenteId = prospecto.FuenteId;
            prospectoExistente.EstadoProspecto = prospecto.EstadoProspecto;
            prospectoExistente.Prioridad = prospecto.Prioridad;
            prospectoExistente.ValorEstimado = prospecto.ValorEstimado;
            prospectoExistente.ProbabilidadCierre = prospecto.ProbabilidadCierre;
            prospectoExistente.FechaEstimadaCierre = prospecto.FechaEstimadaCierre;
            prospectoExistente.VendedorAsignadoId = prospecto.VendedorAsignadoId;
            prospectoExistente.Notas = prospecto.Notas;
            prospectoExistente.FechaActualizacion = DateTime.Now;

            try
            {
                await _context.SaveChangesAsync();
            }
            catch (DbUpdateConcurrencyException)
            {
                if (!await ProspectoExiste(id))
                {
                    return NotFound(new { mensaje = "Prospecto no encontrado" });
                }
                throw;
            }

            // Si la petición es HTMX, devolver lista actualizada
            if (Request.Headers["HX-Request"] == "true")
            {
                var prospectos = await _context.Prospectos
                    .Include(p => p.Fuente)
                    .Include(p => p.VendedorAsignado)
                    .Include(p => p.Sucursal)
                    .OrderByDescending(p => p.FechaCreacion)
                    .Take(50)
                    .ToListAsync();

                Response.Headers.Add("X-Success-Message", "Prospecto actualizado exitosamente");
                return PartialView("~/Pages/Partials/_ProspectosList.cshtml", prospectos);
            }

            return Ok(prospectoExistente);
        }

        /// <summary>
        /// Elimina un prospecto del sistema
        /// </summary>
        /// <param name="id">ID del prospecto a eliminar</param>
        /// <returns>Confirmación de eliminación</returns>
        [HttpDelete("{id}")]
        public async Task<IActionResult> EliminarProspecto(int id)
        {
            var prospecto = await _context.Prospectos.FindAsync(id);
            if (prospecto == null)
            {
                return NotFound(new { mensaje = "Prospecto no encontrado" });
            }

            _context.Prospectos.Remove(prospecto);
            await _context.SaveChangesAsync();

            return Ok(new { mensaje = "Prospecto eliminado exitosamente" });
        }

        /// <summary>
        /// Convierte un prospecto en cliente
        /// </summary>
        /// <param name="id">ID del prospecto a convertir</param>
        /// <returns>Cliente creado a partir del prospecto</returns>
        [HttpPost("{id}/convertir-a-cliente")]
        public async Task<IActionResult> ConvertirACliente(int id)
        {
            var prospecto = await _context.Prospectos
                .Include(p => p.Sucursal)
                .FirstOrDefaultAsync(p => p.Id == id);

            if (prospecto == null)
            {
                return NotFound(new { mensaje = "Prospecto no encontrado" });
            }

            // Generar código de cliente
            var año = DateTime.Now.Year;
            var ultimoCliente = await _context.Clientes
                .Where(c => c.CodigoCliente.StartsWith($"CLI-{año}-"))
                .OrderByDescending(c => c.CodigoCliente)
                .FirstOrDefaultAsync();

            int siguienteNumero = 1;
            if (ultimoCliente != null)
            {
                var partes = ultimoCliente.CodigoCliente.Split('-');
                if (partes.Length == 3 && int.TryParse(partes[2], out int numero))
                {
                    siguienteNumero = numero + 1;
                }
            }

            // Crear nuevo cliente a partir del prospecto
            var cliente = new Cliente
            {
                CodigoCliente = $"CLI-{año}-{siguienteNumero:D3}",
                NombreEmpresa = prospecto.NombreEmpresa,
                Industria = prospecto.Industria,
                Telefono = prospecto.Telefono,
                Email = prospecto.Email,
                Direccion = prospecto.Direccion,
                Ciudad = prospecto.Ciudad,
                Estado = prospecto.EstadoDireccion,
                Pais = prospecto.Pais,
                CategoriaId = 4, // Categoría "Nuevo" por defecto
                SucursalId = prospecto.SucursalId,
                VendedorAsignadoId = prospecto.VendedorAsignadoId,
                EstadoCliente = "Activo",
                FechaRegistro = DateTime.Now,
                FechaCreacion = DateTime.Now,
                FechaActualizacion = DateTime.Now
            };

            _context.Clientes.Add(cliente);

            // Actualizar prospecto
            prospecto.EstadoProspecto = "Ganado";
            prospecto.FechaConversion = DateTime.Now;
            prospecto.FechaActualizacion = DateTime.Now;

            await _context.SaveChangesAsync();

            return Ok(new 
            { 
                mensaje = "Prospecto convertido a cliente exitosamente",
                clienteId = cliente.Id,
                codigoCliente = cliente.CodigoCliente
            });
        }

        /// <summary>
        /// Obtiene estadísticas del embudo de ventas
        /// </summary>
        /// <returns>Datos del embudo de ventas por estado</returns>
        [HttpGet("embudo-ventas")]
        public async Task<IActionResult> ObtenerEmbudoVentas()
        {
            var embudo = await _context.Prospectos
                .GroupBy(p => p.EstadoProspecto)
                .Select(g => new 
                {
                    Estado = g.Key,
                    Cantidad = g.Count(),
                    ValorTotal = g.Sum(p => p.ValorEstimado ?? 0)
                })
                .ToListAsync();

            return Ok(embudo);
        }

        /// <summary>
        /// Obtiene fuentes de prospectos disponibles
        /// </summary>
        /// <returns>Lista de fuentes de prospectos</returns>
        [HttpGet("fuentes")]
        public async Task<IActionResult> ObtenerFuentes()
        {
            var fuentes = await _context.FuentesProspecto.ToListAsync();
            return Ok(fuentes);
        }

        private async Task<bool> ProspectoExiste(int id)
        {
            return await _context.Prospectos.AnyAsync(p => p.Id == id);
        }
    }
}

