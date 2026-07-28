using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using UnitTest.Common;
using WebAPIDevSecOps.Context;
using WebAPIDevSecOps.Dto;
using WebAPIDevSecOps.Services;

namespace IntegrationTest.Producto;

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
            builder.UseSetting("InMemoryDatabaseName", $"IntegrationTestDb_Pro_{Guid.NewGuid():N}");
        });
        _client = _factory.CreateClient();
    }

    public async Task InitializeAsync()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        db.ProProducto.RemoveRange(db.ProProducto);
        await db.SaveChangesAsync();
    }

    public Task DisposeAsync() => Task.CompletedTask;

    private string AdminToken => JwtTestConfig.AdminToken;

    private async Task<ProProductoDto> CreateProductoAsync(string? uniqueName = null)
    {
        var nombre = uniqueName ?? $"intproducto{Guid.NewGuid():N}"[..30];
        var dto = TestDataFactory.CreateProductoCreateDto(
            nombre: nombre,
            existencia: 10,
            precio: 99.99m);

        var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/producto")
        {
            Content = JsonContent.Create(dto)
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", AdminToken);

        var response = await _client.SendAsync(request);
        response.StatusCode.Should().Be(HttpStatusCode.Created);

        var createdProducto = await response.Content.ReadFromJsonAsync<ProProductoDto>();
        createdProducto.Should().NotBeNull();

        var getRequest = new HttpRequestMessage(HttpMethod.Get, $"/api/v1/producto/{createdProducto!.id}");
        getRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", AdminToken);
        var getResponse = await _client.SendAsync(getRequest);
        getResponse.EnsureSuccessStatusCode();
        var fullProducto = await getResponse.Content.ReadFromJsonAsync<ProProductoDto>();
        fullProducto.Should().NotBeNull();

        return fullProducto!;
    }

    private async Task<ProProductoDto> GetProductoAsync(int id)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, $"/api/v1/producto/{id}");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", AdminToken);
        var response = await _client.SendAsync(request);
        response.EnsureSuccessStatusCode();
        var producto = await response.Content.ReadFromJsonAsync<ProProductoDto>();
        producto.Should().NotBeNull();
        return producto!;
    }

    // ==================== GET ALL ====================

    [Fact]
    public async Task GetAll_EmptyDatabase_ReturnsEmptyPagedResult()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "/api/v1/producto");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", AdminToken);

        var response = await _client.SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var result = await response.Content.ReadFromJsonAsync<PagedResult<ProProductoDto>>();
        result.Should().NotBeNull();
        result!.Items.Should().BeEmpty();
        result.TotalCount.Should().Be(0);
        result.PageNumber.Should().Be(1);
        result.PageSize.Should().Be(20);
        result.TotalPages.Should().Be(0);
    }

    [Fact]
    public async Task GetAll_WithProductos_ReturnsDefaultPagination()
    {
        var p1 = await CreateProductoAsync("getalldefault1");
        var p2 = await CreateProductoAsync("getalldefault2");
        var p3 = await CreateProductoAsync("getalldefault3");

        var request = new HttpRequestMessage(HttpMethod.Get, "/api/v1/producto");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", AdminToken);

        var response = await _client.SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var result = await response.Content.ReadFromJsonAsync<PagedResult<ProProductoDto>>();
        result!.Items.Should().HaveCount(3);
        result.TotalCount.Should().Be(3);
        result.PageNumber.Should().Be(1);
        result.PageSize.Should().Be(20);
        result.TotalPages.Should().Be(1);
    }

    [Fact]
    public async Task GetAll_WithCustomPageSize_ReturnsCorrectSize()
    {
        for (int i = 0; i < 15; i++)
            await CreateProductoAsync($"getallsizec{i}");

        var request = new HttpRequestMessage(HttpMethod.Get, "/api/v1/producto?pageSize=5");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", AdminToken);

        var response = await _client.SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var result = await response.Content.ReadFromJsonAsync<PagedResult<ProProductoDto>>();
        result!.Items.Should().HaveCount(5);
        result.TotalCount.Should().Be(15);
        result.PageNumber.Should().Be(1);
        result.PageSize.Should().Be(5);
        result.TotalPages.Should().Be(3);
    }

    [Fact]
    public async Task GetAll_WithCustomPageNumber_ReturnsCorrectPage()
    {
        for (int i = 0; i < 25; i++)
            await CreateProductoAsync($"getallpagec{i}");

        var request = new HttpRequestMessage(HttpMethod.Get, "/api/v1/producto?pageNumber=2&pageSize=10");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", AdminToken);

        var response = await _client.SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var result = await response.Content.ReadFromJsonAsync<PagedResult<ProProductoDto>>();
        result!.Items.Should().HaveCount(10);
        result.TotalCount.Should().Be(25);
        result.PageNumber.Should().Be(2);
        result.PageSize.Should().Be(10);
        result.TotalPages.Should().Be(3);
    }

    [Fact]
    public async Task GetAll_OnLastPage_ReturnsRemainingItems()
    {
        for (int i = 0; i < 25; i++)
            await CreateProductoAsync($"getalllastc{i}");

        var request = new HttpRequestMessage(HttpMethod.Get, "/api/v1/producto?pageNumber=3&pageSize=10");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", AdminToken);

        var response = await _client.SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var result = await response.Content.ReadFromJsonAsync<PagedResult<ProProductoDto>>();
        result!.Items.Should().HaveCount(5);
        result.TotalCount.Should().Be(25);
        result.PageNumber.Should().Be(3);
        result.PageSize.Should().Be(10);
        result.TotalPages.Should().Be(3);
    }

    [Fact]
    public async Task GetAll_WhenPageExceedsTotal_ReturnsEmpty()
    {
        await CreateProductoAsync("getallemptypage");

        var request = new HttpRequestMessage(HttpMethod.Get, "/api/v1/producto?pageNumber=10&pageSize=10");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", AdminToken);

        var response = await _client.SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var result = await response.Content.ReadFromJsonAsync<PagedResult<ProProductoDto>>();
        result!.Items.Should().BeEmpty();
        result.TotalCount.Should().Be(1);
        result.PageNumber.Should().Be(10);
        result.PageSize.Should().Be(10);
        result.TotalPages.Should().Be(1);
    }

    // ==================== SEARCH BY NAME ====================

    [Fact]
    public async Task SearchByName_ReturnsMatchingProductos()
    {
        await CreateProductoAsync("searcheduardo");
        await CreateProductoAsync("searchedel");
        await CreateProductoAsync("searchmaria");

        var request = new HttpRequestMessage(HttpMethod.Get, "/api/v1/producto/buscar?texto=ed");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", AdminToken);

        var response = await _client.SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var result = await response.Content.ReadFromJsonAsync<PagedResult<ProProductoDto>>();
        result!.Items.Should().HaveCount(2);
        result.TotalCount.Should().Be(2);
        result.Items.Select(i => i.strNombreProducto).Should().Contain(["searcheduardo", "searchedel"]);
    }

    [Fact]
    public async Task SearchByName_ReturnsEmpty_WhenNoMatch()
    {
        await CreateProductoAsync("searchjuan");

        var request = new HttpRequestMessage(HttpMethod.Get, "/api/v1/producto/buscar?texto=xyz");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", AdminToken);

        var response = await _client.SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var result = await response.Content.ReadFromJsonAsync<PagedResult<ProProductoDto>>();
        result!.Items.Should().BeEmpty();
        result.TotalCount.Should().Be(0);
    }

    [Fact]
    public async Task SearchByName_WithPagination_ReturnsCorrectPage()
    {
        for (int i = 1; i <= 5; i++)
            await CreateProductoAsync($"searchpageadmin{i}");

        var request = new HttpRequestMessage(HttpMethod.Get, "/api/v1/producto/buscar?texto=admin&pageNumber=2&pageSize=2");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", AdminToken);

        var response = await _client.SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var result = await response.Content.ReadFromJsonAsync<PagedResult<ProProductoDto>>();
        result!.Items.Should().HaveCount(2);
        result.TotalCount.Should().Be(5);
        result.PageNumber.Should().Be(2);
        result.PageSize.Should().Be(2);
    }

    [Fact]
    public async Task SearchByName_EmptyTexto_ReturnsBadRequest()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "/api/v1/producto/buscar?texto=");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", AdminToken);

        var response = await _client.SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task GetAll_HasCacheControlHeader()
    {
        await CreateProductoAsync("getallcache");

        var request = new HttpRequestMessage(HttpMethod.Get, "/api/v1/producto");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", AdminToken);

        var response = await _client.SendAsync(request);

        response.Headers.CacheControl.Should().NotBeNull();
        response.Headers.CacheControl!.NoStore.Should().BeTrue();
    }

    // ==================== GET BY ID ====================

    [Fact]
    public async Task GetById_ExistingProducto_ReturnsProducto()
    {
        var producto = await CreateProductoAsync("getbyidexists");

        var result = await GetProductoAsync(producto.id);

        result.id.Should().Be(producto.id);
        result.strNombreProducto.Should().Be(producto.strNombreProducto);
    }

    [Fact]
    public async Task GetById_NonExistentId_ReturnsNotFound()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "/api/v1/producto/9999");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", AdminToken);

        var response = await _client.SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task GetById_NegativeId_ReturnsNotFound()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "/api/v1/producto/-1");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", AdminToken);

        var response = await _client.SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task GetById_ZeroId_ReturnsNotFound()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "/api/v1/producto/0");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", AdminToken);

        var response = await _client.SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task GetById_MultipleProductos_ReturnsCorrectProducto()
    {
        var c1 = await CreateProductoAsync("getbyidmultia");
        var c2 = await CreateProductoAsync("getbyidmultib");
        var c3 = await CreateProductoAsync("getbyidmultic");

        var result = await GetProductoAsync(c2.id);

        result.id.Should().Be(c2.id);
        result.strNombreProducto.Should().Be("getbyidmultib");
    }

    // ==================== CREATE ====================

    [Fact]
    public async Task Create_ValidDto_Returns201WithLocationHeader()
    {
        var dto = TestDataFactory.CreateProductoCreateDto(
            nombre: "createlocation",
            existencia: 10,
            precio: 99.99m);

        var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/producto")
        {
            Content = JsonContent.Create(dto)
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", AdminToken);

        var response = await _client.SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.Created);
        response.Headers.Location.Should().NotBeNull();
        response.Headers.Location!.ToString().Should().Contain("/api/v1/Producto/");
    }

    [Fact]
    public async Task Create_ValidDto_ReturnsDtoWithId()
    {
        var dto = TestDataFactory.CreateProductoCreateDto(
            nombre: "createwithid",
            existencia: 10,
            precio: 99.99m);

        var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/producto")
        {
            Content = JsonContent.Create(dto)
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", AdminToken);

        var response = await _client.SendAsync(request);

        var createdProducto = await response.Content.ReadFromJsonAsync<ProProductoDto>();
        createdProducto.Should().NotBeNull();
        createdProducto!.id.Should().BeGreaterThan(0);
    }

    [Fact]
    public async Task Create_ValidDto_ReturnsCorrectData()
    {
        var dto = TestDataFactory.CreateProductoCreateDto(
            nombre: "createcorrectdata",
            existencia: 25,
            precio: 149.99m);

        var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/producto")
        {
            Content = JsonContent.Create(dto)
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", AdminToken);

        var response = await _client.SendAsync(request);

        var createdProducto = await response.Content.ReadFromJsonAsync<ProProductoDto>();
        createdProducto!.strNombreProducto.Should().Be("createcorrectdata");
        createdProducto.intNumeroExistencia.Should().Be(25);
        createdProducto.decPrecio.Should().Be(149.99m);
    }

    [Fact]
    public async Task Create_ValidDto_CreatesProductoInDatabase()
    {
        var dto = TestDataFactory.CreateProductoCreateDto(
            nombre: "createpersist",
            existencia: 10,
            precio: 99.99m);

        var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/producto")
        {
            Content = JsonContent.Create(dto)
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", AdminToken);

        var response = await _client.SendAsync(request);

        var createdProducto = await response.Content.ReadFromJsonAsync<ProProductoDto>();

        var getRequest = new HttpRequestMessage(HttpMethod.Get, $"/api/v1/producto/{createdProducto!.id}");
        getRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", AdminToken);
        var getResponse = await _client.SendAsync(getRequest);

        getResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var fetchedProducto = await getResponse.Content.ReadFromJsonAsync<ProProductoDto>();
        fetchedProducto!.strNombreProducto.Should().Be("createpersist");
    }

    [Fact]
    public async Task Create_EmptyNombre_Returns400()
    {
        var dto = TestDataFactory.CreateProductoCreateDto(
            nombre: "",
            existencia: 10,
            precio: 99.99m);

        var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/producto")
        {
            Content = JsonContent.Create(dto)
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", AdminToken);

        var response = await _client.SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Create_NombreTooLong_Returns400()
    {
        var dto = TestDataFactory.CreateProductoCreateDto(
            nombre: new string('a', 51),
            existencia: 10,
            precio: 99.99m);

        var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/producto")
        {
            Content = JsonContent.Create(dto)
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", AdminToken);

        var response = await _client.SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Create_TrimsNombreProducto()
    {
        var dto = TestDataFactory.CreateProductoCreateDto(
            nombre: "  createtrimmed  ",
            existencia: 10,
            precio: 99.99m);

        var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/producto")
        {
            Content = JsonContent.Create(dto)
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", AdminToken);

        var response = await _client.SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.Created);

        var createdProducto = await response.Content.ReadFromJsonAsync<ProProductoDto>();

        var getRequest = new HttpRequestMessage(HttpMethod.Get, $"/api/v1/producto/{createdProducto!.id}");
        getRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", AdminToken);
        var getResponse = await _client.SendAsync(getRequest);

        var fetchedProducto = await getResponse.Content.ReadFromJsonAsync<ProProductoDto>();
        fetchedProducto!.strNombreProducto.Should().Be("createtrimmed");
    }

    [Fact]
    public async Task Create_AllowsDuplicateNombre()
    {
        var dto1 = TestDataFactory.CreateProductoCreateDto(
            nombre: "NombreDuplicado",
            existencia: 10,
            precio: 99.99m);

        var request1 = new HttpRequestMessage(HttpMethod.Post, "/api/v1/producto")
        {
            Content = JsonContent.Create(dto1)
        };
        request1.Headers.Authorization = new AuthenticationHeaderValue("Bearer", AdminToken);
        var response1 = await _client.SendAsync(request1);
        response1.StatusCode.Should().Be(HttpStatusCode.Created);

        var dto2 = TestDataFactory.CreateProductoCreateDto(
            nombre: "NombreDuplicado",
            existencia: 20,
            precio: 199.99m);

        var request2 = new HttpRequestMessage(HttpMethod.Post, "/api/v1/producto")
        {
            Content = JsonContent.Create(dto2)
        };
        request2.Headers.Authorization = new AuthenticationHeaderValue("Bearer", AdminToken);

        var response2 = await _client.SendAsync(request2);
        response2.StatusCode.Should().Be(HttpStatusCode.Created);
    }

    // ==================== UPDATE ====================

    [Fact]
    public async Task Update_ValidUpdate_Returns204()
    {
        var producto = await CreateProductoAsync("updatevalid");

        var dto = new
        {
            id = producto.id,
            strNombreProducto = "updatevalidmodified",
            intNumeroExistencia = 20,
            decPrecio = 199.99m,
            rowVersion = producto.RowVersion
        };

        var request = new HttpRequestMessage(HttpMethod.Put, $"/api/v1/producto/{producto.id}")
        {
            Content = JsonContent.Create(dto)
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", AdminToken);

        var response = await _client.SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.NoContent);
    }

    [Fact]
    public async Task Update_ValidUpdate_ChangesData()
    {
        var producto = await CreateProductoAsync("updatechanges");

        var dto = new
        {
            id = producto.id,
            strNombreProducto = "updatechangesmodified",
            intNumeroExistencia = 50,
            decPrecio = 250.00m,
            rowVersion = producto.RowVersion
        };

        var request = new HttpRequestMessage(HttpMethod.Put, $"/api/v1/producto/{producto.id}")
        {
            Content = JsonContent.Create(dto)
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", AdminToken);

        await _client.SendAsync(request);

        var updatedProducto = await GetProductoAsync(producto.id);
        updatedProducto.strNombreProducto.Should().Be("updatechangesmodified");
        updatedProducto.intNumeroExistencia.Should().Be(50);
        updatedProducto.decPrecio.Should().Be(250.00m);
    }

    [Fact]
    public async Task Update_RouteIdMismatch_Returns400()
    {
        var producto = await CreateProductoAsync("updateidmismatch");

        var dto = new
        {
            id = 999,
            strNombreProducto = "shouldnotwork",
            intNumeroExistencia = 10,
            decPrecio = 99.99m,
            rowVersion = producto.RowVersion
        };

        var request = new HttpRequestMessage(HttpMethod.Put, $"/api/v1/producto/{producto.id}")
        {
            Content = JsonContent.Create(dto)
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", AdminToken);

        var response = await _client.SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Update_NonExistentId_Returns404()
    {
        var dto = new
        {
            id = 9999,
            strNombreProducto = "nonexistent",
            intNumeroExistencia = 10,
            decPrecio = 99.99m,
            rowVersion = new byte[] { 1, 0, 0, 0 }
        };

        var request = new HttpRequestMessage(HttpMethod.Put, "/api/v1/producto/9999")
        {
            Content = JsonContent.Create(dto)
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", AdminToken);

        var response = await _client.SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Update_StaleRowVersion_Returns409()
    {
        var producto = await CreateProductoAsync("updatestalerv");

        var dto = new
        {
            id = producto.id,
            strNombreProducto = "updatestalervmodified",
            intNumeroExistencia = 20,
            decPrecio = 199.99m,
            rowVersion = new byte[] { 2, 0, 0, 0 }
        };

        var request = new HttpRequestMessage(HttpMethod.Put, $"/api/v1/producto/{producto.id}")
        {
            Content = JsonContent.Create(dto)
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", AdminToken);

        var response = await _client.SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task Update_EmptyNombre_Returns400()
    {
        var producto = await CreateProductoAsync("updateempty");

        var dto = new
        {
            id = producto.id,
            strNombreProducto = "",
            intNumeroExistencia = 10,
            decPrecio = 99.99m,
            rowVersion = producto.RowVersion
        };

        var request = new HttpRequestMessage(HttpMethod.Put, $"/api/v1/producto/{producto.id}")
        {
            Content = JsonContent.Create(dto)
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", AdminToken);

        var response = await _client.SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Update_NombreTooLong_Returns400()
    {
        var producto = await CreateProductoAsync("updatetoolong");

        var dto = new
        {
            id = producto.id,
            strNombreProducto = new string('a', 51),
            intNumeroExistencia = 10,
            decPrecio = 99.99m,
            rowVersion = producto.RowVersion
        };

        var request = new HttpRequestMessage(HttpMethod.Put, $"/api/v1/producto/{producto.id}")
        {
            Content = JsonContent.Create(dto)
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", AdminToken);

        var response = await _client.SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    // ==================== DELETE ====================

    [Fact]
    public async Task Delete_ValidDelete_Returns200()
    {
        var producto = await CreateProductoAsync("deletevalid");

        var dto = new
        {
            id = producto.id,
            rowVersion = producto.RowVersion
        };

        var request = new HttpRequestMessage(HttpMethod.Delete, $"/api/v1/producto/{producto.id}")
        {
            Content = JsonContent.Create(dto)
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", AdminToken);

        var response = await _client.SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Delete_ValidDelete_RemovesProductoFromDatabase()
    {
        var producto = await CreateProductoAsync("deleteremoves");

        var dto = new
        {
            id = producto.id,
            rowVersion = producto.RowVersion
        };

        var request = new HttpRequestMessage(HttpMethod.Delete, $"/api/v1/producto/{producto.id}")
        {
            Content = JsonContent.Create(dto)
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", AdminToken);

        await _client.SendAsync(request);

        var getRequest = new HttpRequestMessage(HttpMethod.Get, $"/api/v1/producto/{producto.id}");
        getRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", AdminToken);

        var getResponse = await _client.SendAsync(getRequest);
        getResponse.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Delete_RouteIdMismatch_Returns400()
    {
        var producto = await CreateProductoAsync("deleteidmismatch");

        var dto = new
        {
            id = 999,
            rowVersion = producto.RowVersion
        };

        var request = new HttpRequestMessage(HttpMethod.Delete, $"/api/v1/producto/{producto.id}")
        {
            Content = JsonContent.Create(dto)
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", AdminToken);

        var response = await _client.SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Delete_NonExistentId_Returns404()
    {
        var dto = new
        {
            id = 9999,
            rowVersion = new byte[] { 1, 0, 0, 0 }
        };

        var request = new HttpRequestMessage(HttpMethod.Delete, "/api/v1/producto/9999")
        {
            Content = JsonContent.Create(dto)
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", AdminToken);

        var response = await _client.SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Delete_OnlyRemovesTargetProducto()
    {
        var c1 = await CreateProductoAsync("deleteonlya");
        var c2 = await CreateProductoAsync("deleteonlyb");

        var dto = new
        {
            id = c1.id,
            rowVersion = c1.RowVersion
        };

        var request = new HttpRequestMessage(HttpMethod.Delete, $"/api/v1/producto/{c1.id}")
        {
            Content = JsonContent.Create(dto)
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", AdminToken);

        await _client.SendAsync(request);

        var getDeletedRequest = new HttpRequestMessage(HttpMethod.Get, $"/api/v1/producto/{c1.id}");
        getDeletedRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", AdminToken);
        var getDeletedResponse = await _client.SendAsync(getDeletedRequest);
        getDeletedResponse.StatusCode.Should().Be(HttpStatusCode.NotFound);

        var getRemainingRequest = new HttpRequestMessage(HttpMethod.Get, $"/api/v1/producto/{c2.id}");
        getRemainingRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", AdminToken);
        var getRemainingResponse = await _client.SendAsync(getRemainingRequest);
        getRemainingResponse.StatusCode.Should().Be(HttpStatusCode.OK);

        var remainingProducto = await getRemainingResponse.Content.ReadFromJsonAsync<ProProductoDto>();
        remainingProducto!.id.Should().Be(c2.id);
        remainingProducto.strNombreProducto.Should().Be("deleteonlyb");
    }

    [Fact]
    public async Task Delete_StaleRowVersion_Returns409()
    {
        var producto = await CreateProductoAsync("deletestalerv");

        var dto = new
        {
            id = producto.id,
            rowVersion = new byte[] { 2, 0, 0, 0 }
        };

        var request = new HttpRequestMessage(HttpMethod.Delete, $"/api/v1/producto/{producto.id}")
        {
            Content = JsonContent.Create(dto)
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", AdminToken);

        var response = await _client.SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
    }

    // ==================== FULL LIFECYCLE ====================

    [Fact]
    public async Task FullLifecycle_CreateGetUpdateGetDelete_CompleteFlow()
    {
        var nombre = "lifecycleproducto";
        var dto = TestDataFactory.CreateProductoCreateDto(
            nombre: nombre,
            existencia: 10,
            precio: 99.99m);

        var createRequest = new HttpRequestMessage(HttpMethod.Post, "/api/v1/producto")
        {
            Content = JsonContent.Create(dto)
        };
        createRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", AdminToken);

        var createResponse = await _client.SendAsync(createRequest);
        createResponse.StatusCode.Should().Be(HttpStatusCode.Created);

        var createdProducto = await createResponse.Content.ReadFromJsonAsync<ProProductoDto>();

        var getAfterCreate = await GetProductoAsync(createdProducto!.id);
        getAfterCreate.strNombreProducto.Should().Be(nombre);
        getAfterCreate.intNumeroExistencia.Should().Be(10);
        getAfterCreate.decPrecio.Should().Be(99.99m);

        var updateDto = new
        {
            id = createdProducto.id,
            strNombreProducto = "lifecycleproductoupdated",
            intNumeroExistencia = 50,
            decPrecio = 250.00m,
            rowVersion = getAfterCreate.RowVersion
        };

        var updateRequest = new HttpRequestMessage(HttpMethod.Put, $"/api/v1/producto/{createdProducto.id}")
        {
            Content = JsonContent.Create(updateDto)
        };
        updateRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", AdminToken);

        var updateResponse = await _client.SendAsync(updateRequest);
        updateResponse.StatusCode.Should().Be(HttpStatusCode.NoContent);

        var getAfterUpdate = await GetProductoAsync(createdProducto.id);
        getAfterUpdate.strNombreProducto.Should().Be("lifecycleproductoupdated");
        getAfterUpdate.intNumeroExistencia.Should().Be(50);
        getAfterUpdate.decPrecio.Should().Be(250.00m);

        var deleteDto = new
        {
            id = createdProducto.id,
            rowVersion = getAfterUpdate.RowVersion
        };

        var deleteRequest = new HttpRequestMessage(HttpMethod.Delete, $"/api/v1/producto/{createdProducto.id}")
        {
            Content = JsonContent.Create(deleteDto)
        };
        deleteRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", AdminToken);

        var deleteResponse = await _client.SendAsync(deleteRequest);
        deleteResponse.StatusCode.Should().Be(HttpStatusCode.OK);

        var getAfterDelete = new HttpRequestMessage(HttpMethod.Get, $"/api/v1/producto/{createdProducto.id}");
        getAfterDelete.Headers.Authorization = new AuthenticationHeaderValue("Bearer", AdminToken);

        var finalResponse = await _client.SendAsync(getAfterDelete);
        finalResponse.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }
}
