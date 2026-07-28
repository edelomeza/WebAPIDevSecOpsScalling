using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Threading;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using UnitTest.Common;
using WebAPIDevSecOps.Context;
using WebAPIDevSecOps.Dto;
using WebAPIDevSecOps.Models;
using WebAPIDevSecOps.Services;

namespace IntegrationTest.Empleado;

public class IntegrationTests : IClassFixture<WebApplicationFactory<Program>>, IAsyncLifetime
{
    private static int _curpCounter;
    private static readonly string[] ValidCurps =
    [
        "LOHA850315HDFBCR01", "CEMM880620MDFNBT02", "GUMR900101HNLVCH03",
        "BACX001215HTCFBR04", "ROHI850315HDFBCR05", "DILA950220HDFNPR06",
        "MAVR750810MNLBCH07", "PESC020301HTSRRN08", "HOGU880405MDFNBR09",
        "SARO990710HSLBCH10"
    ];
    private static string NextCurp() => ValidCurps[Interlocked.Increment(ref _curpCounter) % ValidCurps.Length];
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
            builder.UseSetting("InMemoryDatabaseName", $"EmpleadoTestDb_{Guid.NewGuid():N}");
        });
        _client = _factory.CreateClient();
    }

    public async Task InitializeAsync()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        db.EmpEmpleado.RemoveRange(db.EmpEmpleado);
        await db.SaveChangesAsync();
    }

    public Task DisposeAsync() => Task.CompletedTask;

    private string AdminToken => JwtTestConfig.AdminToken;

    private async Task<EmpEmpleadoDto> CreateEmpleadoAsync(string? uniqueName = null)
    {
        var nombre = uniqueName ?? $"int_emp_{Guid.NewGuid():N}"[..30];
        var dto = new EmpEmpleadoCreateDto
        {
            strNombre = nombre,
            strAPaterno = "Apellido",
            strCURP = NextCurp()
        };

        var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/empleado")
        {
            Content = JsonContent.Create(dto)
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", AdminToken);

        var response = await _client.SendAsync(request);
        response.StatusCode.Should().Be(HttpStatusCode.Created);

        var created = await response.Content.ReadFromJsonAsync<EmpEmpleadoDto>();
        created.Should().NotBeNull();

        var getRequest = new HttpRequestMessage(HttpMethod.Get, $"/api/v1/empleado/{created!.id}");
        getRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", AdminToken);
        var getResponse = await _client.SendAsync(getRequest);
        getResponse.EnsureSuccessStatusCode();
        var full = await getResponse.Content.ReadFromJsonAsync<EmpEmpleadoDto>();
        return full!;
    }

    // ==================== GET ALL ====================

    [Fact]
    public async Task GetAll_EmptyDatabase_ReturnsEmptyPagedResult()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "/api/v1/empleado");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", AdminToken);

        var response = await _client.SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var result = await response.Content.ReadFromJsonAsync<PagedResult<EmpEmpleadoDto>>();
        result!.Items.Should().BeEmpty();
        result.TotalCount.Should().Be(0);
        result.PageNumber.Should().Be(1);
        result.PageSize.Should().Be(20);
    }

    [Fact]
    public async Task GetAll_WithUsers_ReturnsDefaultPagination()
    {
        await CreateEmpleadoAsync("getall_a");
        await CreateEmpleadoAsync("getall_b");

        var request = new HttpRequestMessage(HttpMethod.Get, "/api/v1/empleado");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", AdminToken);

        var response = await _client.SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var result = await response.Content.ReadFromJsonAsync<PagedResult<EmpEmpleadoDto>>();
        result!.Items.Should().HaveCount(2);
        result.TotalCount.Should().Be(2);
    }

    [Fact]
    public async Task GetAll_WithCustomPagination_ReturnsCorrectSize()
    {
        for (int i = 0; i < 5; i++)
            await CreateEmpleadoAsync($"getall_page_{i}");

        var request = new HttpRequestMessage(HttpMethod.Get, "/api/v1/empleado?pageSize=2&pageNumber=2");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", AdminToken);

        var response = await _client.SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var result = await response.Content.ReadFromJsonAsync<PagedResult<EmpEmpleadoDto>>();
        result!.Items.Should().HaveCount(2);
        result.PageNumber.Should().Be(2);
        result.PageSize.Should().Be(2);
    }

    // ==================== GET BY ID ====================

    [Fact]
    public async Task GetById_ExistingUser_ReturnsUser()
    {
        var emp = await CreateEmpleadoAsync("getbyid_exists");

        var request = new HttpRequestMessage(HttpMethod.Get, $"/api/v1/empleado/{emp.id}");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", AdminToken);

        var response = await _client.SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var dto = await response.Content.ReadFromJsonAsync<EmpEmpleadoDto>();
        dto!.id.Should().Be(emp.id);
        dto.strNombre.Should().Be(emp.strNombre);
    }

    [Fact]
    public async Task GetById_NonExistentId_ReturnsNotFound()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "/api/v1/empleado/9999");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", AdminToken);

        var response = await _client.SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    // ==================== SEARCH ====================

    [Fact]
    public async Task Search_ByText_ReturnsMatches()
    {
        await CreateEmpleadoAsync("search_juan");
        await CreateEmpleadoAsync("search_maria");

        var request = new HttpRequestMessage(HttpMethod.Get, "/api/v1/empleado/buscar?texto=juan");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", AdminToken);

        var response = await _client.SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var result = await response.Content.ReadFromJsonAsync<PagedResult<EmpEmpleadoDto>>();
        result!.Items.Should().Contain(e => e.strNombre.Contains("juan"));
    }

    [Fact]
    public async Task Search_WithPagination_Works()
    {
        for (int i = 0; i < 10; i++)
            await CreateEmpleadoAsync($"search_pag_{i}");

        var request = new HttpRequestMessage(HttpMethod.Get, "/api/v1/empleado/buscar?texto=search_pag&pageSize=3");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", AdminToken);

        var response = await _client.SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var result = await response.Content.ReadFromJsonAsync<PagedResult<EmpEmpleadoDto>>();
        result!.Items.Should().HaveCount(3);
        result.TotalCount.Should().Be(10);
    }

    // ==================== CREATE ====================

    [Fact]
    public async Task Create_ValidDto_Returns201WithLocation()
    {
        var dto = new EmpEmpleadoCreateDto
        {
            strNombre = "create_location",
            strAPaterno = "Test",
            strCURP = "LOHA850315HDFBCR01"
        };

        var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/empleado")
        {
            Content = JsonContent.Create(dto)
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", AdminToken);

        var response = await _client.SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.Created);
        response.Headers.Location.Should().NotBeNull();
        response.Headers.Location!.ToString().Should().Contain("/api/v1/Empleado/");
    }

    [Fact]
    public async Task Create_ValidDto_ReturnsDtoWithId()
    {
        var dto = new EmpEmpleadoCreateDto
        {
            strNombre = "create_dto",
            strAPaterno = "Test",
            strCURP = "CEMM880620MDFNBT02"
        };

        var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/empleado")
        {
            Content = JsonContent.Create(dto)
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", AdminToken);

        var response = await _client.SendAsync(request);

        var created = await response.Content.ReadFromJsonAsync<EmpEmpleadoDto>();
        created!.id.Should().BeGreaterThan(0);
        created.strNombre.Should().Be("create_dto");
    }

    [Fact]
    public async Task Create_DuplicateCURP_Returns400()
    {
        var emp = await CreateEmpleadoAsync("create_dup");

        var dto = new EmpEmpleadoCreateDto
        {
            strNombre = "otro",
            strCURP = emp.strCURP
        };

        var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/empleado")
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
        var dto = new EmpEmpleadoCreateDto
        {
            strNombre = "",
            strCURP = "BACX001215HTCFBR04"
        };

        var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/empleado")
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
        var dto = new EmpEmpleadoCreateDto
        {
            strNombre = new string('a', 51),
            strCURP = "ROHI850315HDFBCR05"
        };

        var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/empleado")
        {
            Content = JsonContent.Create(dto)
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", AdminToken);

        var response = await _client.SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    // ==================== UPDATE ====================

    [Fact]
    public async Task Update_ValidUpdate_Returns204()
    {
        var emp = await CreateEmpleadoAsync("update_valid");

        var dto = new
        {
            id = emp.id,
            strNombre = "update_valid_mod",
            strAPaterno = emp.strAPaterno,
            strCURP = emp.strCURP,
            idEmpCatTipoEmpleado = (int?)null,
            rowVersion = emp.RowVersion
        };

        var request = new HttpRequestMessage(HttpMethod.Put, $"/api/v1/empleado/{emp.id}")
        {
            Content = JsonContent.Create(dto)
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", AdminToken);

        var response = await _client.SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.NoContent);
    }

    [Fact]
    public async Task Update_RouteIdMismatch_Returns400()
    {
        var emp = await CreateEmpleadoAsync("update_mismatch");

        var dto = new
        {
            id = 999,
            strNombre = "should_not_work",
            strAPaterno = emp.strAPaterno,
            rowVersion = emp.RowVersion
        };

        var request = new HttpRequestMessage(HttpMethod.Put, $"/api/v1/empleado/{emp.id}")
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
            strNombre = "nonexistent",
            rowVersion = new byte[] { 1, 0, 0, 0 }
        };

        var request = new HttpRequestMessage(HttpMethod.Put, "/api/v1/empleado/9999")
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
        var emp = await CreateEmpleadoAsync("update_stale");

        var dto = new
        {
            id = emp.id,
            strNombre = "update_stale_mod",
            strAPaterno = emp.strAPaterno,
            rowVersion = new byte[] { 2, 0, 0, 0 }
        };

        var request = new HttpRequestMessage(HttpMethod.Put, $"/api/v1/empleado/{emp.id}")
        {
            Content = JsonContent.Create(dto)
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", AdminToken);

        var response = await _client.SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
    }

    // ==================== DELETE ====================

    [Fact]
    public async Task Delete_ValidDelete_Returns200()
    {
        var emp = await CreateEmpleadoAsync("delete_valid");

        var dto = new
        {
            id = emp.id,
            rowVersion = emp.RowVersion
        };

        var request = new HttpRequestMessage(HttpMethod.Delete, $"/api/v1/empleado/{emp.id}")
        {
            Content = JsonContent.Create(dto)
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", AdminToken);

        var response = await _client.SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Delete_RemovesFromDatabase()
    {
        var emp = await CreateEmpleadoAsync("delete_remove");

        var dto = new
        {
            id = emp.id,
            rowVersion = emp.RowVersion
        };

        var request = new HttpRequestMessage(HttpMethod.Delete, $"/api/v1/empleado/{emp.id}")
        {
            Content = JsonContent.Create(dto)
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", AdminToken);

        await _client.SendAsync(request);

        var getRequest = new HttpRequestMessage(HttpMethod.Get, $"/api/v1/empleado/{emp.id}");
        getRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", AdminToken);
        var getResponse = await _client.SendAsync(getRequest);

        getResponse.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Delete_RouteIdMismatch_Returns400()
    {
        var emp = await CreateEmpleadoAsync("delete_mismatch");

        var dto = new
        {
            id = 999,
            rowVersion = emp.RowVersion
        };

        var request = new HttpRequestMessage(HttpMethod.Delete, $"/api/v1/empleado/{emp.id}")
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

        var request = new HttpRequestMessage(HttpMethod.Delete, "/api/v1/empleado/9999")
        {
            Content = JsonContent.Create(dto)
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", AdminToken);

        var response = await _client.SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Delete_StaleRowVersion_Returns409()
    {
        var emp = await CreateEmpleadoAsync("delete_stale");

        var dto = new
        {
            id = emp.id,
            rowVersion = new byte[] { 2, 0, 0, 0 }
        };

        var request = new HttpRequestMessage(HttpMethod.Delete, $"/api/v1/empleado/{emp.id}")
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
        var dto = new EmpEmpleadoCreateDto
        {
            strNombre = "lifecycle_user",
            strAPaterno = "Life",
            strCURP = "GUMR900101HNLVCH03"
        };

        var createRequest = new HttpRequestMessage(HttpMethod.Post, "/api/v1/empleado")
        {
            Content = JsonContent.Create(dto)
        };
        createRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", AdminToken);

        var createResponse = await _client.SendAsync(createRequest);
        createResponse.StatusCode.Should().Be(HttpStatusCode.Created);

        var created = await createResponse.Content.ReadFromJsonAsync<EmpEmpleadoDto>();

        var getRequest = new HttpRequestMessage(HttpMethod.Get, $"/api/v1/empleado/{created!.id}");
        getRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", AdminToken);
        var getResponse = await _client.SendAsync(getRequest);
        getResponse.EnsureSuccessStatusCode();
        var fetched = await getResponse.Content.ReadFromJsonAsync<EmpEmpleadoDto>();
        fetched!.strNombre.Should().Be("lifecycle_user");

        var updateDto = new
        {
            id = created.id,
            strNombre = "lifecycle_updated",
            strAPaterno = "LifeMod",
            strCURP = fetched.strCURP,
            rowVersion = fetched.RowVersion
        };

        var updateRequest = new HttpRequestMessage(HttpMethod.Put, $"/api/v1/empleado/{created.id}")
        {
            Content = JsonContent.Create(updateDto)
        };
        updateRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", AdminToken);

        var updateResponse = await _client.SendAsync(updateRequest);
        updateResponse.StatusCode.Should().Be(HttpStatusCode.NoContent);

        var getAfterUpdate = await _client.SendAsync(
            new HttpRequestMessage(HttpMethod.Get, $"/api/v1/empleado/{created.id}")
            {
                Headers = { Authorization = new AuthenticationHeaderValue("Bearer", AdminToken) }
            });
        var updated = await getAfterUpdate.Content.ReadFromJsonAsync<EmpEmpleadoDto>();
        updated!.strNombre.Should().Be("lifecycle_updated");

        var deleteDto = new
        {
            id = created.id,
            rowVersion = updated.RowVersion
        };

        var deleteRequest = new HttpRequestMessage(HttpMethod.Delete, $"/api/v1/empleado/{created.id}")
        {
            Content = JsonContent.Create(deleteDto)
        };
        deleteRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", AdminToken);

        var deleteResponse = await _client.SendAsync(deleteRequest);
        deleteResponse.StatusCode.Should().Be(HttpStatusCode.OK);

        var finalGet = new HttpRequestMessage(HttpMethod.Get, $"/api/v1/empleado/{created.id}")
        {
            Headers = { Authorization = new AuthenticationHeaderValue("Bearer", AdminToken) }
        };
        var finalResponse = await _client.SendAsync(finalGet);
        finalResponse.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }
}
