namespace WebAPIDevSecOps.Events
{
    public class StockRechazadoEvent
    {
        public Guid PedidoId { get; set; }
        public string Motivo { get; set; } = null!;
    }
}
