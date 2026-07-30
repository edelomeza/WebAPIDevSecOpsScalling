using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace WebAPIDevSecOps.Models
{
    [Table("ProProducto")]
    public class ProProducto
    {
        [Key]
        public int id { get; set; }

        [Required]
        [StringLength(50)]
        public string strNombreProducto { get; set; } = null!;

        [StringLength(300)]
        public string? strURLImagen { get; set; }

        [StringLength(250)]
        public string? strDescripcion { get; set; }

        [Required]
        public int intNumeroExistencia { get; set; }

        [Required]
        public decimal decPrecio { get; set; }

        [Timestamp]
        public byte[] RowVersion { get; set; } = new byte[] { 1 };

        [StringLength(50)]
        public string? strCreadoPorUsuario { get; set; }
    }
}
