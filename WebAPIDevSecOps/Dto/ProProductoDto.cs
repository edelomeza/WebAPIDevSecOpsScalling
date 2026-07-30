using System.ComponentModel.DataAnnotations;

namespace WebAPIDevSecOps.Dto
{
    public class ProProductoDto
    {
        public int id { get; set; }
        public string strNombreProducto { get; set; } = null!;
        public string? strURLImagen { get; set; }
        public string? strDescripcion { get; set; }
        public int intNumeroExistencia { get; set; }
        public decimal decPrecio { get; set; }

        [Timestamp]
        public byte[] RowVersion { get; set; } = null!;

        public string? strCreadoPorUsuario { get; set; }
    }
}
