using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

namespace WebAPIDevSecOps.Middleware
{
    public partial class CspNonceMiddleware
    {
        private readonly RequestDelegate _next;
        private readonly IWebHostEnvironment _env;
        private readonly ILogger<CspNonceMiddleware> _logger;

        public CspNonceMiddleware(
            RequestDelegate next,
            IWebHostEnvironment env,
            ILogger<CspNonceMiddleware> logger)
        {
            _next = next;
            _env = env;
            _logger = logger;
        }

        public async Task InvokeAsync(HttpContext context)
        {
            if (_env.IsDevelopment())
            {
                var scriptNonce = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));
                context.Items["ScriptNonce"] = scriptNonce;

                context.Response.Headers.Append("Content-Security-Policy",
                    $"default-src 'self'; " +
                    $"script-src 'self' 'nonce-{scriptNonce}'; " +
                    $"style-src 'self' 'unsafe-inline'; " +
                    $"img-src 'self' data:; " +
                    $"font-src 'self' data:; " +
                    $"connect-src 'self' https://localhost:7227 http://localhost:5196;");

                var path = context.Request.Path.Value;
                if (IsScalarPath(path))
                {
                    var originalBody = context.Response.Body;
                    using var memStream = new MemoryStream();
                    context.Response.Body = memStream;

                    await _next(context);

                    if (context.Response.ContentType?.StartsWith("text/html", StringComparison.OrdinalIgnoreCase) == true)
                    {
                        memStream.Seek(0, SeekOrigin.Begin);
                        var html = await new StreamReader(memStream).ReadToEndAsync();
                        html = ScriptNonceRegex().Replace(html, $"<script$1 nonce=\"{scriptNonce}\">");
                        html = StyleNonceRegex().Replace(html, $"<style$1 nonce=\"{scriptNonce}\">");
                        var bytes = Encoding.UTF8.GetBytes(html);
                        context.Response.ContentLength = bytes.Length;
                        await originalBody.WriteAsync(bytes);
                    }
                    else
                    {
                        memStream.Seek(0, SeekOrigin.Begin);
                        await memStream.CopyToAsync(originalBody);
                    }

                    return;
                }
            }
            else
            {
                context.Response.Headers.Append("Content-Security-Policy",
                    "default-src 'none'; frame-ancestors 'none';");
            }

            await _next(context);
        }

        private static bool IsScalarPath(string? path)
        {
            return string.Equals(path, "/scalar", StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(path, "/scalar/", StringComparison.OrdinalIgnoreCase);
        }

        [GeneratedRegex(@"<script(?![^>]*nonce)([^>]*)>")]
        private static partial Regex ScriptNonceRegex();

        [GeneratedRegex(@"<style(?![^>]*nonce)([^>]*)>")]
        private static partial Regex StyleNonceRegex();
    }
}
