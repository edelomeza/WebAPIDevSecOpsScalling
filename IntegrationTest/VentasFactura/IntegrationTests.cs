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

namespace IntegrationTest.VentasFactura;

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
            builder.UseSetting("InMemoryDatabaseName", $"VentasFacturaTestDb_{Guid.NewGuid():N}");
        });
        _client = _factory.CreateClient();
    }

    public async Task InitializeAsync()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        db.Set<VenPedidoFactura>().RemoveRange(db.Set<VenPedidoFactura>());
        await db.SaveChangesAsync();
    }

    public Task DisposeAsync() => Task.CompletedTask;

    private string AdminToken => JwtTestConfig.AdminToken;

    private async Task<VenPedidoFactura> SeedFacturaAsync(Guid? pedidoId = null)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var factura = new VenPedidoFactura
        {
            idVenPedido = pedidoId ?? Guid.NewGuid(),
            strFolioFactura = $"F-2026-{Guid.NewGuid():N}"[..14],
            strRFC = "XAXX010101000",
            decTotal = 199.98m,
            dteFechaEmision = DateTime.UtcNow,
            strEstado = "Emitida",
        };
        db.Set<VenPedidoFactura>().Add(factura);
        await db.SaveChangesAsync();
        return factura;
    }

    [Fact]
    public async Task GetById_ExistingFactura_ReturnsFactura()
    {
        var factura = await SeedFacturaAsync();

        var request = new HttpRequestMessage(HttpMethod.Get, $"/api/v1/Ventas/factura/{factura.id}");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", AdminToken);
        var response = await _client.SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var dto = await response.Content.ReadFromJsonAsync<FacturaResponseDto>();
        dto.Should().NotBeNull();
        dto!.id.Should().Be(factura.id);
        dto.decTotal.Should().Be(factura.decTotal);
        dto.strEstado.Should().Be(factura.strEstado);
    }

    [Fact]
    public async Task GetById_NonExistentId_ReturnsNotFound()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "/api/v1/Ventas/factura/9999");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", AdminToken);
        var response = await _client.SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task GetById_NegativeId_ReturnsNotFound()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "/api/v1/Ventas/factura/-1");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", AdminToken);
        var response = await _client.SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task GetById_ZeroId_ReturnsNotFound()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "/api/v1/Ventas/factura/0");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", AdminToken);
        var response = await _client.SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task GetById_MultipleFacturas_ReturnsCorrectFactura()
    {
        var f1 = await SeedFacturaAsync();
        var f2 = await SeedFacturaAsync();
        var f3 = await SeedFacturaAsync();

        var request = new HttpRequestMessage(HttpMethod.Get, $"/api/v1/Ventas/factura/{f2.id}");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", AdminToken);
        var response = await _client.SendAsync(request);

        var dto = await response.Content.ReadFromJsonAsync<FacturaResponseDto>();
        dto!.id.Should().Be(f2.id);
    }

    [Fact]
    public async Task GetByPedidoId_ExistingPedido_ReturnsFacturas()
    {
        var pedidoId = Guid.NewGuid();
        await SeedFacturaAsync(pedidoId);
        await SeedFacturaAsync(pedidoId);

        var request = new HttpRequestMessage(HttpMethod.Get, $"/api/v1/Ventas/factura?pedidoId={pedidoId}");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", AdminToken);
        var response = await _client.SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var facturas = await response.Content.ReadFromJsonAsync<List<FacturaResponseDto>>();
        facturas.Should().HaveCount(2);
        facturas.Should().OnlyContain(f => f.idVenPedido == pedidoId);
    }

    [Fact]
    public async Task GetByPedidoId_NoFacturas_ReturnsEmptyList()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, $"/api/v1/Ventas/factura?pedidoId={Guid.NewGuid()}");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", AdminToken);
        var response = await _client.SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var facturas = await response.Content.ReadFromJsonAsync<List<FacturaResponseDto>>();
        facturas.Should().BeEmpty();
    }

    [Fact]
    public async Task GetByPedidoId_FiltersByPedido()
    {
        var pedidoA = Guid.NewGuid();
        var pedidoB = Guid.NewGuid();
        await SeedFacturaAsync(pedidoA);
        await SeedFacturaAsync(pedidoA);
        await SeedFacturaAsync(pedidoB);

        var request = new HttpRequestMessage(HttpMethod.Get, $"/api/v1/Ventas/factura?pedidoId={pedidoA}");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", AdminToken);
        var response = await _client.SendAsync(request);

        var facturas = await response.Content.ReadFromJsonAsync<List<FacturaResponseDto>>();
        facturas.Should().HaveCount(2);
        facturas.Should().OnlyContain(f => f.idVenPedido == pedidoA);
    }

    [Fact]
    public async Task GetByPedidoId_EmptyGuid_ReturnsBadRequest()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "/api/v1/Ventas/factura?pedidoId=00000000-0000-0000-0000-000000000000");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", AdminToken);
        var response = await _client.SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task GetById_WithoutAuth_Returns401()
    {
        var response = await _client.GetAsync($"/api/v1/Ventas/factura/1");
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task GetByPedidoId_WithoutAuth_Returns401()
    {
        var response = await _client.GetAsync($"/api/v1/Ventas/factura?pedidoId={Guid.NewGuid()}");
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }
}
