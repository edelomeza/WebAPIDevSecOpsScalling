namespace WebAPIDevSecOps.Events
{
    public class PagoProcesadoEvent
    {
        public Guid PedidoId { get; set; }
        public string IdTransaccion { get; set; } = null!;
        public decimal Monto { get; set; }
    }
}
