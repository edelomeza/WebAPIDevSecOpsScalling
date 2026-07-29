using System.ComponentModel.DataAnnotations;

namespace WebAPIDevSecOps.Dto
{
    public class PedidoCreateDto
    {
        [Required]
        [Range(1, int.MaxValue)]
        public int idCliCliente { get; set; }

        [Required]
        [MinLength(1)]
        public List<PedidoDetalleCreateDto> Detalles { get; set; } = new();
    }

    public class PedidoDetalleCreateDto
    {
        [Required]
        [Range(1, int.MaxValue)]
        public int idProProducto { get; set; }

        [Required]
        [Range(1, int.MaxValue)]
        public int intCantidad { get; set; }
    }
}