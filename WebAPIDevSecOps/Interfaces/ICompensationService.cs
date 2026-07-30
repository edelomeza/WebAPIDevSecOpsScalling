namespace WebAPIDevSecOps.Interfaces
{
    public interface ICompensationService
    {
        Task CompensarPorPagoRechazadoAsync(Guid pedidoId);
        Task CompensarPorFacturaRechazadaAsync(Guid pedidoId);
    }
}
