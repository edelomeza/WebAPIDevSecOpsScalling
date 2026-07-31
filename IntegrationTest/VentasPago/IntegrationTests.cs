using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using UnitTest.Common;
using WebAPIDevSecOps.Context;
using WebAPIDevSecOps.Dto;
using WebAPIDevSecOps.Models;

namespace IntegrationTest.VentasPago;

public class IntegrationTests : IClassFixture<WebApplicationFactory<Program>>, IAsyncLifetime
{
    private readonly WebApplicationFactory<Program> _factory;
    private readonly HttpClient _client;

    public IntegrationTests(WebApplicationFactory<Program> factory)
    {
        _factory = factory.WithWebHostBuilder(builder =>
        {
            builder.UseSetting("ConnectionStrings:DefaultConnection", "Server=.;Database=Test;Trusted_Connection=True;");
            builder.UseSetting("Jwt:Key", JwtTestConfig.Key);
            builder.UseSetting("Jwt:Issuer", JwtTestConfig.Issuer);
            builder.UseSetting("Jwt:Audience", JwtTestConfig.Audience);
            builder.UseSetting("UseInMemoryDatabase", "true");
            builder.UseSetting("InMemoryDatabaseName", $"VentasPagoTestDb_{Guid.NewGuid():N}");
        });
        _client = _factory.CreateClient();
    }

    public async Task InitializeAsync()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        db.Set<VenPedidoPago>().RemoveRange(db.Set<VenPedidoPago>());
        await db.SaveChangesAsync();
    }

    public Task DisposeAsync() => Task.CompletedTask;

    private string AdminToken => JwtTestConfig.AdminToken;

    private async Task<VenPedidoPago> SeedPagoAsync(Guid? pedidoId = null)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var pago = new VenPedidoPago
        {
            idVenPedido = pedidoId ?? Guid.NewGuid(),
            decMonto = 199.98m,
            strMetodoPago = "Tarjeta",
            strIdTransaccion = $"TXN-{Guid.NewGuid():N}",
            strEstado = "Completado",
            dteFechaPago = DateTime.UtcNow,
        };
        db.Set<VenPedidoPago>().Add(pago);
        await db.SaveChangesAsync();
        return pago;
    }

    [Fact]
    public async Task GetById_ExistingPago_ReturnsPago()
    {
        var pago = await SeedPagoAsync();

        var request = new HttpRequestMessage(HttpMethod.Get, $"/api/v1/Ventas/pago/{pago.id}");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", AdminToken);
        var response = await _client.SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var dto = await response.Content.ReadFromJsonAsync<PagoResponseDto>();
        dto.Should().NotBeNull();
        dto!.id.Should().Be(pago.id);
        dto.decMonto.Should().Be(pago.decMonto);
        dto.strEstado.Should().Be(pago.strEstado);
    }

    [Fact]
    public async Task GetById_NonExistentId_ReturnsNotFound()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "/api/v1/Ventas/pago/9999");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", AdminToken);
        var response = await _client.SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task GetById_NegativeId_ReturnsNotFound()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "/api/v1/Ventas/pago/-1");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", AdminToken);
        var response = await _client.SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task GetById_ZeroId_ReturnsNotFound()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "/api/v1/Ventas/pago/0");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", AdminToken);
        var response = await _client.SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task GetById_MultiplePagos_ReturnsCorrectPago()
    {
        var p1 = await SeedPagoAsync();
        var p2 = await SeedPagoAsync();
        var p3 = await SeedPagoAsync();

        var request = new HttpRequestMessage(HttpMethod.Get, $"/api/v1/Ventas/pago/{p2.id}");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", AdminToken);
        var response = await _client.SendAsync(request);

        var dto = await response.Content.ReadFromJsonAsync<PagoResponseDto>();
        dto!.id.Should().Be(p2.id);
    }

    [Fact]
    public async Task GetByPedidoId_ExistingPedido_ReturnsPagos()
    {
        var pedidoId = Guid.NewGuid();
        await SeedPagoAsync(pedidoId);
        await SeedPagoAsync(pedidoId);

        var request = new HttpRequestMessage(HttpMethod.Get, $"/api/v1/Ventas/pago?pedidoId={pedidoId}");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", AdminToken);
        var response = await _client.SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var pagos = await response.Content.ReadFromJsonAsync<List<PagoResponseDto>>();
        pagos.Should().HaveCount(2);
        pagos.Should().OnlyContain(p => p.idVenPedido == pedidoId);
    }

    [Fact]
    public async Task GetByPedidoId_NoPagos_ReturnsEmptyList()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, $"/api/v1/Ventas/pago?pedidoId={Guid.NewGuid()}");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", AdminToken);
        var response = await _client.SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var pagos = await response.Content.ReadFromJsonAsync<List<PagoResponseDto>>();
        pagos.Should().BeEmpty();
    }

    [Fact]
    public async Task GetByPedidoId_FiltersByPedido()
    {
        var pedidoA = Guid.NewGuid();
        var pedidoB = Guid.NewGuid();
        await SeedPagoAsync(pedidoA);
        await SeedPagoAsync(pedidoA);
        await SeedPagoAsync(pedidoB);

        var request = new HttpRequestMessage(HttpMethod.Get, $"/api/v1/Ventas/pago?pedidoId={pedidoA}");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", AdminToken);
        var response = await _client.SendAsync(request);

        var pagos = await response.Content.ReadFromJsonAsync<List<PagoResponseDto>>();
        pagos.Should().HaveCount(2);
        pagos.Should().OnlyContain(p => p.idVenPedido == pedidoA);
    }

    [Fact]
    public async Task GetByPedidoId_EmptyGuid_ReturnsBadRequest()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "/api/v1/Ventas/pago?pedidoId=00000000-0000-0000-0000-000000000000");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", AdminToken);
        var response = await _client.SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task GetById_WithoutAuth_Returns401()
    {
        var response = await _client.GetAsync($"/api/v1/Ventas/pago/1");
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task GetByPedidoId_WithoutAuth_Returns401()
    {
        var response = await _client.GetAsync($"/api/v1/Ventas/pago?pedidoId={Guid.NewGuid()}");
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }
}
