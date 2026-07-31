using System.Diagnostics;
using System.Text.RegularExpressions;

namespace WebAPIDevSecOps.Middleware
{
    public partial class CorrelationIdMiddleware
    {
        private readonly RequestDelegate _next;
        private readonly ILogger<CorrelationIdMiddleware> _logger;

        private const int MaxCorrelationIdLength = 100;

        public CorrelationIdMiddleware(RequestDelegate next, ILogger<CorrelationIdMiddleware> logger)
        {
            _next = next;
            _logger = logger;
        }

        public async Task InvokeAsync(HttpContext context)
        {
            var correlationId = context.Request.Headers["X-Correlation-Id"].FirstOrDefault();

            if (string.IsNullOrWhiteSpace(correlationId) || !IsValidCorrelationId(correlationId))
            {
                if (!string.IsNullOrWhiteSpace(correlationId))
                {
                    _logger.LogWarning("X-Correlation-Id descartado por formato inválido: {CorrelationId}", correlationId);
                }
                correlationId = Guid.NewGuid().ToString();
            }

            context.Response.Headers["X-Correlation-Id"] = correlationId;

            using (_logger.BeginScope(new Dictionary<string, object>
            {
                ["CorrelationId"] = correlationId
            }))
            {
                Activity.Current?.AddTag("CorrelationId", correlationId);
                await _next(context);
            }
        }

        private static bool IsValidCorrelationId(string correlationId)
        {
            return correlationId.Length <= MaxCorrelationIdLength &&
                   ValidCorrelationIdRegex().IsMatch(correlationId);
        }

        [GeneratedRegex(@"^[A-Za-z0-9\-_.]+$")]
        private static partial Regex ValidCorrelationIdRegex();
    }
}
