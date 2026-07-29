namespace WebAPIDevSecOps.Events
{
    public class FacturaRechazadaEvent
    {
        public Guid PedidoId { get; set; }
        public string Motivo { get; set; } = null!;
    }
}
