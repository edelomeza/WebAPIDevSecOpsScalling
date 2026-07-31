using System.ComponentModel.DataAnnotations;

namespace WebAPIDevSecOps.Dto
{
    public class SegUsuarioDto
    {
        public int id { get; set; }

        public string strNombre { get; set; } = null!;

        public string strCorreoElectronico { get; set; } = null!;

        [Timestamp]
        public byte[] RowVersion { get; set; } = null!;
    }
}
