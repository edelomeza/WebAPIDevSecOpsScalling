using System.Diagnostics;
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

namespace IntegrationTest.Saga;

public class PedidoSagaTests : IClassFixture<WebApplicationFactory<Program>>
{
    private static readonly string[] EstadosTerminales =
    {
        "Facturado", "CompensadoPago", "CompensadoFactura", "StockRechazado",
    };

    private readonly WebApplicationFactory<Program> _factory;
    private readonly HttpClient _client;

    public PedidoSagaTests(WebApplicationFactory<Program> factory)
    {
        _factory = factory.WithWebHostBuilder(builder =>
        {
            builder.UseSetting("ConnectionStrings:DefaultConnection", "Server=.;Database=Test;Trusted_Connection=True;");
            builder.UseSetting("Jwt:Key", JwtTestConfig.Key);
            builder.UseSetting("Jwt:Issuer", JwtTestConfig.Issuer);
            builder.UseSetting("Jwt:Audience", JwtTestConfig.Audience);
            builder.UseSetting("UseInMemoryDatabase", "true");
            builder.UseSetting("InMemoryDatabaseName", $"SagaTestDb_{Guid.NewGuid():N}");
        });
        _client = _factory.CreateClient();
    }

    private string AdminToken => JwtTestConfig.AdminToken;

    private async Task<(int clienteId, int productoId)> SeedDependenciesAsync(int existencia = 100)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var cliente = new CliCliente
        {
            strNombreCliente = $"intcliente{Guid.NewGuid():N}"[..30],
            strCorreoElectronico = $"intcli{Guid.NewGuid():N}@test.com",
            strNumeroTelefono = "5512345678",
            RowVersion = new byte[] { 1, 0, 0, 0 },
        };
        db.CliCliente.Add(cliente);

        var producto = new ProProducto
        {
            strNombreProducto = $"intproducto{Guid.NewGuid():N}"[..30],
            intNumeroExistencia = existencia,
            decPrecio = 99.99m,
            RowVersion = new byte[] { 1, 0, 0, 0 },
        };
        db.ProProducto.Add(producto);

        await db.SaveChangesAsync();
        return (cliente.id, producto.id);
    }

    private async Task<Guid> CreatePedidoAsync(int clienteId, int productoId, int cantidad = 2)
    {
        var dto = new
        {
            idCliCliente = clienteId,
            Detalles = new[]
            {
                new { idProProducto = productoId, intCantidad = cantidad },
            },
        };

        var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/Ventas/pedido")
        {
            Content = JsonContent.Create(dto),
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", AdminToken);

        var response = await _client.SendAsync(request);
        response.StatusCode.Should().Be(HttpStatusCode.Created);

        var created = await response.Content.ReadFromJsonAsync<PedidoResponseDto>();
        created.Should().NotBeNull();
        return created!.id;
    }

    private async Task<PedidoResponseDto> GetPedidoAsync(Guid id)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, $"/api/v1/Ventas/pedido/{id}");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", AdminToken);
        var response = await _client.SendAsync(request);
        response.EnsureSuccessStatusCode();
        var pedido = await response.Content.ReadFromJsonAsync<PedidoResponseDto>();
        pedido.Should().NotBeNull();
        return pedido!;
    }

    private async Task<string> WaitForTerminalStateAsync(Guid pedidoId, int timeoutSeconds = 20)
    {
        var stopwatch = Stopwatch.StartNew();

        while (stopwatch.Elapsed < TimeSpan.FromSeconds(timeoutSeconds))
        {
            var pedido = await GetPedidoAsync(pedidoId);
            if (EstadosTerminales.Contains(pedido.strEstadoSaga))
                return pedido.strEstadoSaga;

            await Task.Delay(200);
        }

        throw new TimeoutException($"El saga no llegó a un estado terminal en {timeoutSeconds} segundos.");
    }

    [Fact]
    public async Task PedidoConStock_SagaCompleta_LlegaAEstadoTerminal()
    {
        var (clienteId, productoId) = await SeedDependenciesAsync();

        var pedidoId = await CreatePedidoAsync(clienteId, productoId);

        var estadoFinal = await WaitForTerminalStateAsync(pedidoId);

        estadoFinal.Should().BeOneOf("Facturado", "CompensadoPago", "CompensadoFactura");

        var request = new HttpRequestMessage(HttpMethod.Get, $"/api/v1/Ventas/saga/{pedidoId}/diagrama");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", AdminToken);
        var timelineResponse = await _client.SendAsync(request);
        timelineResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var timeline = await timelineResponse.Content.ReadFromJsonAsync<SagaTimelineDto>();
        timeline.Should().NotBeNull();
        timeline!.lstEventos.Should().NotBeEmpty();
        timeline.lstEventos.Should().Contain(e => e.strEtapa == "PedidoCreado");
    }

    [Fact]
    public async Task PedidoSinStock_EstadoFinal_StockRechazado()
    {
        var (clienteId, productoId) = await SeedDependenciesAsync(existencia: 1);

        var pedidoId = await CreatePedidoAsync(clienteId, productoId, cantidad: 5);

        var estadoFinal = await WaitForTerminalStateAsync(pedidoId);

        estadoFinal.Should().Be("StockRechazado");

        var pedido = await GetPedidoAsync(pedidoId);
        pedido.strMotivoRechazo.Should().Contain(productoId.ToString());
    }
}
