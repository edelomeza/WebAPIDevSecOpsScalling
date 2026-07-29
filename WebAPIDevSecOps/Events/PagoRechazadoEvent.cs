namespace WebAPIDevSecOps.Events
{
    public class PagoRechazadoEvent
    {
        public Guid PedidoId { get; set; }
        public string Motivo { get; set; } = null!;
    }
}
