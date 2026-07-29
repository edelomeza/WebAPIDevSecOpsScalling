using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace WebAPIDevSecOps.Models
{
    [Table("VenPedido")]
    public class VenPedido
    {
        [Key]
        public Guid id { get; set; }

        public int idCliCliente { get; set; }

        [Required]
        public DateTime dteFechaPedido { get; set; }

        [Required]
        public decimal decTotal { get; set; }

        [Required]
        [StringLength(50)]
        public string strEstadoSaga { get; set; } = null!;

        [StringLength(500)]
        public string? strMotivoRechazo { get; set; }

        [Timestamp]
        public byte[] RowVersion { get; set; } = new byte[] { 1 };

        [ForeignKey("idCliCliente")]
        public CliCliente? CliCliente { get; set; }
    }
}