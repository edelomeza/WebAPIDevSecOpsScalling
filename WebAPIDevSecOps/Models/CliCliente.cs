using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace WebAPIDevSecOps.Models
{
    [Table("CliCliente")]
    public class CliCliente
    {
        [Key]
        public int id { get; set; }

        [Required]
        [StringLength(100)]
        public string strNombreCliente { get; set; } = null!;

        [StringLength(200)]
        public string? strDireccionCliente { get; set; }

        [Timestamp]
        public byte[] RowVersion { get; set; } = new byte[] { 1 };

        [Required]
        [StringLength(100)]
        public string strCorreoElectronico { get; set; } = null!;

        [Required]
        [StringLength(10)]
        public string strNumeroTelefono { get; set; } = null!;

        [StringLength(50)]
        public string? strCreadoPorUsuario { get; set; }
    }
}
