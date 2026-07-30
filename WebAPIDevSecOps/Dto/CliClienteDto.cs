using System.ComponentModel.DataAnnotations;

namespace WebAPIDevSecOps.Dto
{
    public class CliClienteDto
    {
        public int id { get; set; }
        public string strNombreCliente { get; set; } = null!;
        public string? strDireccionCliente { get; set; }
        public string strCorreoElectronico { get; set; } = null!;
        public string strNumeroTelefono { get; set; } = null!;

        [Timestamp]
        public byte[] RowVersion { get; set; } = null!;

        public string? strCreadoPorUsuario { get; set; }
    }
}
