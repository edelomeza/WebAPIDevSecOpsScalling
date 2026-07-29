namespace WebAPIDevSecOps.Interfaces
{
    public interface IEventPublisher
    {
        Task PublishAsync<T>(T eventMessage) where T : class;
    }
}