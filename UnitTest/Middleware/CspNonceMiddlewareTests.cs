using FluentAssertions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Logging;
using Moq;
using WebAPIDevSecOps.Middleware;

namespace UnitTest.Middleware
{
    public class CspNonceMiddlewareTests
    {
        private readonly Mock<ILogger<CspNonceMiddleware>> _loggerMock;

        public CspNonceMiddlewareTests()
        {
            _loggerMock = new Mock<ILogger<CspNonceMiddleware>>();
        }

        private static IWebHostEnvironment CreateEnv(string environmentName)
        {
            return new TestWebHostEnvironment { EnvironmentName = environmentName };
        }

        [Fact]
        public async Task InvokeAsync_DevMode_SetsContentSecurityPolicyWithNonce()
        {
            var env = CreateEnv("Development");
            var context = new DefaultHttpContext();
            var middleware = new CspNonceMiddleware(_ => Task.CompletedTask, env, _loggerMock.Object);

            await middleware.InvokeAsync(context);

            var csp = context.Response.Headers["Content-Security-Policy"].ToString();
            csp.Should().Contain("default-src 'self'");
            csp.Should().Contain("script-src 'self' 'nonce-");
            csp.Should().Contain("style-src 'self' 'unsafe-inline'");
            csp.Should().Contain("img-src 'self' data:");
            csp.Should().Contain("font-src 'self' data:");
            csp.Should().Contain("connect-src 'self'");
        }

        [Fact]
        public async Task InvokeAsync_DevMode_StoresNonceInItems()
        {
            var env = CreateEnv("Development");
            var context = new DefaultHttpContext();
            var middleware = new CspNonceMiddleware(_ => Task.CompletedTask, env, _loggerMock.Object);

            await middleware.InvokeAsync(context);

            var nonce = context.Items["ScriptNonce"]?.ToString();
            nonce.Should().NotBeNullOrEmpty();
            var nonceBytes = Convert.FromBase64String(nonce!);
            nonceBytes.Should().HaveCount(32);
        }

        [Fact]
        public async Task InvokeAsync_DevMode_NonceInCspMatchesItemsNonce()
        {
            var env = CreateEnv("Development");
            var context = new DefaultHttpContext();
            var middleware = new CspNonceMiddleware(_ => Task.CompletedTask, env, _loggerMock.Object);

            await middleware.InvokeAsync(context);

            var nonce = context.Items["ScriptNonce"]?.ToString();
            var csp = context.Response.Headers["Content-Security-Policy"].ToString();
            csp.Should().Contain($"'nonce-{nonce}'");
        }

        [Fact]
        public async Task InvokeAsync_DevMode_CallsNextDelegate()
        {
            var env = CreateEnv("Development");
            var context = new DefaultHttpContext();
            var invoked = false;
            var middleware = new CspNonceMiddleware(_ =>
            {
                invoked = true;
                return Task.CompletedTask;
            }, env, _loggerMock.Object);

            await middleware.InvokeAsync(context);

            invoked.Should().BeTrue();
        }

        [Fact]
        public async Task InvokeAsync_ProductionMode_SetsRestrictiveCsp()
        {
            var env = CreateEnv("Production");
            var context = new DefaultHttpContext();
            var middleware = new CspNonceMiddleware(_ => Task.CompletedTask, env, _loggerMock.Object);

            await middleware.InvokeAsync(context);

            var csp = context.Response.Headers["Content-Security-Policy"].ToString();
            csp.Should().Be("default-src 'none'; frame-ancestors 'none';");
        }

        [Fact]
        public async Task InvokeAsync_ProductionMode_DoesNotSetNonceInItems()
        {
            var env = CreateEnv("Production");
            var context = new DefaultHttpContext();
            var middleware = new CspNonceMiddleware(_ => Task.CompletedTask, env, _loggerMock.Object);

            await middleware.InvokeAsync(context);

            context.Items["ScriptNonce"].Should().BeNull();
        }

