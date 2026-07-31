using FluentAssertions;
using Microsoft.AspNetCore.Http;
using WebAPIDevSecOps.Middleware;

namespace UnitTest.Middleware
{
    public class SecurityHeadersMiddlewareTests
    {
        [Fact]
        public async Task InvokeAsync_SetsXContentTypeOptions()
        {
            var context = new DefaultHttpContext();
            var middleware = new SecurityHeadersMiddleware(_ => Task.CompletedTask);

            await middleware.InvokeAsync(context);

            context.Response.Headers["X-Content-Type-Options"].ToString().Should().Be("nosniff");
        }

        [Fact]
        public async Task InvokeAsync_SetsXFrameOptions()
        {
            var context = new DefaultHttpContext();
            var middleware = new SecurityHeadersMiddleware(_ => Task.CompletedTask);

            await middleware.InvokeAsync(context);

            context.Response.Headers["X-Frame-Options"].ToString().Should().Be("DENY");
        }

        [Fact]
        public async Task InvokeAsync_SetsReferrerPolicy()
        {
            var context = new DefaultHttpContext();
            var middleware = new SecurityHeadersMiddleware(_ => Task.CompletedTask);

            await middleware.InvokeAsync(context);

            context.Response.Headers["Referrer-Policy"].ToString().Should().Be("no-referrer");
        }

        [Fact]
        public async Task InvokeAsync_SetsXXSSProtection()
        {
            var context = new DefaultHttpContext();
            var middleware = new SecurityHeadersMiddleware(_ => Task.CompletedTask);

            await middleware.InvokeAsync(context);

            context.Response.Headers["X-XSS-Protection"].ToString().Should().Be("1; mode=block");
        }

        [Fact]
        public async Task InvokeAsync_SetsStrictTransportSecurity()
        {
            var context = new DefaultHttpContext();
            var middleware = new SecurityHeadersMiddleware(_ => Task.CompletedTask);

            await middleware.InvokeAsync(context);

            context.Response.Headers["Strict-Transport-Security"].ToString().Should().Be("max-age=31536000; includeSubDomains; preload");
        }

        [Fact]
        public async Task InvokeAsync_SetsPermissionsPolicy()
        {
            var context = new DefaultHttpContext();
            var middleware = new SecurityHeadersMiddleware(_ => Task.CompletedTask);

            await middleware.InvokeAsync(context);

            context.Response.Headers["Permissions-Policy"].ToString().Should().Be("geolocation=()");
        }

        [Fact]
        public async Task InvokeAsync_SetsAllHeaders()
        {
            var context = new DefaultHttpContext();
            var middleware = new SecurityHeadersMiddleware(_ => Task.CompletedTask);

            await middleware.InvokeAsync(context);

            context.Response.Headers.Should().ContainKey("X-Content-Type-Options");
            context.Response.Headers.Should().ContainKey("X-Frame-Options");
            context.Response.Headers.Should().ContainKey("Referrer-Policy");
            context.Response.Headers.Should().ContainKey("X-XSS-Protection");
            context.Response.Headers.Should().ContainKey("Strict-Transport-Security");
            context.Response.Headers.Should().ContainKey("Permissions-Policy");
        }

        [Fact]
        public async Task InvokeAsync_CallsNextDelegate()
        {
            var context = new DefaultHttpContext();
            var invoked = false;
            var middleware = new SecurityHeadersMiddleware(_ =>
            {
                invoked = true;
                return Task.CompletedTask;
            });

            await middleware.InvokeAsync(context);

            invoked.Should().BeTrue();
        }

        [Fact]
        public async Task InvokeAsync_DoesNotThrowWhenHeadersAlreadyExist()
        {
            var context = new DefaultHttpContext();
            context.Response.Headers["X-Content-Type-Options"] = "existing-value";
            var middleware = new SecurityHeadersMiddleware(_ => Task.CompletedTask);

            var act = () => middleware.InvokeAsync(context);

            await act.Should().NotThrowAsync();
            context.Response.Headers["X-Content-Type-Options"].ToString().Should().Contain("existing-value");
            context.Response.Headers["X-Content-Type-Options"].ToString().Should().Contain("nosniff");
        }
    }
}
