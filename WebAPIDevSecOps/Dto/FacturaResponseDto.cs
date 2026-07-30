namespace WebAPIDevSecOps.Dto
{
    public class FacturaResponseDto
    {
        public int id { get; set; }
        public Guid idVenPedido { get; set; }
        public string strFolioFactura { get; set; } = null!;
        public string? strRFC { get; set; }
        public decimal decTotal { get; set; }
        public DateTime dteFechaEmision { get; set; }
        public string strEstado { get; set; } = null!;
    }
}
