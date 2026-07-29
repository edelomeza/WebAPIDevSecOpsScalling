using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace WebAPIDevSecOps.Models
{
    [Table("VenPedidoPago")]
    public class VenPedidoPago
    {
        [Key]
        public int id { get; set; }

        [Required]
        public Guid idVenPedido { get; set; }

        [Required]
        public decimal decMonto { get; set; }

        [StringLength(50)]
        public string? strMetodoPago { get; set; }

        [StringLength(100)]
        public string? strIdTransaccion { get; set; }

        [Required]
        [StringLength(20)]
        public string strEstado { get; set; } = null!;

        [Required]
        public DateTime dteFechaPago { get; set; }

        [Timestamp]
        public byte[] RowVersion { get; set; } = new byte[] { 1 };

        [ForeignKey("idVenPedido")]
        public VenPedido? VenPedido { get; set; }
    }
}