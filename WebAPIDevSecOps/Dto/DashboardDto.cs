namespace WebAPIDevSecOps.Dto
{
    public class DashboardDto
    {
        public int intTotalPedidosHoy { get; set; }
        public decimal decVentasHoy { get; set; }
        public List<EstadoSagaCountDto> lstPedidosPorEstado { get; set; } = new();
        public Dictionary<string, int> dctProfundidadColas { get; set; } = new();
    }

    public class EstadoSagaCountDto
    {
        public string strEstadoSaga { get; set; } = null!;
        public int intCantidad { get; set; }
    }
}