        [Fact]
        public async Task InvokeAsync_ProductionMode_CallsNextDelegate()
        {
            var env = CreateEnv("Production");
            var context = new DefaultHttpContext();
            var invoked = false;
            var middleware = new CspNonceMiddleware(_ =>
            {
                invoked = true;
                return Task.CompletedTask;
            }, env, _loggerMock.Object);

            await middleware.InvokeAsync(context);

            invoked.Should().BeTrue();
        }

        [Fact]
        public async Task InvokeAsync_DevMode_GeneratedNonceIsUniquePerRequest()
        {
            var env = CreateEnv("Development");
            var nonces = new List<string>();

            for (int i = 0; i < 10; i++)
            {
                var context = new DefaultHttpContext();
                var middleware = new CspNonceMiddleware(_ => Task.CompletedTask, env, _loggerMock.Object);
                await middleware.InvokeAsync(context);
                nonces.Add(context.Items["ScriptNonce"]!.ToString()!);
            }

            nonces.Distinct().Should().HaveCount(10);
        }

        [Fact]
        public async Task InvokeAsync_DevMode_NonScalarPath_DoesNotReplaceBody()
        {
            var env = CreateEnv("Development");
            var context = new DefaultHttpContext();
            context.Request.Path = "/api/v1/test";
            var originalBody = context.Response.Body;
            var middleware = new CspNonceMiddleware(_ => Task.CompletedTask, env, _loggerMock.Object);

            await middleware.InvokeAsync(context);

            context.Response.Body.Should().BeSameAs(originalBody);
        }

        [Fact]
        public async Task InvokeAsync_ScalarPathHtml_ReemplazaScriptYStyleConNonce()
        {
            var env = CreateEnv("Development");
            var context = new DefaultHttpContext();
            context.Request.Path = "/scalar";
            context.Response.ContentType = "text/html; charset=utf-8";
            var originalBody = new MemoryStream();
            context.Response.Body = originalBody;
            const string html = "<html><script src=\"app.js\"></script><style>.x{}</style></html>";
            var invocations = 0;
            var middleware = new CspNonceMiddleware(async ctx =>
            {
                invocations++;
                await ctx.Response.WriteAsync(html);
            }, env, _loggerMock.Object);

            await middleware.InvokeAsync(context);

            var nonce = context.Items["ScriptNonce"]!.ToString()!;
            originalBody.Seek(0, SeekOrigin.Begin);
            var result = await new StreamReader(originalBody).ReadToEndAsync();
            result.Should().Contain($"<script src=\"app.js\" nonce=\"{nonce}\">");
            result.Should().Contain($"<style nonce=\"{nonce}\">");
            invocations.Should().Be(1);
        }

        [Fact]
        public async Task InvokeAsync_ScalarPathNoHtml_CopiaCuerpoSinModificar()
        {
            var env = CreateEnv("Development");
            var context = new DefaultHttpContext();
            context.Request.Path = "/scalar";
            context.Response.ContentType = "application/json";
            var originalBody = new MemoryStream();
            context.Response.Body = originalBody;
            var invocations = 0;
            var middleware = new CspNonceMiddleware(async ctx =>
            {
                invocations++;
                await ctx.Response.WriteAsync("{\"ok\":true}");
            }, env, _loggerMock.Object);

            await middleware.InvokeAsync(context);

            originalBody.Seek(0, SeekOrigin.Begin);
            var result = await new StreamReader(originalBody).ReadToEndAsync();
            result.Should().Be("{\"ok\":true}");
            invocations.Should().Be(1);
        }
    }

    public class TestWebHostEnvironment : IWebHostEnvironment
    {
        public string EnvironmentName { get; set; } = "Development";
        public string ApplicationName { get; set; } = "Test";
        public string WebRootPath { get; set; } = ".";
        public IFileProvider WebRootFileProvider { get; set; } = new NullFileProvider();
        public string ContentRootPath { get; set; } = ".";
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }
}
