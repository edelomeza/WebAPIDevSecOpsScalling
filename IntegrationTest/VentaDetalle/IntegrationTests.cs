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
using WebAPIDevSecOps.Services;

namespace IntegrationTest.VentaDetalle;

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
            builder.UseSetting("InMemoryDatabaseName", $"VentaDetalleTestDb_{Guid.NewGuid():N}");
        });
        _client = _factory.CreateClient();
    }

    public async Task InitializeAsync()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        db.Set<VenVentaDetalle>().RemoveRange(db.Set<VenVentaDetalle>());
        await db.SaveChangesAsync();
    }

    public Task DisposeAsync() => Task.CompletedTask;

    private string AdminToken => JwtTestConfig.AdminToken;

    private async Task<(int ventaId, int productoId, decimal precio)> SeedDependenciesAsync()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var cliente = new CliCliente
        {
            strNombreCliente = $"intcliente{Guid.NewGuid():N}"[..30],
            strCorreoElectronico = $"intcli{Guid.NewGuid():N}@test.com",
            strNumeroTelefono = "5512345678",
            RowVersion = new byte[] { 1, 0, 0, 0 }
        };
        db.CliCliente.Add(cliente);

        var usuario = new SegUsuario
        {
            strNombre = "admin",
            strPWD = "hash",
            strCorreoElectronico = $"intusr{Guid.NewGuid():N}@test.com",
            RowVersion = new byte[] { 1, 0, 0, 0 }
        };
        db.SegUsuario.Add(usuario);

        if (!db.VenCatEstado.Any())
        {
            db.VenCatEstado.AddRange(
                new VenCatEstado { strValor = "En compra", strDescripcion = "Compra en proceso" },
                new VenCatEstado { strValor = "Pagado", strDescripcion = "Compra pagada" },
                new VenCatEstado { strValor = "Cancelado", strDescripcion = "Compra cancelada" }
            );
        }

        var producto = new ProProducto
        {
            strNombreProducto = $"intproducto{Guid.NewGuid():N}"[..30],
            intNumeroExistencia = 1000,
            decPrecio = 149.99m,
            RowVersion = new byte[] { 1, 0, 0, 0 }
        };
        db.ProProducto.Add(producto);
        await db.SaveChangesAsync();

        var venta = new VenVenta
        {
            idCliCliente = cliente.id,
            idSegUsuario = usuario.id,
            idVenCatEstado = 1,
            dteFechaHoraCompra = DateTime.UtcNow,
            strClaveVenta = Guid.NewGuid().ToString("N")[..10],
            RowVersion = new byte[] { 1, 0, 0, 0 }
        };
        db.VenVenta.Add(venta);
        await db.SaveChangesAsync();

        return (venta.id, producto.id, producto.decPrecio);
    }

    private async Task<VenVentaDetalleDto> CreateDetalleAsync(int? ventaId = null, int? productoId = null, int piezas = 2)
    {
        if (ventaId == null || productoId == null)
        {
            var (vid, pid, _) = await SeedDependenciesAsync();
            ventaId = vid;
            productoId = pid;
        }

        var dto = new { idVenVenta = ventaId.Value, idProProducto = productoId.Value, intPiezaVenta = piezas };

        var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/ventadetalle")
        {
            Content = JsonContent.Create(dto)
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", AdminToken);

        var response = await _client.SendAsync(request);
        response.StatusCode.Should().Be(HttpStatusCode.Created);

        var created = await response.Content.ReadFromJsonAsync<VenVentaDetalleDto>();
        created.Should().NotBeNull();

        var getRequest = new HttpRequestMessage(HttpMethod.Get, $"/api/v1/ventadetalle/{created!.id}");
        getRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", AdminToken);
        var getResponse = await _client.SendAsync(getRequest);
        getResponse.EnsureSuccessStatusCode();
        var full = await getResponse.Content.ReadFromJsonAsync<VenVentaDetalleDto>();
        full.Should().NotBeNull();

        return full!;
    }

    [Fact]
    public async Task GetAll_EmptyDatabase_ReturnsEmptyPagedResult()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "/api/v1/ventadetalle");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", AdminToken);

        var response = await _client.SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var result = await response.Content.ReadFromJsonAsync<PagedResult<VenVentaDetalleDto>>();
        result.Should().NotBeNull();
        result!.Items.Should().BeEmpty();
        result.TotalCount.Should().Be(0);
        result.PageNumber.Should().Be(1);
        result.PageSize.Should().Be(20);
        result.TotalPages.Should().Be(0);
    }

    [Fact]
    public async Task GetAll_WithDetalles_ReturnsDefaultPagination()
    {
        await CreateDetalleAsync();
        await CreateDetalleAsync();

        var request = new HttpRequestMessage(HttpMethod.Get, "/api/v1/ventadetalle");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", AdminToken);

        var response = await _client.SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var result = await response.Content.ReadFromJsonAsync<PagedResult<VenVentaDetalleDto>>();
        result!.Items.Should().HaveCount(2);
        result.TotalCount.Should().Be(2);
        result.PageNumber.Should().Be(1);
        result.PageSize.Should().Be(20);
        result.TotalPages.Should().Be(1);
    }

    [Fact]
    public async Task GetAll_WithCustomPageSize_ReturnsCorrectSize()
    {
        var (ventaId, productoId, _) = await SeedDependenciesAsync();
        for (int i = 0; i < 15; i++)
            await CreateDetalleAsync(ventaId, productoId);

        var request = new HttpRequestMessage(HttpMethod.Get, "/api/v1/ventadetalle?pageSize=5");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", AdminToken);

        var response = await _client.SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var result = await response.Content.ReadFromJsonAsync<PagedResult<VenVentaDetalleDto>>();
        result!.Items.Should().HaveCount(5);
        result.TotalCount.Should().Be(15);
        result.PageNumber.Should().Be(1);
        result.PageSize.Should().Be(5);
        result.TotalPages.Should().Be(3);
    }

    [Fact]
    public async Task GetAll_WithCustomPageNumber_ReturnsCorrectPage()
    {
        var (ventaId, productoId, _) = await SeedDependenciesAsync();
        for (int i = 0; i < 25; i++)
            await CreateDetalleAsync(ventaId, productoId);

        var request = new HttpRequestMessage(HttpMethod.Get, "/api/v1/ventadetalle?pageNumber=2&pageSize=10");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", AdminToken);

        var response = await _client.SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var result = await response.Content.ReadFromJsonAsync<PagedResult<VenVentaDetalleDto>>();
        result!.Items.Should().HaveCount(10);
        result.TotalCount.Should().Be(25);
        result.PageNumber.Should().Be(2);
        result.PageSize.Should().Be(10);
        result.TotalPages.Should().Be(3);
    }

    [Fact]
    public async Task GetAll_OnLastPage_ReturnsRemainingItems()
    {
        var (ventaId, productoId, _) = await SeedDependenciesAsync();
        for (int i = 0; i < 25; i++)
            await CreateDetalleAsync(ventaId, productoId);

        var request = new HttpRequestMessage(HttpMethod.Get, "/api/v1/ventadetalle?pageNumber=3&pageSize=10");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", AdminToken);

        var response = await _client.SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var result = await response.Content.ReadFromJsonAsync<PagedResult<VenVentaDetalleDto>>();
        result!.Items.Should().HaveCount(5);
        result.TotalCount.Should().Be(25);
        result.PageNumber.Should().Be(3);
        result.PageSize.Should().Be(10);
        result.TotalPages.Should().Be(3);
    }

    [Fact]
    public async Task GetAll_HasCacheControlHeader()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "/api/v1/ventadetalle");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", AdminToken);

        var response = await _client.SendAsync(request);

        response.Headers.CacheControl.Should().NotBeNull();
        response.Headers.CacheControl!.NoStore.Should().BeTrue();
    }

    [Fact]
    public async Task GetById_ExistingDetalle_ReturnsDetalle()
    {
        var detalle = await CreateDetalleAsync();

        var request = new HttpRequestMessage(HttpMethod.Get, $"/api/v1/ventadetalle/{detalle.id}");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", AdminToken);

        var response = await _client.SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var result = await response.Content.ReadFromJsonAsync<VenVentaDetalleDto>();
        result!.id.Should().Be(detalle.id);
        result.intPiezaVenta.Should().Be(detalle.intPiezaVenta);
        result.decTotalVenta.Should().Be(detalle.decTotalVenta);
    }

    [Fact]
    public async Task GetById_NonExistentId_ReturnsNotFound()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "/api/v1/ventadetalle/9999");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", AdminToken);

        var response = await _client.SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task GetById_NegativeId_ReturnsNotFound()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "/api/v1/ventadetalle/-1");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", AdminToken);

        var response = await _client.SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task GetById_ZeroId_ReturnsNotFound()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "/api/v1/ventadetalle/0");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", AdminToken);

        var response = await _client.SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Create_ValidDto_Returns201WithLocationHeader()
    {
        var (ventaId, productoId, _) = await SeedDependenciesAsync();

        var dto = new { idVenVenta = ventaId, idProProducto = productoId, intPiezaVenta = 3 };

        var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/ventadetalle")
        {
            Content = JsonContent.Create(dto)
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", AdminToken);

        var response = await _client.SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.Created);
        response.Headers.Location.Should().NotBeNull();
        response.Headers.Location!.ToString().Should().Contain("/api/v1/VentaDetalle/");
    }

    [Fact]
    public async Task Create_ValidDto_ReturnsDtoWithId()
    {
        var (ventaId, productoId, _) = await SeedDependenciesAsync();

        var dto = new { idVenVenta = ventaId, idProProducto = productoId, intPiezaVenta = 3 };

        var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/ventadetalle")
        {
            Content = JsonContent.Create(dto)
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", AdminToken);

        var response = await _client.SendAsync(request);

        var created = await response.Content.ReadFromJsonAsync<VenVentaDetalleDto>();
        created.Should().NotBeNull();
        created!.id.Should().BeGreaterThan(0);
    }

    [Fact]
    public async Task Create_ValidDto_ReturnsCorrectData()
    {
        var (ventaId, productoId, precio) = await SeedDependenciesAsync();

        var dto = new { idVenVenta = ventaId, idProProducto = productoId, intPiezaVenta = 5 };

        var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/ventadetalle")
        {
            Content = JsonContent.Create(dto)
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", AdminToken);

        var response = await _client.SendAsync(request);

        var created = await response.Content.ReadFromJsonAsync<VenVentaDetalleDto>();
        created!.idVenVenta.Should().Be(ventaId);
        created.idProProducto.Should().Be(productoId);
        created.intPiezaVenta.Should().Be(5);
        created.decTotalVenta.Should().Be(5 * precio);
        created.strNombreProducto.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public async Task Create_ValidDto_CreatesDetalleInDatabase()
    {
        var (ventaId, productoId, _) = await SeedDependenciesAsync();

        var dto = new { idVenVenta = ventaId, idProProducto = productoId, intPiezaVenta = 3 };

        var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/ventadetalle")
        {
            Content = JsonContent.Create(dto)
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", AdminToken);

        var response = await _client.SendAsync(request);

        var created = await response.Content.ReadFromJsonAsync<VenVentaDetalleDto>();

        var getRequest = new HttpRequestMessage(HttpMethod.Get, $"/api/v1/ventadetalle/{created!.id}");
        getRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", AdminToken);
        var getResponse = await _client.SendAsync(getRequest);

        getResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var fetched = await getResponse.Content.ReadFromJsonAsync<VenVentaDetalleDto>();
        fetched!.id.Should().Be(created.id);
    }

    [Fact]
    public async Task Create_NonExistentVenta_Returns400()
    {
        var (_, productoId, _) = await SeedDependenciesAsync();

        var dto = new { idVenVenta = 9999, idProProducto = productoId, intPiezaVenta = 1 };

        var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/ventadetalle")
        {
            Content = JsonContent.Create(dto)
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", AdminToken);

        var response = await _client.SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Create_NonExistentProducto_Returns400()
    {
        var (ventaId, _, _) = await SeedDependenciesAsync();

        var dto = new { idVenVenta = ventaId, idProProducto = 9999, intPiezaVenta = 1 };

        var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/ventadetalle")
        {
            Content = JsonContent.Create(dto)
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", AdminToken);

        var response = await _client.SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Create_InsufficientStock_Returns400()
    {
        var (ventaId, productoId, _) = await SeedDependenciesAsync();

        var dto = new { idVenVenta = ventaId, idProProducto = productoId, intPiezaVenta = 9999 };

        var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/ventadetalle")
        {
            Content = JsonContent.Create(dto)
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", AdminToken);

        var response = await _client.SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Create_UpdatesStockInDatabase()
    {
        var (ventaId, productoId, _) = await SeedDependenciesAsync();

        var dto = new { idVenVenta = ventaId, idProProducto = productoId, intPiezaVenta = 4 };

        var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/ventadetalle")
        {
            Content = JsonContent.Create(dto)
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", AdminToken);

        var response = await _client.SendAsync(request);
        response.StatusCode.Should().Be(HttpStatusCode.Created);

        var searchRequest = new HttpRequestMessage(HttpMethod.Get, "/api/v1/ventadetalle/autocomplete?texto=intproducto");
        searchRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", AdminToken);
        var searchResponse = await _client.SendAsync(searchRequest);
        var productos = await searchResponse.Content.ReadFromJsonAsync<IEnumerable<ProProductoAutocompleteDto>>();

        productos.Should().NotBeNull();
        var producto = productos!.First();
        producto.strTextoAutocomplete.Should().Contain("#: 996");
    }

    [Fact]
    public async Task BuscarProducto_ReturnsMatchingProductos()
    {
        var (_, productoId, _) = await SeedDependenciesAsync();

        var request = new HttpRequestMessage(HttpMethod.Get, "/api/v1/ventadetalle/autocomplete?texto=intproducto");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", AdminToken);

        var response = await _client.SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var result = await response.Content.ReadFromJsonAsync<IEnumerable<ProProductoAutocompleteDto>>();
        result.Should().NotBeNull();
        result!.Should().HaveCount(1);
        result.First().id.Should().Be(productoId);
        result.First().strTextoAutocomplete.Should().Contain("| #:");
    }

    [Fact]
    public async Task BuscarProducto_EmptyTexto_Returns400()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "/api/v1/ventadetalle/autocomplete?texto=");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", AdminToken);

        var response = await _client.SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Update_ValidUpdate_Returns204()
    {
        var detalle = await CreateDetalleAsync();
        var dto = new { id = detalle.id, idVenVenta = detalle.idVenVenta, idProProducto = detalle.idProProducto, intPiezaVenta = 5, RowVersion = detalle.RowVersion };

        var request = new HttpRequestMessage(HttpMethod.Put, $"/api/v1/ventadetalle/{detalle.id}")
        {
            Content = JsonContent.Create(dto)
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", AdminToken);

        var response = await _client.SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.NoContent);
    }

    [Fact]
    public async Task Update_ValidUpdate_ChangesFieldsInDatabase()
    {
        var detalle = await CreateDetalleAsync();
        var dto = new { id = detalle.id, idVenVenta = detalle.idVenVenta, idProProducto = detalle.idProProducto, intPiezaVenta = 10, RowVersion = detalle.RowVersion };

        var request = new HttpRequestMessage(HttpMethod.Put, $"/api/v1/ventadetalle/{detalle.id}")
        {
            Content = JsonContent.Create(dto)
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", AdminToken);

        var response = await _client.SendAsync(request);
        response.EnsureSuccessStatusCode();

        var getRequest = new HttpRequestMessage(HttpMethod.Get, $"/api/v1/ventadetalle/{detalle.id}");
        getRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", AdminToken);
        var getResponse = await _client.SendAsync(getRequest);
        var updated = await getResponse.Content.ReadFromJsonAsync<VenVentaDetalleDto>();

        updated!.intPiezaVenta.Should().Be(10);
        updated.decTotalVenta.Should().Be(10 * detalle.decTotalVenta / detalle.intPiezaVenta);
    }

    [Fact]
    public async Task Update_NonExistentDetalle_Returns404()
    {
        var (ventaId, productoId, _) = await SeedDependenciesAsync();
        var dto = new { id = 9999, idVenVenta = ventaId, idProProducto = productoId, intPiezaVenta = 1 };

        var request = new HttpRequestMessage(HttpMethod.Put, "/api/v1/ventadetalle/9999")
        {
            Content = JsonContent.Create(dto)
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", AdminToken);

        var response = await _client.SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Update_NonExistentVenta_Returns400()
    {
        var detalle = await CreateDetalleAsync();
        var dto = new { id = detalle.id, idVenVenta = 9999, idProProducto = detalle.idProProducto, intPiezaVenta = 1, RowVersion = detalle.RowVersion };

        var request = new HttpRequestMessage(HttpMethod.Put, $"/api/v1/ventadetalle/{detalle.id}")
        {
            Content = JsonContent.Create(dto)
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", AdminToken);

        var response = await _client.SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Delete_ValidDelete_Returns200()
    {
        var detalle = await CreateDetalleAsync();
        var dto = new { id = detalle.id, RowVersion = detalle.RowVersion };

        var request = new HttpRequestMessage(HttpMethod.Delete, $"/api/v1/ventadetalle/{detalle.id}")
        {
            Content = JsonContent.Create(dto)
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", AdminToken);

        var response = await _client.SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Delete_ValidDelete_RemovesDetalle()
    {
        var detalle = await CreateDetalleAsync();
        var dto = new { id = detalle.id, RowVersion = detalle.RowVersion };

        var request = new HttpRequestMessage(HttpMethod.Delete, $"/api/v1/ventadetalle/{detalle.id}")
        {
            Content = JsonContent.Create(dto)
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", AdminToken);

        var response = await _client.SendAsync(request);
        response.EnsureSuccessStatusCode();

        var getRequest = new HttpRequestMessage(HttpMethod.Get, $"/api/v1/ventadetalle/{detalle.id}");
        getRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", AdminToken);
        var getResponse = await _client.SendAsync(getRequest);

        getResponse.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Delete_NonExistentDetalle_Returns404()
    {
        var dto = new { id = 9999, RowVersion = new byte[] { 1, 0, 0, 0 } };

        var request = new HttpRequestMessage(HttpMethod.Delete, "/api/v1/ventadetalle/9999")
        {
            Content = JsonContent.Create(dto)
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", AdminToken);

        var response = await _client.SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Delete_OnlyRemovesTargetDetalle()
    {
        var detalle1 = await CreateDetalleAsync();
        var detalle2 = await CreateDetalleAsync();
        var dto = new { id = detalle1.id, RowVersion = detalle1.RowVersion };

        var request = new HttpRequestMessage(HttpMethod.Delete, $"/api/v1/ventadetalle/{detalle1.id}")
        {
            Content = JsonContent.Create(dto)
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", AdminToken);

        var response = await _client.SendAsync(request);
        response.EnsureSuccessStatusCode();

        var getRequest = new HttpRequestMessage(HttpMethod.Get, $"/api/v1/ventadetalle/{detalle2.id}");
        getRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", AdminToken);
        var getResponse = await _client.SendAsync(getRequest);

        getResponse.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Delete_ValidDelete_RestoresStockInDatabase()
    {
        var detalle = await CreateDetalleAsync(piezas: 3);

        var searchRequest = new HttpRequestMessage(HttpMethod.Get, "/api/v1/ventadetalle/autocomplete?texto=intproducto");
        searchRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", AdminToken);
        var searchAfterCreate = await _client.SendAsync(searchRequest);
        var productosAfterCreate = await searchAfterCreate.Content.ReadFromJsonAsync<IEnumerable<ProProductoAutocompleteDto>>();
        productosAfterCreate!.First().strTextoAutocomplete.Should().Contain("#: 997");

        var deleteDto = new { id = detalle.id, RowVersion = detalle.RowVersion };
        var deleteRequest = new HttpRequestMessage(HttpMethod.Delete, $"/api/v1/ventadetalle/{detalle.id}")
        {
            Content = JsonContent.Create(deleteDto)
        };
        deleteRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", AdminToken);
        await _client.SendAsync(deleteRequest);

        var searchAfterDeleteRequest = new HttpRequestMessage(HttpMethod.Get, "/api/v1/ventadetalle/autocomplete?texto=intproducto");
        searchAfterDeleteRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", AdminToken);
        var searchAfterDelete = await _client.SendAsync(searchAfterDeleteRequest);
        var productosAfterDelete = await searchAfterDelete.Content.ReadFromJsonAsync<IEnumerable<ProProductoAutocompleteDto>>();
        productosAfterDelete!.First().strTextoAutocomplete.Should().Contain("#: 1000");
    }
}
