namespace WebAPIDevSecOps.Dto
{
    public class PagoResponseDto
    {
        public int id { get; set; }
        public Guid idVenPedido { get; set; }
        public decimal decMonto { get; set; }
        public string? strMetodoPago { get; set; }
        public string? strIdTransaccion { get; set; }
        public string strEstado { get; set; } = null!;
        public DateTime dteFechaPago { get; set; }
    }
}
