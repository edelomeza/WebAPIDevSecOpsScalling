using System.Diagnostics;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using UnitTest.Common;
using WebAPIDevSecOps.Context;
using WebAPIDevSecOps.Dto;
using WebAPIDevSecOps.Models;

namespace IntegrationTest.Saga;

/// <summary>
/// PostDespliegue9: puente legacy -&gt; saga sin modificar frontend.
/// Flujo UI en 2 etapas: POST /venta -&gt; POST /ventadetalle -&gt; PUT /venta/{id} estado 2 (finalize).
/// Flag ON explícito (independiente del default de Development).
/// </summary>
public class SagaBridgeIntegrationTests : IClassFixture<WebApplicationFactory<Program>>
{
    private static readonly string[] EstadosTerminales =
    {
        "Facturado", "CompensadoPago", "CompensadoFactura", "StockRechazado",
    };

    private readonly WebApplicationFactory<Program> _factory;
    private readonly HttpClient _client;

    public SagaBridgeIntegrationTests(WebApplicationFactory<Program> factory)
    {
        _factory = factory.WithWebHostBuilder(builder =>
        {
            builder.UseSetting("ConnectionStrings:DefaultConnection", "Server=.;Database=Test;Trusted_Connection=True;");
            builder.UseSetting("Jwt:Key", JwtTestConfig.Key);
            builder.UseSetting("Jwt:Issuer", JwtTestConfig.Issuer);
            builder.UseSetting("Jwt:Audience", JwtTestConfig.Audience);
            builder.UseSetting("UseInMemoryDatabase", "true");
            builder.UseSetting("InMemoryDatabaseName", $"SagaBridgeTestDb_{Guid.NewGuid():N}");
            builder.UseSetting("Feature:SagaBridge", "true");
        });
        _client = _factory.CreateClient();
    }

    private string AdminToken => JwtTestConfig.AdminToken;

    private void Auth(HttpRequestMessage request) =>
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", AdminToken);

    private async Task<(int clienteId, int usuarioId, int productoId)> SeedAsync(int existencia = 100, decimal precio = 50m)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var cliente = new CliCliente
        {
            strNombreCliente = $"bridgecli{Guid.NewGuid():N}"[..30],
            strCorreoElectronico = $"bridgecli{Guid.NewGuid():N}@test.com",
            strNumeroTelefono = "5512345678",
            RowVersion = new byte[] { 1, 0, 0, 0 },
        };
        db.CliCliente.Add(cliente);

        var usuario = new SegUsuario
        {
            strNombre = $"bridgeuser{Guid.NewGuid():N}"[..30],
            strPWD = "hash",
            strCorreoElectronico = $"bridgeuser{Guid.NewGuid():N}@test.com",
        };
        db.SegUsuario.Add(usuario);

        if (!await db.VenCatEstado.AnyAsync())
        {
            db.VenCatEstado.AddRange(
                new VenCatEstado { id = 1, strValor = "En compra usuario", strDescripcion = "Se realiza la compra por el usuario" },
                new VenCatEstado { id = 2, strValor = "Fin compra usuario", strDescripcion = "Se concluye compra por usuario" });
        }

        var producto = new ProProducto
        {
            strNombreProducto = $"bridgeprod{Guid.NewGuid():N}"[..30],
            intNumeroExistencia = existencia,
            decPrecio = precio,
            RowVersion = new byte[] { 1, 0, 0, 0 },
        };
        db.ProProducto.Add(producto);

