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

namespace IntegrationTest.Venta;

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
            builder.UseSetting("InMemoryDatabaseName", $"VentaTestDb_{Guid.NewGuid():N}");
        });
        _client = _factory.CreateClient();
    }

    public async Task InitializeAsync()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        db.VenVenta.RemoveRange(db.VenVenta);
        await db.SaveChangesAsync();
    }

    public Task DisposeAsync() => Task.CompletedTask;

    private string AdminToken => JwtTestConfig.AdminToken;

    private async Task<(int clienteId, int usuarioId)> SeedDependenciesAsync()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var cliente = new CliCliente { strNombreCliente = $"intcliente{Guid.NewGuid():N}"[..30], strCorreoElectronico = $"intcli{Guid.NewGuid():N}@test.com", strNumeroTelefono = "5512345678", RowVersion = new byte[] { 1, 0, 0, 0 } };
        db.CliCliente.Add(cliente);

        var usuario = new SegUsuario { strNombre = $"intuser{Guid.NewGuid():N}"[..20], strPWD = "hash", strCorreoElectronico = $"intusr{Guid.NewGuid():N}@test.com", RowVersion = new byte[] { 1, 0, 0, 0 } };
        db.SegUsuario.Add(usuario);

        if (!db.VenCatEstado.Any())
        {
            db.VenCatEstado.AddRange(
                new VenCatEstado { id = 1, strValor = "En compra", strDescripcion = "Compra en proceso" },
                new VenCatEstado { id = 2, strValor = "Pagado", strDescripcion = "Compra pagada" },
                new VenCatEstado { id = 3, strValor = "Cancelado", strDescripcion = "Compra cancelada" }
            );
        }

        await db.SaveChangesAsync();
        return (cliente.id, usuario.id);
    }

    private async Task<VenVentaDto> CreateVentaAsync(int? clienteId = null, int? usuarioId = null)
    {
        if (clienteId == null || usuarioId == null)
        {
            var (cid, uid) = await SeedDependenciesAsync();
            clienteId = cid;
            usuarioId = uid;
        }

        var dto = new { idCliCliente = clienteId.Value, idSegUsuario = usuarioId.Value };

        var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/venta")
        {
            Content = JsonContent.Create(dto)
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", AdminToken);

        var response = await _client.SendAsync(request);
        response.StatusCode.Should().Be(HttpStatusCode.Created);

        var createdVenta = await response.Content.ReadFromJsonAsync<VenVentaDto>();
        createdVenta.Should().NotBeNull();

        var getRequest = new HttpRequestMessage(HttpMethod.Get, $"/api/v1/venta/{createdVenta!.id}");
        getRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", AdminToken);
        var getResponse = await _client.SendAsync(getRequest);
        getResponse.EnsureSuccessStatusCode();
        var fullVenta = await getResponse.Content.ReadFromJsonAsync<VenVentaDto>();
        fullVenta.Should().NotBeNull();

        return fullVenta!;
    }

    private async Task<VenVentaDto> GetVentaAsync(int id)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, $"/api/v1/venta/{id}");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", AdminToken);
        var response = await _client.SendAsync(request);
        response.EnsureSuccessStatusCode();
        var venta = await response.Content.ReadFromJsonAsync<VenVentaDto>();
        venta.Should().NotBeNull();
        return venta!;
    }

    [Fact]
    public async Task GetAll_EmptyDatabase_ReturnsEmptyPagedResult()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "/api/v1/venta");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", AdminToken);

        var response = await _client.SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var result = await response.Content.ReadFromJsonAsync<PagedResult<VenVentaDto>>();
        result.Should().NotBeNull();
        result!.Items.Should().BeEmpty();
        result.TotalCount.Should().Be(0);
        result.PageNumber.Should().Be(1);
        result.PageSize.Should().Be(20);
        result.TotalPages.Should().Be(0);
    }

    [Fact]
    public async Task GetAll_WithVentas_ReturnsDefaultPagination()
    {
        await CreateVentaAsync();
        await CreateVentaAsync();

        var request = new HttpRequestMessage(HttpMethod.Get, "/api/v1/venta");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", AdminToken);

        var response = await _client.SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var result = await response.Content.ReadFromJsonAsync<PagedResult<VenVentaDto>>();
        result!.Items.Should().HaveCount(2);
        result.TotalCount.Should().Be(2);
        result.PageNumber.Should().Be(1);
        result.PageSize.Should().Be(20);
        result.TotalPages.Should().Be(1);
    }

    [Fact]
    public async Task GetAll_WithCustomPageSize_ReturnsCorrectSize()
    {
        var (clienteId, usuarioId) = await SeedDependenciesAsync();
        for (int i = 0; i < 15; i++)
            await CreateVentaAsync(clienteId, usuarioId);

        var request = new HttpRequestMessage(HttpMethod.Get, "/api/v1/venta?pageSize=5");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", AdminToken);

        var response = await _client.SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var result = await response.Content.ReadFromJsonAsync<PagedResult<VenVentaDto>>();
        result!.Items.Should().HaveCount(5);
        result.TotalCount.Should().Be(15);
        result.PageNumber.Should().Be(1);
        result.PageSize.Should().Be(5);
        result.TotalPages.Should().Be(3);
    }

    [Fact]
    public async Task GetAll_WithCustomPageNumber_ReturnsCorrectPage()
    {
        var (clienteId, usuarioId) = await SeedDependenciesAsync();
        for (int i = 0; i < 25; i++)
            await CreateVentaAsync(clienteId, usuarioId);

        var request = new HttpRequestMessage(HttpMethod.Get, "/api/v1/venta?pageNumber=2&pageSize=10");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", AdminToken);

        var response = await _client.SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var result = await response.Content.ReadFromJsonAsync<PagedResult<VenVentaDto>>();
        result!.Items.Should().HaveCount(10);
        result.TotalCount.Should().Be(25);
        result.PageNumber.Should().Be(2);
        result.PageSize.Should().Be(10);
        result.TotalPages.Should().Be(3);
    }

    [Fact]
    public async Task GetAll_OnLastPage_ReturnsRemainingItems()
    {
        var (clienteId, usuarioId) = await SeedDependenciesAsync();
        for (int i = 0; i < 25; i++)
            await CreateVentaAsync(clienteId, usuarioId);

        var request = new HttpRequestMessage(HttpMethod.Get, "/api/v1/venta?pageNumber=3&pageSize=10");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", AdminToken);

        var response = await _client.SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var result = await response.Content.ReadFromJsonAsync<PagedResult<VenVentaDto>>();
        result!.Items.Should().HaveCount(5);
        result.TotalCount.Should().Be(25);
        result.PageNumber.Should().Be(3);
        result.PageSize.Should().Be(10);
        result.TotalPages.Should().Be(3);
    }

    [Fact]
    public async Task GetAll_HasCacheControlHeader()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "/api/v1/venta");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", AdminToken);

        var response = await _client.SendAsync(request);

        response.Headers.CacheControl.Should().NotBeNull();
        response.Headers.CacheControl!.NoStore.Should().BeTrue();
    }

    [Fact]
    public async Task GetById_ExistingVenta_ReturnsVenta()
    {
        var venta = await CreateVentaAsync();

        var result = await GetVentaAsync(venta.id);

        result.id.Should().Be(venta.id);
        result.strClaveVenta.Should().Be(venta.strClaveVenta);
        result.idVenCatEstado.Should().Be(1);
        result.dteFechaHoraCompra.Should().NotBeNull();
    }

    [Fact]
    public async Task GetById_NonExistentId_ReturnsNotFound()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "/api/v1/venta/9999");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", AdminToken);

        var response = await _client.SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task GetById_NegativeId_ReturnsNotFound()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "/api/v1/venta/-1");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", AdminToken);

        var response = await _client.SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task GetById_ZeroId_ReturnsNotFound()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "/api/v1/venta/0");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", AdminToken);

        var response = await _client.SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task GetById_MultipleVentas_ReturnsCorrectVenta()
    {
        var v1 = await CreateVentaAsync();
        var v2 = await CreateVentaAsync();
        var v3 = await CreateVentaAsync();

        var result = await GetVentaAsync(v2.id);

        result.id.Should().Be(v2.id);
        result.strClaveVenta.Should().Be(v2.strClaveVenta);
    }

    [Fact]
    public async Task Create_ValidDto_Returns201WithLocationHeader()
    {
        var (clienteId, usuarioId) = await SeedDependenciesAsync();

        var dto = new { idCliCliente = clienteId, idSegUsuario = usuarioId };

        var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/venta")
        {
            Content = JsonContent.Create(dto)
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", AdminToken);

        var response = await _client.SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.Created);
        response.Headers.Location.Should().NotBeNull();
        response.Headers.Location!.ToString().Should().Contain("/api/v1/Venta/");
    }

    [Fact]
    public async Task Create_ValidDto_ReturnsDtoWithId()
    {
        var (clienteId, usuarioId) = await SeedDependenciesAsync();

        var dto = new { idCliCliente = clienteId, idSegUsuario = usuarioId };

        var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/venta")
        {
            Content = JsonContent.Create(dto)
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", AdminToken);

        var response = await _client.SendAsync(request);

        var createdVenta = await response.Content.ReadFromJsonAsync<VenVentaDto>();
        createdVenta.Should().NotBeNull();
        createdVenta!.id.Should().BeGreaterThan(0);
    }

    [Fact]
    public async Task Create_ValidDto_ReturnsCorrectData()
    {
        var (clienteId, usuarioId) = await SeedDependenciesAsync();

        var dto = new { idCliCliente = clienteId, idSegUsuario = usuarioId };

        var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/venta")
        {
            Content = JsonContent.Create(dto)
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", AdminToken);

        var response = await _client.SendAsync(request);

        var createdVenta = await response.Content.ReadFromJsonAsync<VenVentaDto>();
        createdVenta!.idCliCliente.Should().Be(clienteId);
        createdVenta.idSegUsuario.Should().Be(usuarioId);
        createdVenta.idVenCatEstado.Should().Be(1);
        createdVenta.dteFechaHoraCompra.Should().NotBeNull();
        createdVenta.strClaveVenta.Should().NotBeNullOrEmpty();
        createdVenta.strClaveVenta.Length.Should().Be(10);
    }

    [Fact]
    public async Task Create_ValidDto_CreatesVentaInDatabase()
    {
        var (clienteId, usuarioId) = await SeedDependenciesAsync();

        var dto = new { idCliCliente = clienteId, idSegUsuario = usuarioId };

        var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/venta")
        {
            Content = JsonContent.Create(dto)
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", AdminToken);

        var response = await _client.SendAsync(request);

        var createdVenta = await response.Content.ReadFromJsonAsync<VenVentaDto>();

        var getRequest = new HttpRequestMessage(HttpMethod.Get, $"/api/v1/venta/{createdVenta!.id}");
        getRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", AdminToken);
        var getResponse = await _client.SendAsync(getRequest);

        getResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var fetchedVenta = await getResponse.Content.ReadFromJsonAsync<VenVentaDto>();
        fetchedVenta!.id.Should().Be(createdVenta.id);
    }

    [Fact]
    public async Task Create_NonExistentCliente_Returns400()
    {
        var dto = new { idCliCliente = 9999, idSegUsuario = 1 };

        var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/venta")
        {
            Content = JsonContent.Create(dto)
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", AdminToken);

        var response = await _client.SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Create_NonExistentUsuario_Returns400()
    {
        var (clienteId, _) = await SeedDependenciesAsync();

        var dto = new { idCliCliente = clienteId, idSegUsuario = 9999 };

        var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/venta")
        {
            Content = JsonContent.Create(dto)
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", AdminToken);

        var response = await _client.SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Search_ByClaveVenta_ReturnsMatching()
    {
        var v1 = await CreateVentaAsync();
        await CreateVentaAsync();

        var request = new HttpRequestMessage(HttpMethod.Get, $"/api/v1/venta/buscar?strClaveVenta={v1.strClaveVenta[..5]}");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", AdminToken);

        var response = await _client.SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var result = await response.Content.ReadFromJsonAsync<PagedResult<VenVentaDto>>();
        result!.Items.Should().Contain(i => i.id == v1.id);
    }

    [Fact]
    public async Task Search_WithPagination_ReturnsCorrectPage()
    {
        var (clienteId, usuarioId) = await SeedDependenciesAsync();
        for (int i = 0; i < 5; i++)
            await CreateVentaAsync(clienteId, usuarioId);

        var request = new HttpRequestMessage(HttpMethod.Get, "/api/v1/venta/buscar?pageNumber=2&pageSize=2");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", AdminToken);

        var response = await _client.SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var result = await response.Content.ReadFromJsonAsync<PagedResult<VenVentaDto>>();
        result!.Items.Should().HaveCount(2);
        result.PageNumber.Should().Be(2);
        result.PageSize.Should().Be(2);
    }

    // ==================== UPDATE ====================

    [Fact]
    public async Task Update_ValidUpdate_Returns204()
    {
        var venta = await CreateVentaAsync();

                var updateDto = TestDataFactory.CreateVentaUpdateDto(
                    venta.id, venta.idCliCliente, venta.idSegUsuario, idVenCatEstado: 3);

                var request = new HttpRequestMessage(HttpMethod.Put, $"/api/v1/venta/{venta.id}")
                {
                    Content = JsonContent.Create(updateDto)
                };
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", AdminToken);

                var response = await _client.SendAsync(request);

                response.StatusCode.Should().Be(HttpStatusCode.NoContent);
            }

    [Fact]
    public async Task Update_ValidUpdate_ChangesEstado()
    {
        var venta = await CreateVentaAsync();

        var updateDto = TestDataFactory.CreateVentaUpdateDto(
            venta.id, venta.idCliCliente, venta.idSegUsuario, idVenCatEstado: 3);

        var request = new HttpRequestMessage(HttpMethod.Put, $"/api/v1/venta/{venta.id}")
        {
            Content = JsonContent.Create(updateDto)
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", AdminToken);

        await _client.SendAsync(request);

        var updated = await GetVentaAsync(venta.id);
        updated.idVenCatEstado.Should().Be(3);
    }

    [Fact]
    public async Task Update_NonExistentId_Returns404()
    {
        var venta = await CreateVentaAsync();

        var updateDto = TestDataFactory.CreateVentaUpdateDto(
            9999, venta.idCliCliente, venta.idSegUsuario, idVenCatEstado: 2);

        var request = new HttpRequestMessage(HttpMethod.Put, "/api/v1/venta/9999")
        {
            Content = JsonContent.Create(updateDto)
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", AdminToken);

        var response = await _client.SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Update_StaleRowVersion_Returns409()
    {
        var venta = await CreateVentaAsync();

        var updateDto = TestDataFactory.CreateVentaUpdateDto(
            venta.id, venta.idCliCliente, venta.idSegUsuario, idVenCatEstado: 2, rowVersion: new byte[] { 2, 0, 0, 0 });

        var request = new HttpRequestMessage(HttpMethod.Put, $"/api/v1/venta/{venta.id}")
        {
            Content = JsonContent.Create(updateDto)
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", AdminToken);

        var response = await _client.SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task Update_NonExistentCliente_Returns400()
    {
        var (clienteId, usuarioId) = await SeedDependenciesAsync();
        var venta = await CreateVentaAsync(clienteId, usuarioId);

        var updateDto = TestDataFactory.CreateVentaUpdateDto(
            venta.id, idCliCliente: 9999, idSegUsuario: usuarioId, idVenCatEstado: 2);

        var request = new HttpRequestMessage(HttpMethod.Put, $"/api/v1/venta/{venta.id}")
        {
            Content = JsonContent.Create(updateDto)
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", AdminToken);

        var response = await _client.SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Update_NonExistentUsuario_Returns400()
    {
        var (clienteId, usuarioId) = await SeedDependenciesAsync();
        var venta = await CreateVentaAsync(clienteId, usuarioId);

        var updateDto = TestDataFactory.CreateVentaUpdateDto(
            venta.id, clienteId, idSegUsuario: 9999, idVenCatEstado: 2);

        var request = new HttpRequestMessage(HttpMethod.Put, $"/api/v1/venta/{venta.id}")
        {
            Content = JsonContent.Create(updateDto)
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", AdminToken);

        var response = await _client.SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Update_NonExistentEstado_Returns400()
    {
        var venta = await CreateVentaAsync();

        var updateDto = TestDataFactory.CreateVentaUpdateDto(
            venta.id, venta.idCliCliente, venta.idSegUsuario, idVenCatEstado: 9999);

        var request = new HttpRequestMessage(HttpMethod.Put, $"/api/v1/venta/{venta.id}")
        {
            Content = JsonContent.Create(updateDto)
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", AdminToken);

        var response = await _client.SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    // ==================== DELETE ====================

    [Fact]
    public async Task Delete_ValidDelete_Returns200()
    {
        var venta = await CreateVentaAsync();

        var deleteDto = TestDataFactory.CreateVentaDeleteDto(venta.id);

        var request = new HttpRequestMessage(HttpMethod.Delete, $"/api/v1/venta/{venta.id}")
        {
            Content = JsonContent.Create(deleteDto)
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", AdminToken);

        var response = await _client.SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Delete_ValidDelete_RemovesVentaFromDatabase()
    {
        var venta = await CreateVentaAsync();

        var deleteDto = TestDataFactory.CreateVentaDeleteDto(venta.id);

        var request = new HttpRequestMessage(HttpMethod.Delete, $"/api/v1/venta/{venta.id}")
        {
            Content = JsonContent.Create(deleteDto)
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", AdminToken);

        await _client.SendAsync(request);

        var getRequest = new HttpRequestMessage(HttpMethod.Get, $"/api/v1/venta/{venta.id}");
        getRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", AdminToken);
        var getResponse = await _client.SendAsync(getRequest);

        getResponse.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Delete_NonExistentId_Returns404()
    {
        var deleteDto = TestDataFactory.CreateVentaDeleteDto(9999);

        var request = new HttpRequestMessage(HttpMethod.Delete, "/api/v1/venta/9999")
        {
            Content = JsonContent.Create(deleteDto)
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", AdminToken);

        var response = await _client.SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Delete_StaleRowVersion_Returns409()
    {
        var venta = await CreateVentaAsync();

        var deleteDto = TestDataFactory.CreateVentaDeleteDto(venta.id, rowVersion: new byte[] { 2, 0, 0, 0 });

        var request = new HttpRequestMessage(HttpMethod.Delete, $"/api/v1/venta/{venta.id}")
        {
            Content = JsonContent.Create(deleteDto)
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", AdminToken);

        var response = await _client.SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task Delete_OnlyRemovesTargetVenta()
    {
        var v1 = await CreateVentaAsync();
        var v2 = await CreateVentaAsync();

        var deleteDto = TestDataFactory.CreateVentaDeleteDto(v1.id);

        var request = new HttpRequestMessage(HttpMethod.Delete, $"/api/v1/venta/{v1.id}")
        {
            Content = JsonContent.Create(deleteDto)
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", AdminToken);

        await _client.SendAsync(request);

        var getV2Request = new HttpRequestMessage(HttpMethod.Get, $"/api/v1/venta/{v2.id}");
        getV2Request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", AdminToken);
        var getV2Response = await _client.SendAsync(getV2Request);

        getV2Response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    // ==================== FULL LIFECYCLE ====================

    [Fact]
    public async Task FullLifecycle_CreateGetUpdateGetDelete_CompleteFlow()
    {
        var (clienteId, usuarioId) = await SeedDependenciesAsync();

        var createDto = new { idCliCliente = clienteId, idSegUsuario = usuarioId };
        var createRequest = new HttpRequestMessage(HttpMethod.Post, "/api/v1/venta")
        {
            Content = JsonContent.Create(createDto)
        };
        createRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", AdminToken);
        var createResponse = await _client.SendAsync(createRequest);
        createResponse.StatusCode.Should().Be(HttpStatusCode.Created);

        var created = await createResponse.Content.ReadFromJsonAsync<VenVentaDto>();
        created.Should().NotBeNull();

        var getRequest = new HttpRequestMessage(HttpMethod.Get, $"/api/v1/venta/{created!.id}");
        getRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", AdminToken);
        var getResponse = await _client.SendAsync(getRequest);
        getResponse.StatusCode.Should().Be(HttpStatusCode.OK);

        var updateDto = TestDataFactory.CreateVentaUpdateDto(
            created.id, created.idCliCliente, created.idSegUsuario, idVenCatEstado: 3);

        var updateRequest = new HttpRequestMessage(HttpMethod.Put, $"/api/v1/venta/{created.id}")
        {
            Content = JsonContent.Create(updateDto)
        };
        updateRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", AdminToken);
        var updateResponse = await _client.SendAsync(updateRequest);
        updateResponse.StatusCode.Should().Be(HttpStatusCode.NoContent);

        var getUpdatedRequest = new HttpRequestMessage(HttpMethod.Get, $"/api/v1/venta/{created.id}");
        getUpdatedRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", AdminToken);
        var getUpdatedResponse = await _client.SendAsync(getUpdatedRequest);
        var updated = await getUpdatedResponse.Content.ReadFromJsonAsync<VenVentaDto>();
        updated!.idVenCatEstado.Should().Be(3);

        var deleteDto = TestDataFactory.CreateVentaDeleteDto(created.id);

        var deleteRequest = new HttpRequestMessage(HttpMethod.Delete, $"/api/v1/venta/{created.id}")
        {
            Content = JsonContent.Create(deleteDto)
        };
        deleteRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", AdminToken);
        var deleteResponse = await _client.SendAsync(deleteRequest);
        deleteResponse.StatusCode.Should().Be(HttpStatusCode.OK);

        var getAfterDeleteRequest = new HttpRequestMessage(HttpMethod.Get, $"/api/v1/venta/{created.id}");
        getAfterDeleteRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", AdminToken);
        var getAfterDeleteResponse = await _client.SendAsync(getAfterDeleteRequest);
        getAfterDeleteResponse.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }
}
