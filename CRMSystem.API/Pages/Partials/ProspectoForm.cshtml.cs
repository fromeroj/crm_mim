using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using CRMSystem.API.Data;
using CRMSystem.API.Models;

namespace CRMSystem.API.Pages.Partials
{
    public class ProspectoFormModel : PageModel
    {
        private readonly ContextoBDCRM _context;

        public ProspectoFormModel(ContextoBDCRM context)
        {
            _context = context;
        }

        [BindProperty(Name = "id", SupportsGet = true)]
        public int? ProspectoId { get; set; }

        public Prospecto? Prospecto { get; set; }

        public IEnumerable<FuenteProspecto> Fuentes { get; set; } = new List<FuenteProspecto>();

        public async Task OnGetAsync()
        {
            // Load fuentes for the dropdown
            Fuentes = await _context.FuentesProspecto.ToListAsync();

            if (ProspectoId.HasValue)
            {
                Prospecto = await _context.Prospectos
                    .Include(p => p.Fuente)
                    .Include(p => p.VendedorAsignado)
                    .Include(p => p.Sucursal)
                    .FirstOrDefaultAsync(p => p.Id == ProspectoId.Value);
            }
        }
    }
}

