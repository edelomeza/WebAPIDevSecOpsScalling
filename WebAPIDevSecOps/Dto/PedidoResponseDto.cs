namespace WebAPIDevSecOps.Dto
{
    public class PedidoResponseDto
    {
        public Guid id { get; set; }
        public int idCliCliente { get; set; }
        public string? strNombreCliente { get; set; }
        public DateTime dteFechaPedido { get; set; }
        public decimal decTotal { get; set; }
        public string strEstadoSaga { get; set; } = null!;
        public string? strMotivoRechazo { get; set; }
        public byte[] RowVersion { get; set; } = Array.Empty<byte>();
        public List<PedidoDetalleResponseDto> Detalles { get; set; } = new();
    }

    public class PedidoDetalleResponseDto
    {
        public int id { get; set; }
        public int idProProducto { get; set; }
        public string? strNombreProducto { get; set; }
        public int intCantidad { get; set; }
        public decimal decPrecioUnitario { get; set; }
    }
}