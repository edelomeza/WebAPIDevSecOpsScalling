using System.Text;
using System.Text.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Moq;
using UnitTest.Common;
using WebAPIDevSecOps.Middleware;

namespace UnitTest.Middleware
{
    public class ExceptionHandlingMiddlewareTests
    {
        private readonly Mock<ILogger<ExceptionHandlingMiddleware>> _loggerMock;

        public ExceptionHandlingMiddlewareTests()
        {
            _loggerMock = new Mock<ILogger<ExceptionHandlingMiddleware>>();
        }

        private static DefaultHttpContext CreateContext()
        {
            var context = new DefaultHttpContext();
            context.Response.Body = new MemoryStream();
            return context;
        }

        private static async Task<string> ReadBodyAsync(DefaultHttpContext context)
        {
            context.Response.Body.Seek(0, SeekOrigin.Begin);
            using var reader = new StreamReader(context.Response.Body, Encoding.UTF8);
            return await reader.ReadToEndAsync();
        }

        [Fact]
        public async Task InvokeAsync_DbUpdateConcurrencyException_Returns409()
        {
            var context = CreateContext();
            var middleware = new ExceptionHandlingMiddleware(
                _ => throw new DbUpdateConcurrencyException("conflicto"),
                _loggerMock.Object);

            await middleware.InvokeAsync(context);

            context.Response.StatusCode.Should().Be(StatusCodes.Status409Conflict);
            var body = await ReadBodyAsync(context);
            body.Should().Contain("El registro fue modificado por otro usuario.");
            body.Should().Contain("\"statusCode\":409");
            LogVerifier.VerifyLog(_loggerMock, LogLevel.Warning, "Conflicto de concurrencia", Times.Once());
        }

        [Fact]
        public async Task InvokeAsync_KeyNotFoundException_Returns404WithMessage()
        {
            var context = CreateContext();
            var middleware = new ExceptionHandlingMiddleware(
                _ => throw new KeyNotFoundException("Venta no encontrada."),
                _loggerMock.Object);

            await middleware.InvokeAsync(context);

            context.Response.StatusCode.Should().Be(StatusCodes.Status404NotFound);
            var body = await ReadBodyAsync(context);
            body.Should().Contain("Venta no encontrada.");
            LogVerifier.VerifyLog(_loggerMock, LogLevel.Warning, "Recurso no encontrado", Times.Once());
        }

        [Fact]
        public async Task InvokeAsync_UnauthorizedAccessException_Returns403()
        {
            var context = CreateContext();
            var middleware = new ExceptionHandlingMiddleware(
                _ => throw new UnauthorizedAccessException("sin permisos"),
                _loggerMock.Object);

            await middleware.InvokeAsync(context);

            context.Response.StatusCode.Should().Be(StatusCodes.Status403Forbidden);
            var body = await ReadBodyAsync(context);
            using (var doc = JsonDocument.Parse(body))
            {
                doc.RootElement.GetProperty("error").GetString()
                    .Should().Be("No tiene permisos para realizar esta acción.");
            }
            LogVerifier.VerifyLog(_loggerMock, LogLevel.Warning, "Acceso no autorizado", Times.Once());
        }

        [Fact]
        public async Task InvokeAsync_ArgumentException_Returns400WithMessage()
        {
            var context = CreateContext();
            var middleware = new ExceptionHandlingMiddleware(
                _ => throw new ArgumentException("El ID no coincide."),
                _loggerMock.Object);

            await middleware.InvokeAsync(context);

            context.Response.StatusCode.Should().Be(StatusCodes.Status400BadRequest);
            var body = await ReadBodyAsync(context);
            body.Should().Contain("El ID no coincide.");
            LogVerifier.VerifyLog(_loggerMock, LogLevel.Warning, "Argumento inválido", Times.Once());
        }

        [Fact]
        public async Task InvokeAsync_GenericException_Returns500()
        {
            var context = CreateContext();
            var middleware = new ExceptionHandlingMiddleware(
                _ => throw new InvalidOperationException("boom"),
                _loggerMock.Object);

            await middleware.InvokeAsync(context);

            context.Response.StatusCode.Should().Be(StatusCodes.Status500InternalServerError);
            var body = await ReadBodyAsync(context);
            using (var doc = JsonDocument.Parse(body))
            {
                doc.RootElement.GetProperty("error").GetString()
                    .Should().Be("Ocurrió un error inesperado.");
            }
            LogVerifier.VerifyLog(_loggerMock, LogLevel.Error, "Error interno del servidor", Times.Once());
        }

        [Fact]
        public async Task InvokeAsync_ErrorResponse_IsJsonWithCamelCaseType()
        {
            var context = CreateContext();
            var middleware = new ExceptionHandlingMiddleware(
                _ => throw new KeyNotFoundException("no existe"),
                _loggerMock.Object);

            await middleware.InvokeAsync(context);

            context.Response.ContentType.Should().Be("application/json");
            var body = await ReadBodyAsync(context);
            using var doc = JsonDocument.Parse(body);
            var root = doc.RootElement;
            root.GetProperty("statusCode").GetInt32().Should().Be(StatusCodes.Status404NotFound);
            root.GetProperty("error").GetString().Should().Be("no existe");
            root.GetProperty("type").GetString().Should().Be("https://httpstatuses.io/404");
        }
    }
}
