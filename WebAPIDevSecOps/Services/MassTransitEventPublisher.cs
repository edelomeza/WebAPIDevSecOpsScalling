using MassTransit;
using WebAPIDevSecOps.Interfaces;

namespace WebAPIDevSecOps.Services
{
    public class MassTransitEventPublisher : IEventPublisher
    {
        private readonly IPublishEndpoint _publishEndpoint;
        private readonly ILogger<MassTransitEventPublisher> _logger;

        public MassTransitEventPublisher(IPublishEndpoint publishEndpoint, ILogger<MassTransitEventPublisher> logger)
        {
            _publishEndpoint = publishEndpoint;
            _logger = logger;
        }

        public async Task PublishAsync<T>(T eventMessage) where T : class
        {
            _logger.LogInformation("Publicando evento via MassTransit: {EventType}", typeof(T).Name);
            await _publishEndpoint.Publish(eventMessage);
        }
    }
}
