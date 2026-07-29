using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace WebAPIDevSecOps.Models
{
    [Table("VenPedidoFactura")]
    public class VenPedidoFactura
    {
        [Key]
        public int id { get; set; }

        [Required]
        public Guid idVenPedido { get; set; }

        [Required]
        [StringLength(50)]
        public string strFolioFactura { get; set; } = null!;

        [StringLength(13)]
        public string? strRFC { get; set; }

        [Required]
        public decimal decTotal { get; set; }

        [Required]
        public DateTime dteFechaEmision { get; set; }

        [Required]
        [StringLength(20)]
        public string strEstado { get; set; } = null!;

        [Timestamp]
        public byte[] RowVersion { get; set; } = new byte[] { 1 };

        [ForeignKey("idVenPedido")]
        public VenPedido? VenPedido { get; set; }
    }
}