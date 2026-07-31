using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace WebAPIDevSecOps.Models
{
    [Table("SegUsuario")]
    public class SegUsuario
    {
        [Key]
        public int id { get; set; }

        [StringLength(50)]
        public string strNombre { get; set; } = null!;

        [StringLength(200)]
        public string strPWD { get; set; } = null!;

        [StringLength(50)]
        public string strCorreoElectronico { get; set; } = null!;

        public DateTime? dteFechaRegistro { get; set; }

        public bool bln2FAHabilitado { get; set; }

        [StringLength(200)]
        public string? str2FASecreto { get; set; }

        [Timestamp]
        public byte[] RowVersion { get; set; } = new byte[] { 1 };
    }
}
