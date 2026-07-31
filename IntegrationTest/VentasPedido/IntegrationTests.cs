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

namespace IntegrationTest.VentasPedido;

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
            builder.UseSetting("InMemoryDatabaseName", $"VentasPedidoTestDb_{Guid.NewGuid():N}");
        });
        _client = _factory.CreateClient();
    }

    public async Task InitializeAsync()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        db.Set<VenPedido>().RemoveRange(db.Set<VenPedido>());
        await db.SaveChangesAsync();
    }

    public Task DisposeAsync() => Task.CompletedTask;

    private string AdminToken => JwtTestConfig.AdminToken;

    private async Task<(int clienteId, int productoId)> SeedDependenciesAsync()
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
            intNumeroExistencia = 100,
            decPrecio = 99.99m,
            RowVersion = new byte[] { 1, 0, 0, 0 },
        };
        db.ProProducto.Add(producto);

        await db.SaveChangesAsync();
        return (cliente.id, producto.id);
    }

    private async Task<PedidoResponseDto> CreatePedidoAsync(int? clienteId = null, int? productoId = null)
    {
        if (clienteId == null || productoId == null)
        {
            var (cid, pid) = await SeedDependenciesAsync();
            clienteId = cid;
            productoId = pid;
        }

        var dto = new
        {
            idCliCliente = clienteId.Value,
            Detalles = new[]
            {
                new { idProProducto = productoId.Value, intCantidad = 2 },
            },
        };

        var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/Ventas/pedido")
        {
            Content = JsonContent.Create(dto),
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", AdminToken);

        var response = await _client.SendAsync(request);
        response.StatusCode.Should().Be(HttpStatusCode.Created);

        var createdPedido = await response.Content.ReadFromJsonAsync<PedidoResponseDto>();
        createdPedido.Should().NotBeNull();

        var getRequest = new HttpRequestMessage(HttpMethod.Get, $"/api/v1/Ventas/pedido/{createdPedido!.id}");
        getRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", AdminToken);
        var getResponse = await _client.SendAsync(getRequest);
        getResponse.EnsureSuccessStatusCode();
        var fullPedido = await getResponse.Content.ReadFromJsonAsync<PedidoResponseDto>();
        fullPedido.Should().NotBeNull();

        return fullPedido!;
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

    [Fact]
    public async Task GetAll_EmptyDatabase_ReturnsEmptyPagedResult()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "/api/v1/Ventas/pedido");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", AdminToken);

        var response = await _client.SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var result = await response.Content.ReadFromJsonAsync<PagedResult<PedidoResponseDto>>();
        result.Should().NotBeNull();
        result!.Items.Should().BeEmpty();
        result.TotalCount.Should().Be(0);
        result.PageNumber.Should().Be(1);
        result.PageSize.Should().Be(20);
        result.TotalPages.Should().Be(0);
    }

    [Fact]
    public async Task GetAll_WithPedidos_ReturnsDefaultPagination()
    {
        await CreatePedidoAsync();
        await CreatePedidoAsync();

        var request = new HttpRequestMessage(HttpMethod.Get, "/api/v1/Ventas/pedido");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", AdminToken);

        var response = await _client.SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var result = await response.Content.ReadFromJsonAsync<PagedResult<PedidoResponseDto>>();
        result!.Items.Should().HaveCount(2);
        result.TotalCount.Should().Be(2);
        result.PageNumber.Should().Be(1);
        result.PageSize.Should().Be(20);
        result.TotalPages.Should().Be(1);
    }

    [Fact]
    public async Task GetAll_WithCustomPageSize_ReturnsCorrectSize()
    {
        var (clienteId, productoId) = await SeedDependenciesAsync();
        for (int i = 0; i < 5; i++)
            await CreatePedidoAsync(clienteId, productoId);

        var request = new HttpRequestMessage(HttpMethod.Get, "/api/v1/Ventas/pedido?pageSize=2");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", AdminToken);

        var response = await _client.SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var result = await response.Content.ReadFromJsonAsync<PagedResult<PedidoResponseDto>>();
        result!.Items.Should().HaveCount(2);
        result.TotalCount.Should().Be(5);
        result.PageNumber.Should().Be(1);
        result.PageSize.Should().Be(2);
        result.TotalPages.Should().Be(3);
    }

    [Fact]
    public async Task GetById_ExistingPedido_ReturnsPedido()
    {
        var pedido = await CreatePedidoAsync();

        var result = await GetPedidoAsync(pedido.id);

        result.id.Should().Be(pedido.id);
        result.Detalles.Should().HaveCount(1);
        var estadosSaga = new[] { "Pendiente", "StockValidado", "Pagado", "Facturado", "StockRechazado", "PagoRechazado", "CompensadoPago", "CompensadoFactura" };
        result.strEstadoSaga.Should().BeOneOf(estadosSaga);
    }

    [Fact]
    public async Task GetById_NonExistentId_ReturnsNotFound()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, $"/api/v1/Ventas/pedido/{Guid.NewGuid()}");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", AdminToken);

        var response = await _client.SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task GetById_MultiplePedidos_ReturnsCorrectPedido()
    {
        var p1 = await CreatePedidoAsync();
        var p2 = await CreatePedidoAsync();
        var p3 = await CreatePedidoAsync();

        var result = await GetPedidoAsync(p2.id);

        result.id.Should().Be(p2.id);
    }

    [Fact]
    public async Task Create_ValidDto_Returns201WithLocationHeader()
    {
        var (clienteId, productoId) = await SeedDependenciesAsync();

        var dto = new
        {
            idCliCliente = clienteId,
            Detalles = new[]
            {
                new { idProProducto = productoId, intCantidad = 2 },
            },
        };

        var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/Ventas/pedido")
        {
            Content = JsonContent.Create(dto),
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", AdminToken);

        var response = await _client.SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.Created);
        response.Headers.Location.Should().NotBeNull();
        response.Headers.Location!.ToString().Should().Contain("/api/v1/Ventas/pedido/");
    }

    [Fact]
    public async Task Create_ValidDto_ReturnsDtoWithId()
    {
        var (clienteId, productoId) = await SeedDependenciesAsync();

        var dto = new
        {
            idCliCliente = clienteId,
            Detalles = new[]
            {
                new { idProProducto = productoId, intCantidad = 2 },
            },
        };

        var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/Ventas/pedido")
        {
            Content = JsonContent.Create(dto),
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", AdminToken);

        var response = await _client.SendAsync(request);

        var createdPedido = await response.Content.ReadFromJsonAsync<PedidoResponseDto>();
        createdPedido.Should().NotBeNull();
        createdPedido!.id.Should().NotBeEmpty();
    }

    [Fact]
    public async Task Create_ValidDto_ReturnsCorrectData()
    {
        var (clienteId, productoId) = await SeedDependenciesAsync();

        var dto = new
        {
            idCliCliente = clienteId,
            Detalles = new[]
            {
                new { idProProducto = productoId, intCantidad = 2 },
            },
        };

        var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/Ventas/pedido")
        {
            Content = JsonContent.Create(dto),
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", AdminToken);

        var response = await _client.SendAsync(request);

        var createdPedido = await response.Content.ReadFromJsonAsync<PedidoResponseDto>();
        createdPedido!.idCliCliente.Should().Be(clienteId);
        createdPedido.strEstadoSaga.Should().Be("Pendiente");
        createdPedido.decTotal.Should().Be(199.98m);
        createdPedido.Detalles.Should().HaveCount(1);
    }

    [Fact]
    public async Task Create_ValidDto_CreatesPedidoInDatabase()
    {
        var (clienteId, productoId) = await SeedDependenciesAsync();

        var dto = new
        {
            idCliCliente = clienteId,
            Detalles = new[]
            {
                new { idProProducto = productoId, intCantidad = 2 },
            },
        };

        var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/Ventas/pedido")
        {
            Content = JsonContent.Create(dto),
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", AdminToken);

        var response = await _client.SendAsync(request);

        var createdPedido = await response.Content.ReadFromJsonAsync<PedidoResponseDto>();

        var getRequest = new HttpRequestMessage(HttpMethod.Get, $"/api/v1/Ventas/pedido/{createdPedido!.id}");
        getRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", AdminToken);
        var getResponse = await _client.SendAsync(getRequest);

        getResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var fetchedPedido = await getResponse.Content.ReadFromJsonAsync<PedidoResponseDto>();
        fetchedPedido!.id.Should().Be(createdPedido.id);
    }

    [Fact]
    public async Task Create_NonExistentCliente_Returns400()
    {
        var (_, productoId) = await SeedDependenciesAsync();

        var dto = new
        {
            idCliCliente = 9999,
            Detalles = new[]
            {
                new { idProProducto = productoId, intCantidad = 1 },
            },
        };

        var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/Ventas/pedido")
        {
            Content = JsonContent.Create(dto),
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", AdminToken);

        var response = await _client.SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Create_NonExistentProducto_Returns400()
    {
        var (clienteId, _) = await SeedDependenciesAsync();

        var dto = new
        {
            idCliCliente = clienteId,
            Detalles = new[]
            {
                new { idProProducto = 9999, intCantidad = 1 },
            },
        };

        var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/Ventas/pedido")
        {
            Content = JsonContent.Create(dto),
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", AdminToken);

        var response = await _client.SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Create_WithoutAuth_Returns401()
    {
        var (clienteId, productoId) = await SeedDependenciesAsync();

        var dto = new
        {
            idCliCliente = clienteId,
            Detalles = new[]
            {
                new { idProProducto = productoId, intCantidad = 1 },
            },
        };

        var response = await _client.PostAsJsonAsync("/api/v1/Ventas/pedido", dto);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task GetAll_WithoutAuth_Returns401()
    {
        var response = await _client.GetAsync("/api/v1/Ventas/pedido");

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task GetById_WithoutAuth_Returns401()
    {
        var response = await _client.GetAsync($"/api/v1/Ventas/pedido/{Guid.NewGuid()}");

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task FullLifecycle_CreateGet_CompleteFlow()
    {
        var (clienteId, productoId) = await SeedDependenciesAsync();

        var createDto = new
        {
            idCliCliente = clienteId,
            Detalles = new[]
            {
                new { idProProducto = productoId, intCantidad = 2 },
            },
        };

        var createRequest = new HttpRequestMessage(HttpMethod.Post, "/api/v1/Ventas/pedido")
        {
            Content = JsonContent.Create(createDto),
        };
        createRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", AdminToken);
        var createResponse = await _client.SendAsync(createRequest);
        createResponse.StatusCode.Should().Be(HttpStatusCode.Created);

        var created = await createResponse.Content.ReadFromJsonAsync<PedidoResponseDto>();
        created.Should().NotBeNull();

        var getRequest = new HttpRequestMessage(HttpMethod.Get, $"/api/v1/Ventas/pedido/{created!.id}");
        getRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", AdminToken);
        var getResponse = await _client.SendAsync(getRequest);
        getResponse.StatusCode.Should().Be(HttpStatusCode.OK);

        var fetched = await getResponse.Content.ReadFromJsonAsync<PedidoResponseDto>();
        fetched!.id.Should().Be(created.id);
        var estadosSaga = new[] { "Pendiente", "StockValidado", "Pagado", "Facturado", "StockRechazado", "PagoRechazado", "CompensadoPago", "CompensadoFactura" };
        fetched.strEstadoSaga.Should().BeOneOf(estadosSaga);
        fetched.Detalles.Should().HaveCount(1);
    }
}
