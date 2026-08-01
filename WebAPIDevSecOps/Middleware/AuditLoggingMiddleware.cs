using System.Diagnostics;
using System.Security.Claims;
using WebAPIDevSecOps.Dto;

namespace WebAPIDevSecOps.Middleware;

public class AuditLoggingMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<AuditLoggingMiddleware> _logger;

    public AuditLoggingMiddleware(RequestDelegate next, ILogger<AuditLoggingMiddleware> logger)
    {
        _next = next;
        _logger = logger;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        var sw = Stopwatch.StartNew();
        var originalBodyStream = context.Response.Body;

        using var responseBody = new MemoryStream();
        context.Response.Body = responseBody;

        try
        {
            await _next(context);
        }
        finally
        {
            sw.Stop();
            responseBody.Seek(0, SeekOrigin.Begin);
            await responseBody.CopyToAsync(originalBodyStream);
            context.Response.Body = originalBodyStream;

            var user = context.User?.Identity?.Name
                       ?? context.User?.FindFirst(ClaimTypes.NameIdentifier)?.Value;

            var entry = new AuditLogEntry
            {
                Timestamp = DateTime.UtcNow.ToString("O"),
                HttpMethod = context.Request.Method,
                Path = context.Request.Path,
                StatusCode = context.Response.StatusCode,
                ResponseTimeMs = sw.ElapsedMilliseconds,
                User = user,
                UserAgent = context.Request.Headers["User-Agent"]
            };

            var content = AuditHashChain.BuildContent(entry);
            (entry.PrevHash, entry.Hash) = AuditHashChain.Append(content);

            _logger.LogInformation(
                "Audit: {Method} {Path} -> {StatusCode} | {ResponseTimeMs}ms | User={User} | Agent={Agent} | PrevHash={PrevHash} | Hash={Hash}",
                entry.HttpMethod, entry.Path, entry.StatusCode,
                entry.ResponseTimeMs, entry.User, entry.UserAgent,
                entry.PrevHash, entry.Hash);
        }
    }
}