        await db.SaveChangesAsync();
        return (cliente.id, usuario.id, producto.id);
    }

    private async Task<int> PostVentaAsync(int clienteId, int usuarioId)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/venta")
        {
            Content = JsonContent.Create(new { idCliCliente = clienteId, idSegUsuario = usuarioId }),
        };
        Auth(request);
        var response = await _client.SendAsync(request);
        response.StatusCode.Should().Be(HttpStatusCode.Created);
        var created = await response.Content.ReadFromJsonAsync<VenVentaDto>();
        created.Should().NotBeNull();
        return created!.id;
    }

    private async Task PostDetalleAsync(int ventaId, int productoId, int piezas = 2)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/ventadetalle")
        {
            Content = JsonContent.Create(new { idVenVenta = ventaId, idProProducto = productoId, intPiezaVenta = piezas }),
        };
        Auth(request);
        var response = await _client.SendAsync(request);
        response.StatusCode.Should().Be(HttpStatusCode.Created);
    }

    private async Task<HttpStatusCode> PutVentaAsync(int ventaId, int clienteId, int usuarioId, int estado)
    {
        var request = new HttpRequestMessage(HttpMethod.Put, $"/api/v1/venta/{ventaId}")
        {
            Content = JsonContent.Create(new { id = ventaId, idCliCliente = clienteId, idSegUsuario = usuarioId, idVenCatEstado = estado }),
        };
        Auth(request);
        var response = await _client.SendAsync(request);
        return response.StatusCode;
    }

    private async Task<Guid> GetPedidoIdAsync(int ventaId)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var pedido = await db.VenPedido.AsNoTracking().FirstOrDefaultAsync(p => p.LegacyVentaId == ventaId);
        pedido.Should().NotBeNull("el dual-write debe crear el pedido espejo");
        return pedido!.id;
    }

    private async Task<PedidoResponseDto> GetPedidoAsync(Guid id)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, $"/api/v1/Ventas/pedido/{id}");
        Auth(request);
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

        throw new TimeoutException("El saga del puente no llegó a estado terminal.");
    }

    private async Task<int> GetStockAsync(int productoId)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        return (await db.ProProducto.AsNoTracking().FirstAsync(p => p.id == productoId)).intNumeroExistencia;
    }

    [Fact]
    public async Task LegacyFlow_PUT2_DisparaSaga_LlegaAEstadoTerminal()
    {
        var (clienteId, usuarioId, productoId) = await SeedAsync(existencia: 100, precio: 50m);

        var ventaId = await PostVentaAsync(clienteId, usuarioId);
        await PostDetalleAsync(ventaId, productoId, piezas: 2);

        // Puente activo: el detalle NO descuenta stock (owner = saga).
        (await GetStockAsync(productoId)).Should().Be(100);

        (await PutVentaAsync(ventaId, clienteId, usuarioId, estado: 2)).Should().Be(HttpStatusCode.NoContent);

        var pedidoId = await GetPedidoIdAsync(ventaId);
        var estadoFinal = await WaitForTerminalStateAsync(pedidoId);
        estadoFinal.Should().BeOneOf("Facturado", "CompensadoPago", "CompensadoFactura");

        // Facturado = descuento neto 1x; Compensado = la compensación restauró el stock (correcto).
        var stockEsperado = estadoFinal == "Facturado" ? 98 : 100;
        (await GetStockAsync(productoId)).Should().Be(stockEsperado);

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var pedido = await db.VenPedido.AsNoTracking().Include(p => p.Detalles).FirstAsync(p => p.id == pedidoId);
        pedido.decTotal.Should().Be(100m);
        pedido.Detalles.Should().HaveCount(1);
        (await db.VenVenta.AsNoTracking().FirstAsync(v => v.id == ventaId)).idVenCatEstado.Should().Be(2);
        (await db.VenVentaDetalle.AsNoTracking().CountAsync(d => d.idVenVenta == ventaId)).Should().Be(1);
    }

    [Fact]
    public async Task LegacyFlow_DoblePUT2_NoDuplicaSaga()
    {
        var (clienteId, usuarioId, productoId) = await SeedAsync(existencia: 100, precio: 50m);

        var ventaId = await PostVentaAsync(clienteId, usuarioId);
        await PostDetalleAsync(ventaId, productoId, piezas: 1);

        (await PutVentaAsync(ventaId, clienteId, usuarioId, estado: 2)).Should().Be(HttpStatusCode.NoContent);
        var pedidoId = await GetPedidoIdAsync(ventaId);
        await WaitForTerminalStateAsync(pedidoId);

        var stockTrasPrimero = await GetStockAsync(productoId);

        // Re-PUT 2->2: idempotente, no republica, no redecrementa.
        (await PutVentaAsync(ventaId, clienteId, usuarioId, estado: 2)).Should().Be(HttpStatusCode.NoContent);
        await Task.Delay(1000);

        (await GetStockAsync(productoId)).Should().Be(stockTrasPrimero);
    }

    [Fact]
    public async Task LegacyFlow_PUT2_SinDetalles_Retorna400()
    {
        var (clienteId, usuarioId, _) = await SeedAsync();

        var ventaId = await PostVentaAsync(clienteId, usuarioId);

        (await PutVentaAsync(ventaId, clienteId, usuarioId, estado: 2)).Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Admin_PendienteSinFinalizar_RepublishDisparaSaga()
    {
        var (clienteId, usuarioId, productoId) = await SeedAsync(existencia: 100, precio: 50m);

        var ventaId = await PostVentaAsync(clienteId, usuarioId);
        await PostDetalleAsync(ventaId, productoId, piezas: 2);
        var pedidoId = await GetPedidoIdAsync(ventaId);

        // Sin PUT 2 el pedido sigue Pendiente y aparece en la lista de recuperación.
        var pendientesRequest = new HttpRequestMessage(HttpMethod.Get, "/api/v1/Ventas/admin/pedidos/pendientes");
        Auth(pendientesRequest);
        var pendientesResponse = await _client.SendAsync(pendientesRequest);
        pendientesResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var pendientes = await pendientesResponse.Content.ReadFromJsonAsync<List<PedidoResponseDto>>();
        pendientes.Should().Contain(p => p.id == pedidoId);

        var republishRequest = new HttpRequestMessage(HttpMethod.Post, $"/api/v1/Ventas/admin/pedidos/{pedidoId}/republish");
        Auth(republishRequest);
        var republishResponse = await _client.SendAsync(republishRequest);
        republishResponse.StatusCode.Should().Be(HttpStatusCode.OK);

        var estadoFinal = await WaitForTerminalStateAsync(pedidoId);
        estadoFinal.Should().BeOneOf("Facturado", "CompensadoPago", "CompensadoFactura");
        (await GetStockAsync(productoId)).Should().Be(98);
    }
}
