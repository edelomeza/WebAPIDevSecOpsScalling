namespace WebAPIDevSecOps.Events
{
    public class FacturaGeneradoEvent
    {
        public Guid PedidoId { get; set; }
        public string FolioFactura { get; set; } = null!;
    }
}
