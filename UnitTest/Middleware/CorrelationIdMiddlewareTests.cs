using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Moq;
using WebAPIDevSecOps.Middleware;

namespace UnitTest.Middleware
{
    public class CorrelationIdMiddlewareTests
    {
        private readonly Mock<ILogger<CorrelationIdMiddleware>> _loggerMock;

        public CorrelationIdMiddlewareTests()
        {
            _loggerMock = new Mock<ILogger<CorrelationIdMiddleware>>();
        }

        [Fact]
        public async Task InvokeAsync_NoCorrelationIdInRequest_GeneratesNewOneAndSetsResponseHeader()
        {
            var context = new DefaultHttpContext();
            var invoked = false;
            var middleware = new CorrelationIdMiddleware(
                _ => { invoked = true; return Task.CompletedTask; },
                _loggerMock.Object);

            await middleware.InvokeAsync(context);

            invoked.Should().BeTrue();
            context.Response.Headers.Should().ContainKey("X-Correlation-Id");
            var correlationId = context.Response.Headers["X-Correlation-Id"].ToString();
            Guid.TryParse(correlationId, out _).Should().BeTrue();
        }

        [Fact]
        public async Task InvokeAsync_WithCorrelationIdInRequest_PropagatesToResponseHeader()
        {
            var expectedId = "my-custom-correlation-id-123";
            var context = new DefaultHttpContext();
            context.Request.Headers["X-Correlation-Id"] = expectedId;
            var invoked = false;
            var middleware = new CorrelationIdMiddleware(
                _ => { invoked = true; return Task.CompletedTask; },
                _loggerMock.Object);

            await middleware.InvokeAsync(context);

            invoked.Should().BeTrue();
            context.Response.Headers["X-Correlation-Id"].ToString().Should().Be(expectedId);
        }

        [Fact]
        public async Task InvokeAsync_WithEmptyCorrelationId_GeneratesNewOne()
        {
            var context = new DefaultHttpContext();
            context.Request.Headers["X-Correlation-Id"] = "";
            var middleware = new CorrelationIdMiddleware(
                _ => Task.CompletedTask,
                _loggerMock.Object);

            await middleware.InvokeAsync(context);

            var correlationId = context.Response.Headers["X-Correlation-Id"].ToString();
            Guid.TryParse(correlationId, out _).Should().BeTrue();
        }

        [Fact]
        public async Task InvokeAsync_WithWhitespaceCorrelationId_GeneratesNewOne()
        {
            var context = new DefaultHttpContext();
            context.Request.Headers["X-Correlation-Id"] = "   ";
            var middleware = new CorrelationIdMiddleware(
                _ => Task.CompletedTask,
                _loggerMock.Object);

            await middleware.InvokeAsync(context);

            var correlationId = context.Response.Headers["X-Correlation-Id"].ToString();
            Guid.TryParse(correlationId, out _).Should().BeTrue();
        }

        [Fact]
        public async Task InvokeAsync_GeneratedCorrelationIdIsUnique()
        {
            var middleware = new CorrelationIdMiddleware(
                _ => Task.CompletedTask,
                _loggerMock.Object);

            var ids = new List<string>();
            for (int i = 0; i < 10; i++)
            {
                var context = new DefaultHttpContext();
                await middleware.InvokeAsync(context);
                ids.Add(context.Response.Headers["X-Correlation-Id"].ToString());
            }

            ids.Distinct().Should().HaveCount(10);
        }

        [Fact]
        public async Task InvokeAsync_CallsNextDelegate()
        {
            var context = new DefaultHttpContext();
            var callOrder = new List<string>();
            var middleware = new CorrelationIdMiddleware(
                _ =>
                {
                    callOrder.Add("next");
                    return Task.CompletedTask;
                },
                _loggerMock.Object);

            await middleware.InvokeAsync(context);

            callOrder.Should().ContainSingle("next");
        }

        [Fact]
        public async Task InvokeAsync_BeginScopeCalledWithCorrelationId()
        {
            var context = new DefaultHttpContext();
            var middleware = new CorrelationIdMiddleware(
                _ => Task.CompletedTask,
                _loggerMock.Object);

            await middleware.InvokeAsync(context);

            var correlationId = context.Response.Headers["X-Correlation-Id"].ToString();
            _loggerMock.Verify(
                x => x.BeginScope(It.Is<Dictionary<string, object>>(
                    d => d.ContainsKey("CorrelationId") && d["CorrelationId"]!.ToString() == correlationId)),
                Times.Once);
        }

        [Fact]
        public async Task InvokeAsync_CustomCorrelationIdIsPassedToBeginScope()
        {
            var expectedId = "custom-id-for-scope-test";
            var context = new DefaultHttpContext();
            context.Request.Headers["X-Correlation-Id"] = expectedId;
            var middleware = new CorrelationIdMiddleware(
                _ => Task.CompletedTask,
                _loggerMock.Object);

            await middleware.InvokeAsync(context);

            _loggerMock.Verify(
                x => x.BeginScope(It.Is<Dictionary<string, object>>(
                    d => d.ContainsKey("CorrelationId") && d["CorrelationId"]!.ToString() == expectedId)),
                Times.Once);
        }
    }
}
