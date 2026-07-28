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

namespace IntegrationTest.Clientes;

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
            builder.UseSetting("InMemoryDatabaseName", $"IntegrationTestDb_Cli_{Guid.NewGuid():N}");
        });
        _client = _factory.CreateClient();
    }

    public async Task InitializeAsync()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        db.CliCliente.RemoveRange(db.CliCliente);
        await db.SaveChangesAsync();
    }

    public Task DisposeAsync() => Task.CompletedTask;

    private string AdminToken => JwtTestConfig.AdminToken;

    private async Task<CliClienteDto> CreateClienteAsync(string? uniqueName = null)
    {
        var nombre = uniqueName ?? $"intcliente{Guid.NewGuid():N}"[..30];
        var dto = TestDataFactory.CreateClienteCreateDto(
            nombre: nombre,
            correo: $"{nombre}@test.com",
            telefono: "5512345678");

        var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/cliente")
        {
            Content = JsonContent.Create(dto)
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", AdminToken);

        var response = await _client.SendAsync(request);
        response.StatusCode.Should().Be(HttpStatusCode.Created);

        var createdCliente = await response.Content.ReadFromJsonAsync<CliClienteDto>();
        createdCliente.Should().NotBeNull();

        var getRequest = new HttpRequestMessage(HttpMethod.Get, $"/api/v1/cliente/{createdCliente!.id}");
        getRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", AdminToken);
        var getResponse = await _client.SendAsync(getRequest);
        getResponse.EnsureSuccessStatusCode();
        var fullCliente = await getResponse.Content.ReadFromJsonAsync<CliClienteDto>();
        fullCliente.Should().NotBeNull();

        return fullCliente!;
    }

    private async Task<CliClienteDto> GetClienteAsync(int id)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, $"/api/v1/cliente/{id}");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", AdminToken);
        var response = await _client.SendAsync(request);
        response.EnsureSuccessStatusCode();
        var cliente = await response.Content.ReadFromJsonAsync<CliClienteDto>();
        cliente.Should().NotBeNull();
        return cliente!;
    }

    // ==================== GET ALL ====================

    [Fact]
    public async Task GetAll_EmptyDatabase_ReturnsEmptyPagedResult()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "/api/v1/cliente");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", AdminToken);

        var response = await _client.SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var result = await response.Content.ReadFromJsonAsync<PagedResult<CliClienteDto>>();
        result.Should().NotBeNull();
        result!.Items.Should().BeEmpty();
        result.TotalCount.Should().Be(0);
        result.PageNumber.Should().Be(1);
        result.PageSize.Should().Be(20);
        result.TotalPages.Should().Be(0);
    }

    [Fact]
    public async Task GetAll_WithClientes_ReturnsDefaultPagination()
    {
        var c1 = await CreateClienteAsync("getalldefault1");
        var c2 = await CreateClienteAsync("getalldefault2");
        var c3 = await CreateClienteAsync("getalldefault3");

        var request = new HttpRequestMessage(HttpMethod.Get, "/api/v1/cliente");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", AdminToken);

        var response = await _client.SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var result = await response.Content.ReadFromJsonAsync<PagedResult<CliClienteDto>>();
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
            await CreateClienteAsync($"getallsizec{i}");

        var request = new HttpRequestMessage(HttpMethod.Get, "/api/v1/cliente?pageSize=5");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", AdminToken);

        var response = await _client.SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var result = await response.Content.ReadFromJsonAsync<PagedResult<CliClienteDto>>();
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
            await CreateClienteAsync($"getallpagec{i}");

        var request = new HttpRequestMessage(HttpMethod.Get, "/api/v1/cliente?pageNumber=2&pageSize=10");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", AdminToken);

        var response = await _client.SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var result = await response.Content.ReadFromJsonAsync<PagedResult<CliClienteDto>>();
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
            await CreateClienteAsync($"getalllastc{i}");

        var request = new HttpRequestMessage(HttpMethod.Get, "/api/v1/cliente?pageNumber=3&pageSize=10");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", AdminToken);

        var response = await _client.SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var result = await response.Content.ReadFromJsonAsync<PagedResult<CliClienteDto>>();
        result!.Items.Should().HaveCount(5);
        result.TotalCount.Should().Be(25);
        result.PageNumber.Should().Be(3);
        result.PageSize.Should().Be(10);
        result.TotalPages.Should().Be(3);
    }

    [Fact]
    public async Task GetAll_WhenPageExceedsTotal_ReturnsEmpty()
    {
        await CreateClienteAsync("getallemptypage");

        var request = new HttpRequestMessage(HttpMethod.Get, "/api/v1/cliente?pageNumber=10&pageSize=10");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", AdminToken);

        var response = await _client.SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var result = await response.Content.ReadFromJsonAsync<PagedResult<CliClienteDto>>();
        result!.Items.Should().BeEmpty();
        result.TotalCount.Should().Be(1);
        result.PageNumber.Should().Be(10);
        result.PageSize.Should().Be(10);
        result.TotalPages.Should().Be(1);
    }

    // ==================== SEARCH BY NAME ====================

    [Fact]
    public async Task SearchByName_ReturnsMatchingClientes()
    {
        await CreateClienteAsync("searcheduardo");
        await CreateClienteAsync("searchedel");
        await CreateClienteAsync("searchmaria");

        var request = new HttpRequestMessage(HttpMethod.Get, "/api/v1/cliente/buscar?texto=ed");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", AdminToken);

        var response = await _client.SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var result = await response.Content.ReadFromJsonAsync<PagedResult<CliClienteDto>>();
        result!.Items.Should().HaveCount(2);
        result.TotalCount.Should().Be(2);
        result.Items.Select(i => i.strNombreCliente).Should().Contain(["searcheduardo", "searchedel"]);
    }

    [Fact]
    public async Task SearchByName_ReturnsEmpty_WhenNoMatch()
    {
        await CreateClienteAsync("searchjuan");

        var request = new HttpRequestMessage(HttpMethod.Get, "/api/v1/cliente/buscar?texto=xyz");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", AdminToken);

        var response = await _client.SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var result = await response.Content.ReadFromJsonAsync<PagedResult<CliClienteDto>>();
        result!.Items.Should().BeEmpty();
        result.TotalCount.Should().Be(0);
    }

    [Fact]
    public async Task SearchByName_WithPagination_ReturnsCorrectPage()
    {
        for (int i = 1; i <= 5; i++)
            await CreateClienteAsync($"searchpageadmin{i}");

        var request = new HttpRequestMessage(HttpMethod.Get, "/api/v1/cliente/buscar?texto=admin&pageNumber=2&pageSize=2");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", AdminToken);

        var response = await _client.SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var result = await response.Content.ReadFromJsonAsync<PagedResult<CliClienteDto>>();
        result!.Items.Should().HaveCount(2);
        result.TotalCount.Should().Be(5);
        result.PageNumber.Should().Be(2);
        result.PageSize.Should().Be(2);
    }

    [Fact]
    public async Task SearchByName_EmptyTexto_ReturnsBadRequest()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "/api/v1/cliente/buscar?texto=");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", AdminToken);

        var response = await _client.SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task GetAll_HasCacheControlHeader()
    {
        await CreateClienteAsync("getallcache");

        var request = new HttpRequestMessage(HttpMethod.Get, "/api/v1/cliente");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", AdminToken);

        var response = await _client.SendAsync(request);

        response.Headers.CacheControl.Should().NotBeNull();
        response.Headers.CacheControl!.NoStore.Should().BeTrue();
    }

    // ==================== GET BY ID ====================

    [Fact]
    public async Task GetById_ExistingCliente_ReturnsCliente()
    {
        var cliente = await CreateClienteAsync("getbyidexists");

        var result = await GetClienteAsync(cliente.id);

        result.id.Should().Be(cliente.id);
        result.strNombreCliente.Should().Be(cliente.strNombreCliente);
        result.strCorreoElectronico.Should().Be(cliente.strCorreoElectronico);
    }

    [Fact]
    public async Task GetById_NonExistentId_ReturnsNotFound()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "/api/v1/cliente/9999");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", AdminToken);

        var response = await _client.SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task GetById_NegativeId_ReturnsNotFound()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "/api/v1/cliente/-1");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", AdminToken);

        var response = await _client.SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task GetById_ZeroId_ReturnsNotFound()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "/api/v1/cliente/0");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", AdminToken);

        var response = await _client.SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task GetById_MultipleClientes_ReturnsCorrectCliente()
    {
        var c1 = await CreateClienteAsync("getbyidmultia");
        var c2 = await CreateClienteAsync("getbyidmultib");
        var c3 = await CreateClienteAsync("getbyidmultic");

        var result = await GetClienteAsync(c2.id);

        result.id.Should().Be(c2.id);
        result.strNombreCliente.Should().Be("getbyidmultib");
        result.strCorreoElectronico.Should().Be("getbyidmultib@test.com");
    }

    // ==================== CREATE ====================

    [Fact]
    public async Task Create_ValidDto_Returns201WithLocationHeader()
    {
        var dto = TestDataFactory.CreateClienteCreateDto(
            nombre: "createlocation",
            correo: "createlocation@test.com",
            telefono: "5512345678");

        var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/cliente")
        {
            Content = JsonContent.Create(dto)
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", AdminToken);

        var response = await _client.SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.Created);
        response.Headers.Location.Should().NotBeNull();
        response.Headers.Location!.ToString().Should().Contain("/api/v1/Cliente/");
    }

    [Fact]
    public async Task Create_ValidDto_ReturnsDtoWithId()
    {
        var dto = TestDataFactory.CreateClienteCreateDto(
            nombre: "createwithid",
            correo: "createwithid@test.com",
            telefono: "5512345678");

        var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/cliente")
        {
            Content = JsonContent.Create(dto)
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", AdminToken);

        var response = await _client.SendAsync(request);

        var createdCliente = await response.Content.ReadFromJsonAsync<CliClienteDto>();
        createdCliente.Should().NotBeNull();
        createdCliente!.id.Should().BeGreaterThan(0);
    }

    [Fact]
    public async Task Create_ValidDto_ReturnsCorrectData()
    {
        var dto = TestDataFactory.CreateClienteCreateDto(
            nombre: "createcorrectdata",
            correo: "createcorrectdata@test.com",
            telefono: "5512345678");

        var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/cliente")
        {
            Content = JsonContent.Create(dto)
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", AdminToken);

        var response = await _client.SendAsync(request);

        var createdCliente = await response.Content.ReadFromJsonAsync<CliClienteDto>();
        createdCliente!.strNombreCliente.Should().Be("createcorrectdata");
        createdCliente.strCorreoElectronico.Should().Be("createcorrectdata@test.com");
        createdCliente.strNumeroTelefono.Should().Be("5512345678");
    }

    [Fact]
    public async Task Create_ValidDto_CreatesClienteInDatabase()
    {
        var dto = TestDataFactory.CreateClienteCreateDto(
            nombre: "createpersist",
            correo: "createpersist@test.com",
            telefono: "5512345678");

        var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/cliente")
        {
            Content = JsonContent.Create(dto)
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", AdminToken);

        var response = await _client.SendAsync(request);

        var createdCliente = await response.Content.ReadFromJsonAsync<CliClienteDto>();

        var getRequest = new HttpRequestMessage(HttpMethod.Get, $"/api/v1/cliente/{createdCliente!.id}");
        getRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", AdminToken);
        var getResponse = await _client.SendAsync(getRequest);

        getResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var fetchedCliente = await getResponse.Content.ReadFromJsonAsync<CliClienteDto>();
        fetchedCliente!.strNombreCliente.Should().Be("createpersist");
    }

    [Fact]
    public async Task Create_DuplicateCorreo_Returns400()
    {
        await CreateClienteAsync("createdupcorreo");

        var dto = TestDataFactory.CreateClienteCreateDto(
            nombre: "createdupother",
            correo: "createdupcorreo@test.com",
            telefono: "5512345678");

        var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/cliente")
        {
            Content = JsonContent.Create(dto)
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", AdminToken);

        var response = await _client.SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Create_EmptyNombre_Returns400()
    {
        var dto = TestDataFactory.CreateClienteCreateDto(
            nombre: "",
            correo: "createempty@test.com",
            telefono: "5512345678");

        var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/cliente")
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
        var dto = TestDataFactory.CreateClienteCreateDto(
            nombre: new string('a', 101),
            correo: "createlong@test.com",
            telefono: "5512345678");

        var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/cliente")
        {
            Content = JsonContent.Create(dto)
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", AdminToken);

        var response = await _client.SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Create_InvalidEmail_Returns400()
    {
        var dto = TestDataFactory.CreateClienteCreateDto(
            nombre: "createbademail",
            correo: "not-an-email",
            telefono: "5512345678");

        var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/cliente")
        {
            Content = JsonContent.Create(dto)
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", AdminToken);

        var response = await _client.SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Create_TrimsNombreCliente()
    {
        var dto = TestDataFactory.CreateClienteCreateDto(
            nombre: "  createtrimmed  ",
            correo: "createtrimmed@test.com",
            telefono: "5512345678");

        var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/cliente")
        {
            Content = JsonContent.Create(dto)
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", AdminToken);

        var response = await _client.SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.Created);

        var createdCliente = await response.Content.ReadFromJsonAsync<CliClienteDto>();

        var getRequest = new HttpRequestMessage(HttpMethod.Get, $"/api/v1/cliente/{createdCliente!.id}");
        getRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", AdminToken);
        var getResponse = await _client.SendAsync(getRequest);

        var fetchedCliente = await getResponse.Content.ReadFromJsonAsync<CliClienteDto>();
        fetchedCliente!.strNombreCliente.Should().Be("createtrimmed");
    }

    // ==================== UPDATE ====================

    [Fact]
    public async Task Update_ValidUpdate_Returns204()
    {
        var cliente = await CreateClienteAsync("updatevalid");

        var dto = new
        {
            id = cliente.id,
            strNombreCliente = "updatevalidmodified",
            strCorreoElectronico = "updatevalidmodified@test.com",
            strNumeroTelefono = "5512345678",
            rowVersion = cliente.RowVersion
        };

        var request = new HttpRequestMessage(HttpMethod.Put, $"/api/v1/cliente/{cliente.id}")
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
        var cliente = await CreateClienteAsync("updatechanges");

        var dto = new
        {
            id = cliente.id,
            strNombreCliente = "updatechangesmodified",
            strCorreoElectronico = "updatechangesmodified@test.com",
            strNumeroTelefono = "5599999999",
            rowVersion = cliente.RowVersion
        };

        var request = new HttpRequestMessage(HttpMethod.Put, $"/api/v1/cliente/{cliente.id}")
        {
            Content = JsonContent.Create(dto)
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", AdminToken);

        await _client.SendAsync(request);

        var updatedCliente = await GetClienteAsync(cliente.id);
        updatedCliente.strNombreCliente.Should().Be("updatechangesmodified");
        updatedCliente.strCorreoElectronico.Should().Be("updatechangesmodified@test.com");
        updatedCliente.strNumeroTelefono.Should().Be("5599999999");
    }

    [Fact]
    public async Task Update_RouteIdMismatch_Returns400()
    {
        var cliente = await CreateClienteAsync("updateidmismatch");

        var dto = new
        {
            id = 999,
            strNombreCliente = "shouldnotwork",
            strCorreoElectronico = "shouldnotwork@test.com",
            strNumeroTelefono = "5512345678",
            rowVersion = cliente.RowVersion
        };

        var request = new HttpRequestMessage(HttpMethod.Put, $"/api/v1/cliente/{cliente.id}")
        {
            Content = JsonContent.Create(dto)
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", AdminToken);

        var response = await _client.SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Update_DuplicateCorreo_Returns400()
    {
        var c1 = await CreateClienteAsync("updatedupa");
        var c2 = await CreateClienteAsync("updatedupb");

        var dto = new
        {
            id = c2.id,
            strNombreCliente = c2.strNombreCliente,
            strCorreoElectronico = "updatedupa@test.com",
            strNumeroTelefono = "5512345678",
            rowVersion = c2.RowVersion
        };

        var request = new HttpRequestMessage(HttpMethod.Put, $"/api/v1/cliente/{c2.id}")
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
            strNombreCliente = "nonexistent",
            strCorreoElectronico = "nonexistent@test.com",
            strNumeroTelefono = "5512345678",
            rowVersion = new byte[] { 1, 0, 0, 0 }
        };

        var request = new HttpRequestMessage(HttpMethod.Put, "/api/v1/cliente/9999")
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
        var cliente = await CreateClienteAsync("updatestalerv");

        var dto = new
        {
            id = cliente.id,
            strNombreCliente = "updatestalervmodified",
            strCorreoElectronico = "updatestalervmodified@test.com",
            strNumeroTelefono = "5512345678",
            rowVersion = new byte[] { 2, 0, 0, 0 }
        };

        var request = new HttpRequestMessage(HttpMethod.Put, $"/api/v1/cliente/{cliente.id}")
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
        var cliente = await CreateClienteAsync("updateempty");

        var dto = new
        {
            id = cliente.id,
            strNombreCliente = "",
            strCorreoElectronico = cliente.strCorreoElectronico,
            strNumeroTelefono = "5512345678",
            rowVersion = cliente.RowVersion
        };

        var request = new HttpRequestMessage(HttpMethod.Put, $"/api/v1/cliente/{cliente.id}")
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
        var cliente = await CreateClienteAsync("updatetoolong");

        var dto = new
        {
            id = cliente.id,
            strNombreCliente = new string('a', 101),
            strCorreoElectronico = cliente.strCorreoElectronico,
            strNumeroTelefono = "5512345678",
            rowVersion = cliente.RowVersion
        };

        var request = new HttpRequestMessage(HttpMethod.Put, $"/api/v1/cliente/{cliente.id}")
        {
            Content = JsonContent.Create(dto)
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", AdminToken);

        var response = await _client.SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Update_InvalidEmail_Returns400()
    {
        var cliente = await CreateClienteAsync("updatebademail");

        var dto = new
        {
            id = cliente.id,
            strNombreCliente = cliente.strNombreCliente,
            strCorreoElectronico = "not-an-email",
            strNumeroTelefono = "5512345678",
            rowVersion = cliente.RowVersion
        };

        var request = new HttpRequestMessage(HttpMethod.Put, $"/api/v1/cliente/{cliente.id}")
        {
            Content = JsonContent.Create(dto)
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", AdminToken);

        var response = await _client.SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Update_TrimsNombreCliente()
    {
        var cliente = await CreateClienteAsync("updatetrim");

        var dto = new
        {
            id = cliente.id,
            strNombreCliente = "  updatetrimmodified  ",
            strCorreoElectronico = cliente.strCorreoElectronico,
            strNumeroTelefono = "5512345678",
            rowVersion = cliente.RowVersion
        };

        var request = new HttpRequestMessage(HttpMethod.Put, $"/api/v1/cliente/{cliente.id}")
        {
            Content = JsonContent.Create(dto)
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", AdminToken);

        await _client.SendAsync(request);

        var updatedCliente = await GetClienteAsync(cliente.id);
        updatedCliente.strNombreCliente.Should().Be("updatetrimmodified");
    }

    [Fact]
    public async Task Update_SelfRename_AllowsSameCorreo()
    {
        var cliente = await CreateClienteAsync("updateselfrename");

        var dto = new
        {
            id = cliente.id,
            strNombreCliente = cliente.strNombreCliente,
            strCorreoElectronico = "updateselfrename@test.com",
            strNumeroTelefono = "5512345678",
            rowVersion = cliente.RowVersion
        };

        var request = new HttpRequestMessage(HttpMethod.Put, $"/api/v1/cliente/{cliente.id}")
        {
            Content = JsonContent.Create(dto)
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", AdminToken);

        var response = await _client.SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.NoContent);
    }

    // ==================== DELETE ====================

    [Fact]
    public async Task Delete_ValidDelete_Returns200()
    {
        var cliente = await CreateClienteAsync("deletevalid");

        var dto = new
        {
            id = cliente.id,
            rowVersion = cliente.RowVersion
        };

        var request = new HttpRequestMessage(HttpMethod.Delete, $"/api/v1/cliente/{cliente.id}")
        {
            Content = JsonContent.Create(dto)
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", AdminToken);

        var response = await _client.SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Delete_ValidDelete_RemovesClienteFromDatabase()
    {
        var cliente = await CreateClienteAsync("deleteremoves");

        var dto = new
        {
            id = cliente.id,
            rowVersion = cliente.RowVersion
        };

        var request = new HttpRequestMessage(HttpMethod.Delete, $"/api/v1/cliente/{cliente.id}")
        {
            Content = JsonContent.Create(dto)
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", AdminToken);

        await _client.SendAsync(request);

        var getRequest = new HttpRequestMessage(HttpMethod.Get, $"/api/v1/cliente/{cliente.id}");
        getRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", AdminToken);

        var getResponse = await _client.SendAsync(getRequest);
        getResponse.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Delete_RouteIdMismatch_Returns400()
    {
        var cliente = await CreateClienteAsync("deleteidmismatch");

        var dto = new
        {
            id = 999,
            rowVersion = cliente.RowVersion
        };

        var request = new HttpRequestMessage(HttpMethod.Delete, $"/api/v1/cliente/{cliente.id}")
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

        var request = new HttpRequestMessage(HttpMethod.Delete, "/api/v1/cliente/9999")
        {
            Content = JsonContent.Create(dto)
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", AdminToken);

        var response = await _client.SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Delete_OnlyRemovesTargetCliente()
    {
        var c1 = await CreateClienteAsync("deleteonlya");
        var c2 = await CreateClienteAsync("deleteonlyb");

        var dto = new
        {
            id = c1.id,
            rowVersion = c1.RowVersion
        };

        var request = new HttpRequestMessage(HttpMethod.Delete, $"/api/v1/cliente/{c1.id}")
        {
            Content = JsonContent.Create(dto)
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", AdminToken);

        await _client.SendAsync(request);

        var getDeletedRequest = new HttpRequestMessage(HttpMethod.Get, $"/api/v1/cliente/{c1.id}");
        getDeletedRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", AdminToken);
        var getDeletedResponse = await _client.SendAsync(getDeletedRequest);
        getDeletedResponse.StatusCode.Should().Be(HttpStatusCode.NotFound);

        var getRemainingRequest = new HttpRequestMessage(HttpMethod.Get, $"/api/v1/cliente/{c2.id}");
        getRemainingRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", AdminToken);
        var getRemainingResponse = await _client.SendAsync(getRemainingRequest);
        getRemainingResponse.StatusCode.Should().Be(HttpStatusCode.OK);

        var remainingCliente = await getRemainingResponse.Content.ReadFromJsonAsync<CliClienteDto>();
        remainingCliente!.id.Should().Be(c2.id);
        remainingCliente.strNombreCliente.Should().Be("deleteonlyb");
    }

    [Fact]
    public async Task Delete_StaleRowVersion_Returns409()
    {
        var cliente = await CreateClienteAsync("deletestalerv");

        var dto = new
        {
            id = cliente.id,
            rowVersion = new byte[] { 2, 0, 0, 0 }
        };

        var request = new HttpRequestMessage(HttpMethod.Delete, $"/api/v1/cliente/{cliente.id}")
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
        var nombre = "lifecyclecliente";
        var dto = TestDataFactory.CreateClienteCreateDto(
            nombre: nombre,
            correo: $"{nombre}@test.com",
            telefono: "5512345678");

        var createRequest = new HttpRequestMessage(HttpMethod.Post, "/api/v1/cliente")
        {
            Content = JsonContent.Create(dto)
        };
        createRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", AdminToken);

        var createResponse = await _client.SendAsync(createRequest);
        createResponse.StatusCode.Should().Be(HttpStatusCode.Created);

        var createdCliente = await createResponse.Content.ReadFromJsonAsync<CliClienteDto>();

        var getAfterCreate = await GetClienteAsync(createdCliente!.id);
        getAfterCreate.strNombreCliente.Should().Be(nombre);
        getAfterCreate.strCorreoElectronico.Should().Be($"{nombre}@test.com");

        var updateDto = new
        {
            id = createdCliente.id,
            strNombreCliente = "lifecycleclienteupdated",
            strCorreoElectronico = "lifecycleclienteupdated@test.com",
            strNumeroTelefono = "5599999999",
            rowVersion = getAfterCreate.RowVersion
        };

        var updateRequest = new HttpRequestMessage(HttpMethod.Put, $"/api/v1/cliente/{createdCliente.id}")
        {
            Content = JsonContent.Create(updateDto)
        };
        updateRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", AdminToken);

        var updateResponse = await _client.SendAsync(updateRequest);
        updateResponse.StatusCode.Should().Be(HttpStatusCode.NoContent);

        var getAfterUpdate = await GetClienteAsync(createdCliente.id);
        getAfterUpdate.strNombreCliente.Should().Be("lifecycleclienteupdated");
        getAfterUpdate.strCorreoElectronico.Should().Be("lifecycleclienteupdated@test.com");
        getAfterUpdate.strNumeroTelefono.Should().Be("5599999999");

        var deleteDto = new
        {
            id = createdCliente.id,
            rowVersion = getAfterUpdate.RowVersion
        };

        var deleteRequest = new HttpRequestMessage(HttpMethod.Delete, $"/api/v1/cliente/{createdCliente.id}")
        {
            Content = JsonContent.Create(deleteDto)
        };
        deleteRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", AdminToken);

        var deleteResponse = await _client.SendAsync(deleteRequest);
        deleteResponse.StatusCode.Should().Be(HttpStatusCode.OK);

        var getAfterDelete = new HttpRequestMessage(HttpMethod.Get, $"/api/v1/cliente/{createdCliente.id}");
        getAfterDelete.Headers.Authorization = new AuthenticationHeaderValue("Bearer", AdminToken);

        var finalResponse = await _client.SendAsync(getAfterDelete);
        finalResponse.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }
}
