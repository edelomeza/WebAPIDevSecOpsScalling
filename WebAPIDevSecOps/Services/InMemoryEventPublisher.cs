using WebAPIDevSecOps.Interfaces;

namespace WebAPIDevSecOps.Services
{
    public class InMemoryEventPublisher : IEventPublisher
    {
        private readonly ILogger<InMemoryEventPublisher> _logger;

        public InMemoryEventPublisher(ILogger<InMemoryEventPublisher> logger)
        {
            _logger = logger;
        }

        public Task PublishAsync<T>(T eventMessage) where T : class
        {
            _logger.LogInformation("Evento publicado: {EventType} = {@Event}", typeof(T).Name, eventMessage);
            return Task.CompletedTask;
        }
    }
}