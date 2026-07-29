namespace WebAPIDevSecOps.Events
{
    public class PedidoCreadoEvent
    {
        public Guid PedidoId { get; set; }
        public int ClienteId { get; set; }
        public decimal Total { get; set; }
        public List<PedidoCreadoDetalleItem> Detalles { get; set; } = new();
        public DateTime FechaCreacion { get; set; }
    }

    public class PedidoCreadoDetalleItem
    {
        public int idProProducto { get; set; }
        public int intCantidad { get; set; }
        public decimal decPrecioUnitario { get; set; }
    }
}
