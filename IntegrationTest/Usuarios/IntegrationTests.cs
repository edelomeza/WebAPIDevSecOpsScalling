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

namespace IntegrationTest.Usuarios;

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
            builder.UseSetting("InMemoryDatabaseName", $"IntegrationTestDb_{Guid.NewGuid():N}");
        });
        _client = _factory.CreateClient();
    }

    public async Task InitializeAsync()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        db.SegUsuario.RemoveRange(db.SegUsuario);
        await db.SaveChangesAsync();
    }

    public Task DisposeAsync() => Task.CompletedTask;

    private string AdminToken => JwtTestConfig.AdminToken;

    private async Task<SegUsuarioDto> CreateUserAsync(string? uniqueName = null)
    {
        var nombre = uniqueName ?? $"int_user_{Guid.NewGuid():N}"[..30];
        var dto = TestDataFactory.CreateUsuarioCreateDto(
            nombre: nombre,
            password: "Test@1234",
            correo: $"{nombre}@test.com");

        var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/usuario")
        {
            Content = JsonContent.Create(dto)
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", AdminToken);

        var response = await _client.SendAsync(request);
        response.StatusCode.Should().Be(HttpStatusCode.Created);

        var createdUser = await response.Content.ReadFromJsonAsync<SegUsuarioDto>();
        createdUser.Should().NotBeNull();

        var getRequest = new HttpRequestMessage(HttpMethod.Get, $"/api/v1/usuario/{createdUser!.id}");
        getRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", AdminToken);
        var getResponse = await _client.SendAsync(getRequest);
        getResponse.EnsureSuccessStatusCode();
        var fullUser = await getResponse.Content.ReadFromJsonAsync<SegUsuarioDto>();
        fullUser.Should().NotBeNull();

        return fullUser!;
    }

    private async Task<SegUsuarioDto> GetUserAsync(int id)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, $"/api/v1/usuario/{id}");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", AdminToken);
        var response = await _client.SendAsync(request);
        response.EnsureSuccessStatusCode();
        var user = await response.Content.ReadFromJsonAsync<SegUsuarioDto>();
        user.Should().NotBeNull();
        return user!;
    }

    // ==================== GET ALL ====================

    [Fact]
    public async Task GetAll_EmptyDatabase_ReturnsEmptyPagedResult()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "/api/v1/usuario");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", AdminToken);

        var response = await _client.SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var result = await response.Content.ReadFromJsonAsync<PagedResult<SegUsuarioDto>>();
        result.Should().NotBeNull();
        result!.Items.Should().BeEmpty();
        result.TotalCount.Should().Be(0);
        result.PageNumber.Should().Be(1);
        result.PageSize.Should().Be(20);
        result.TotalPages.Should().Be(0);
    }

    [Fact]
    public async Task GetAll_WithUsers_ReturnsDefaultPagination()
    {
        var user1 = await CreateUserAsync("getall_default_1");
        var user2 = await CreateUserAsync("getall_default_2");
        var user3 = await CreateUserAsync("getall_default_3");

        var request = new HttpRequestMessage(HttpMethod.Get, "/api/v1/usuario");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", AdminToken);

        var response = await _client.SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var result = await response.Content.ReadFromJsonAsync<PagedResult<SegUsuarioDto>>();
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
            await CreateUserAsync($"getall_size_u{i}");

        var request = new HttpRequestMessage(HttpMethod.Get, "/api/v1/usuario?pageSize=5");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", AdminToken);

        var response = await _client.SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var result = await response.Content.ReadFromJsonAsync<PagedResult<SegUsuarioDto>>();
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
            await CreateUserAsync($"getall_page_u{i}");

        var request = new HttpRequestMessage(HttpMethod.Get, "/api/v1/usuario?pageNumber=2&pageSize=10");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", AdminToken);

        var response = await _client.SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var result = await response.Content.ReadFromJsonAsync<PagedResult<SegUsuarioDto>>();
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
            await CreateUserAsync($"getall_last_u{i}");

        var request = new HttpRequestMessage(HttpMethod.Get, "/api/v1/usuario?pageNumber=3&pageSize=10");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", AdminToken);

        var response = await _client.SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var result = await response.Content.ReadFromJsonAsync<PagedResult<SegUsuarioDto>>();
        result!.Items.Should().HaveCount(5);
        result.TotalCount.Should().Be(25);
        result.PageNumber.Should().Be(3);
        result.PageSize.Should().Be(10);
        result.TotalPages.Should().Be(3);
    }

    [Fact]
    public async Task GetAll_WhenPageExceedsTotal_ReturnsEmpty()
    {
        await CreateUserAsync("getall_empty_page");

        var request = new HttpRequestMessage(HttpMethod.Get, "/api/v1/usuario?pageNumber=10&pageSize=10");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", AdminToken);

        var response = await _client.SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var result = await response.Content.ReadFromJsonAsync<PagedResult<SegUsuarioDto>>();
        result!.Items.Should().BeEmpty();
        result.TotalCount.Should().Be(1);
        result.PageNumber.Should().Be(10);
        result.PageSize.Should().Be(10);
        result.TotalPages.Should().Be(1);
    }

    // ==================== SEARCH BY NAME ====================

    [Fact]
    public async Task SearchByName_ReturnsMatchingUsers()
    {
        await CreateUserAsync("search_eduardo");
        await CreateUserAsync("search_edel");
        await CreateUserAsync("search_maria");

        var request = new HttpRequestMessage(HttpMethod.Get, "/api/v1/usuario/buscar?texto=ed");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", AdminToken);

        var response = await _client.SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var result = await response.Content.ReadFromJsonAsync<PagedResult<SegUsuarioDto>>();
        result!.Items.Should().HaveCount(2);
        result.TotalCount.Should().Be(2);
        result.Items.Select(i => i.strNombre).Should().Contain(["search_eduardo", "search_edel"]);
    }

    [Fact]
    public async Task SearchByName_ReturnsEmpty_WhenNoMatch()
    {
        await CreateUserAsync("search_juan");

        var request = new HttpRequestMessage(HttpMethod.Get, "/api/v1/usuario/buscar?texto=xyz");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", AdminToken);

        var response = await _client.SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var result = await response.Content.ReadFromJsonAsync<PagedResult<SegUsuarioDto>>();
        result!.Items.Should().BeEmpty();
        result.TotalCount.Should().Be(0);
    }

    [Fact]
    public async Task SearchByName_WithPagination_ReturnsCorrectPage()
    {
        for (int i = 1; i <= 5; i++)
            await CreateUserAsync($"search_page_admin{i}");

        var request = new HttpRequestMessage(HttpMethod.Get, "/api/v1/usuario/buscar?texto=admin&pageNumber=2&pageSize=2");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", AdminToken);

        var response = await _client.SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var result = await response.Content.ReadFromJsonAsync<PagedResult<SegUsuarioDto>>();
        result!.Items.Should().HaveCount(2);
        result.TotalCount.Should().Be(5);
        result.PageNumber.Should().Be(2);
        result.PageSize.Should().Be(2);
    }

    [Fact]
    public async Task SearchByName_EmptyTexto_ReturnsBadRequest()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "/api/v1/usuario/buscar?texto=");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", AdminToken);

        var response = await _client.SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

        // ==================== AUTOCOMPLETE ====================

        [Fact]
        public async Task Autocomplete_ReturnsMatchingUsers()
        {
            await CreateUserAsync("ac_eduardo");
            await CreateUserAsync("ac_edel");
            await CreateUserAsync("ac_maria");

            var request = new HttpRequestMessage(HttpMethod.Get, "/api/v1/usuario/autocomplete?texto=ed");
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", AdminToken);

            var response = await _client.SendAsync(request);

            response.StatusCode.Should().Be(HttpStatusCode.OK);

            var result = await response.Content.ReadFromJsonAsync<IEnumerable<SegUsuarioAutocompleteDto>>();
            result.Should().HaveCount(2);
            result!.Select(r => r.strNombre).Should().Contain(["ac_eduardo", "ac_edel"]);
        }

        [Fact]
        public async Task Autocomplete_RespectsMaxResultados()
        {
            for (int i = 0; i < 5; i++)
                await CreateUserAsync($"ac_max_u{i}");

            var request = new HttpRequestMessage(HttpMethod.Get, "/api/v1/usuario/autocomplete?texto=ac_max&maxResultados=2");
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", AdminToken);

            var response = await _client.SendAsync(request);

            response.StatusCode.Should().Be(HttpStatusCode.OK);

            var result = await response.Content.ReadFromJsonAsync<IEnumerable<SegUsuarioAutocompleteDto>>();
            result.Should().HaveCount(2);
        }

        [Fact]
        public async Task Autocomplete_ReturnsEmpty_WhenNoMatch()
        {
            await CreateUserAsync("ac_nomatch");

            var request = new HttpRequestMessage(HttpMethod.Get, "/api/v1/usuario/autocomplete?texto=xyz");
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", AdminToken);

            var response = await _client.SendAsync(request);

            response.StatusCode.Should().Be(HttpStatusCode.OK);

            var result = await response.Content.ReadFromJsonAsync<IEnumerable<SegUsuarioAutocompleteDto>>();
            result.Should().BeEmpty();
        }

        [Fact]
        public async Task Autocomplete_EmptyTexto_ReturnsBadRequest()
        {
            var request = new HttpRequestMessage(HttpMethod.Get, "/api/v1/usuario/autocomplete?texto=");
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", AdminToken);

            var response = await _client.SendAsync(request);

            response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        }

        [Fact]
        public async Task Autocomplete_DoesNotExposePassword()
        {
            await CreateUserAsync("ac_no_pwd");

            var request = new HttpRequestMessage(HttpMethod.Get, "/api/v1/usuario/autocomplete?texto=ac_no");
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", AdminToken);

            var response = await _client.SendAsync(request);

            var json = await response.Content.ReadAsStringAsync();
            json.Should().NotContain("strPWD");
            json.Should().NotContain("password");
        }

        [Fact]
        public async Task GetAll_DoesNotExposePassword()
    {
        await CreateUserAsync("getall_no_pwd");

        var request = new HttpRequestMessage(HttpMethod.Get, "/api/v1/usuario");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", AdminToken);

        var response = await _client.SendAsync(request);

        var json = await response.Content.ReadAsStringAsync();
        json.Should().NotContain("strPWD");
        json.Should().NotContain("strPwd");
        json.Should().NotContain("password");
    }

    [Fact]
    public async Task GetAll_HasCacheControlHeader()
    {
        await CreateUserAsync("getall_cache");

        var request = new HttpRequestMessage(HttpMethod.Get, "/api/v1/usuario");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", AdminToken);

        var response = await _client.SendAsync(request);

        response.Headers.CacheControl.Should().NotBeNull();
        response.Headers.CacheControl!.NoStore.Should().BeTrue();
    }

    // ==================== GET BY ID ====================

    [Fact]
    public async Task GetById_ExistingUser_ReturnsUser()
    {
        var user = await CreateUserAsync("getbyid_exists");

        var result = await GetUserAsync(user.id);

        result.id.Should().Be(user.id);
        result.strNombre.Should().Be(user.strNombre);
        result.strCorreoElectronico.Should().Be(user.strCorreoElectronico);
    }

    [Fact]
    public async Task GetById_NonExistentId_ReturnsNotFound()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "/api/v1/usuario/9999");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", AdminToken);

        var response = await _client.SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task GetById_NegativeId_ReturnsNotFound()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "/api/v1/usuario/-1");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", AdminToken);

        var response = await _client.SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task GetById_ZeroId_ReturnsNotFound()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "/api/v1/usuario/0");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", AdminToken);

        var response = await _client.SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task GetById_MultipleUsers_ReturnsCorrectUser()
    {
        var user1 = await CreateUserAsync("getbyid_multi_a");
        var user2 = await CreateUserAsync("getbyid_multi_b");
        var user3 = await CreateUserAsync("getbyid_multi_c");

        var result = await GetUserAsync(user2.id);

        result.id.Should().Be(user2.id);
        result.strNombre.Should().Be("getbyid_multi_b");
        result.strCorreoElectronico.Should().Be("getbyid_multi_b@test.com");
    }

    [Fact]
    public async Task GetById_DoesNotExposePassword()
    {
        var user = await CreateUserAsync("getbyid_no_pwd");

        var request = new HttpRequestMessage(HttpMethod.Get, $"/api/v1/usuario/{user.id}");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", AdminToken);

        var response = await _client.SendAsync(request);

        var json = await response.Content.ReadAsStringAsync();
        json.Should().NotContain("strPWD");
        json.Should().NotContain("password");
    }

    // ==================== CREATE ====================

    [Fact]
    public async Task Create_ValidDto_Returns201WithLocationHeader()
    {
        var dto = TestDataFactory.CreateUsuarioCreateDto(
            nombre: "create_location",
            password: "Test@1234",
            correo: "create_location@test.com");

        var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/usuario")
        {
            Content = JsonContent.Create(dto)
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", AdminToken);

        var response = await _client.SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.Created);
        response.Headers.Location.Should().NotBeNull();
        response.Headers.Location!.ToString().Should().Contain("/api/v1/Usuario/");
    }

    [Fact]
    public async Task Create_ValidDto_ReturnsSegUsuarioDtoWithId()
    {
        var dto = TestDataFactory.CreateUsuarioCreateDto(
            nombre: "create_with_id",
            password: "Test@1234",
            correo: "create_with_id@test.com");
        dto.id = 0;

        var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/usuario")
        {
            Content = JsonContent.Create(dto)
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", AdminToken);

        var response = await _client.SendAsync(request);

        var createdUser = await response.Content.ReadFromJsonAsync<SegUsuarioDto>();
        createdUser.Should().NotBeNull();
        createdUser!.id.Should().BeGreaterThan(0);
    }

    [Fact]
    public async Task Create_ValidDto_ReturnsCorrectNombreAndCorreo()
    {
        var dto = TestDataFactory.CreateUsuarioCreateDto(
            nombre: "create_correct_data",
            password: "Test@1234",
            correo: "create_correct_data@test.com");

        var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/usuario")
        {
            Content = JsonContent.Create(dto)
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", AdminToken);

        var response = await _client.SendAsync(request);

        var createdUser = await response.Content.ReadFromJsonAsync<SegUsuarioDto>();
        createdUser!.strNombre.Should().Be("create_correct_data");
        createdUser.strCorreoElectronico.Should().Be("create_correct_data@test.com");
    }

    [Fact]
    public async Task Create_ValidDto_CreatesUserInDatabase()
    {
        var dto = TestDataFactory.CreateUsuarioCreateDto(
            nombre: "create_persist",
            password: "Test@1234",
            correo: "create_persist@test.com");

        var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/usuario")
        {
            Content = JsonContent.Create(dto)
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", AdminToken);

        var response = await _client.SendAsync(request);

        var createdUser = await response.Content.ReadFromJsonAsync<SegUsuarioDto>();

        var getRequest = new HttpRequestMessage(HttpMethod.Get, $"/api/v1/usuario/{createdUser!.id}");
        getRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", AdminToken);
        var getResponse = await _client.SendAsync(getRequest);

        getResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var fetchedUser = await getResponse.Content.ReadFromJsonAsync<SegUsuarioDto>();
        fetchedUser!.strNombre.Should().Be("create_persist");
    }

    [Fact]
    public async Task Create_ValidDto_DoesNotExposePassword()
    {
        var dto = TestDataFactory.CreateUsuarioCreateDto(
            nombre: "create_no_pwd",
            password: "Test@1234",
            correo: "create_no_pwd@test.com");

        var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/usuario")
        {
            Content = JsonContent.Create(dto)
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", AdminToken);

        var response = await _client.SendAsync(request);

        var json = await response.Content.ReadAsStringAsync();
        json.Should().NotContain("strPWD");
        json.Should().NotContain("password");
    }

    [Fact]
    public async Task Create_DuplicateNombre_Returns400()
    {
        await CreateUserAsync("create_dup_nombre");

        var dto = TestDataFactory.CreateUsuarioCreateDto(
            nombre: "create_dup_nombre",
            password: "Test@5678",
            correo: "create_dup_other@test.com");

        var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/usuario")
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
        var dto = TestDataFactory.CreateUsuarioCreateDto(
            nombre: "",
            password: "Test@1234",
            correo: "create_empty@test.com");

        var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/usuario")
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
        var dto = TestDataFactory.CreateUsuarioCreateDto(
            nombre: new string('a', 51),
            password: "Test@1234",
            correo: "create_long@test.com");

        var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/usuario")
        {
            Content = JsonContent.Create(dto)
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", AdminToken);

        var response = await _client.SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Create_InvalidNombreCharacters_Returns400()
    {
        var dto = TestDataFactory.CreateUsuarioCreateDto(
            nombre: "user@name!",
            password: "Test@1234",
            correo: "create_invalidchar@test.com");

        var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/usuario")
        {
            Content = JsonContent.Create(dto)
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", AdminToken);

        var response = await _client.SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Create_PasswordTooShort_Returns400()
    {
        var dto = TestDataFactory.CreateUsuarioCreateDto(
            nombre: "create_short_pwd",
            password: "1234567",
            correo: "create_shortpwd@test.com");

        var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/usuario")
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
        var dto = TestDataFactory.CreateUsuarioCreateDto(
            nombre: "create_bad_email",
            password: "Test@1234",
            correo: "not-an-email");

        var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/usuario")
        {
            Content = JsonContent.Create(dto)
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", AdminToken);

        var response = await _client.SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Create_TrimsNombre()
    {
        var dto = TestDataFactory.CreateUsuarioCreateDto(
            nombre: "  create_trimmed  ",
            password: "Test@1234",
            correo: "create_trimmed@test.com");

        var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/usuario")
        {
            Content = JsonContent.Create(dto)
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", AdminToken);

        var response = await _client.SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.Created);

        var createdUser = await response.Content.ReadFromJsonAsync<SegUsuarioDto>();

        var getRequest = new HttpRequestMessage(HttpMethod.Get, $"/api/v1/usuario/{createdUser!.id}");
        getRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", AdminToken);
        var getResponse = await _client.SendAsync(getRequest);

        var fetchedUser = await getResponse.Content.ReadFromJsonAsync<SegUsuarioDto>();
        fetchedUser!.strNombre.Should().Be("create_trimmed");
    }

    // ==================== UPDATE ====================

    [Fact]
    public async Task Update_ValidUpdate_Returns204()
    {
        var user = await CreateUserAsync("update_valid");

        var dto = new
        {
            id = user.id,
            strNombre = "update_valid_modified",
            strCorreoElectronico = "update_valid_modified@test.com",
            rowVersion = user.RowVersion
        };

        var request = new HttpRequestMessage(HttpMethod.Put, $"/api/v1/usuario/{user.id}")
        {
            Content = JsonContent.Create(dto)
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", AdminToken);

        var response = await _client.SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.NoContent);
    }

    [Fact]
    public async Task Update_ValidUpdate_ChangesNombreAndCorreo()
    {
        var user = await CreateUserAsync("update_changes");

        var dto = new
        {
            id = user.id,
            strNombre = "update_changes_modified",
            strCorreoElectronico = "update_changes_modified@test.com",
            rowVersion = user.RowVersion
        };

        var request = new HttpRequestMessage(HttpMethod.Put, $"/api/v1/usuario/{user.id}")
        {
            Content = JsonContent.Create(dto)
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", AdminToken);

        await _client.SendAsync(request);

        var updatedUser = await GetUserAsync(user.id);
        updatedUser.strNombre.Should().Be("update_changes_modified");
        updatedUser.strCorreoElectronico.Should().Be("update_changes_modified@test.com");
    }

    [Fact]
    public async Task Update_WithNewPassword_Returns204()
    {
        var user = await CreateUserAsync("update_newpwd");

        var dto = new
        {
            id = user.id,
            strNombre = user.strNombre,
            strCorreoElectronico = user.strCorreoElectronico,
            strPWD = "NewPass@5678",
            rowVersion = user.RowVersion
        };

        var request = new HttpRequestMessage(HttpMethod.Put, $"/api/v1/usuario/{user.id}")
        {
            Content = JsonContent.Create(dto)
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", AdminToken);

        var response = await _client.SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.NoContent);
    }

    [Fact]
    public async Task Update_WithoutPassword_KeepsExistingData()
    {
        var user = await CreateUserAsync("update_nopwd");

        var dto = new
        {
            id = user.id,
            strNombre = "update_nopwd_modified",
            strCorreoElectronico = "update_nopwd_modified@test.com",
            rowVersion = user.RowVersion
        };

        var request = new HttpRequestMessage(HttpMethod.Put, $"/api/v1/usuario/{user.id}")
        {
            Content = JsonContent.Create(dto)
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", AdminToken);

        var response = await _client.SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.NoContent);

        var updatedUser = await GetUserAsync(user.id);
        updatedUser.strNombre.Should().Be("update_nopwd_modified");
        updatedUser.strCorreoElectronico.Should().Be("update_nopwd_modified@test.com");
    }

    [Fact]
    public async Task Update_RouteIdMismatch_Returns400()
    {
        var user = await CreateUserAsync("update_idmismatch");

        var dto = new
        {
            id = 999,
            strNombre = "should_not_work",
            strCorreoElectronico = "should_not_work@test.com",
            rowVersion = user.RowVersion
        };

        var request = new HttpRequestMessage(HttpMethod.Put, $"/api/v1/usuario/{user.id}")
        {
            Content = JsonContent.Create(dto)
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", AdminToken);

        var response = await _client.SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Update_DuplicateNombre_Returns400()
    {
        var user1 = await CreateUserAsync("update_dup_a");
        var user2 = await CreateUserAsync("update_dup_b");

        var dto = new
        {
            id = user2.id,
            strNombre = "update_dup_a",
            strCorreoElectronico = user2.strCorreoElectronico,
            rowVersion = user2.RowVersion
        };

        var request = new HttpRequestMessage(HttpMethod.Put, $"/api/v1/usuario/{user2.id}")
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
            strCorreoElectronico = "nonexistent@test.com",
            rowVersion = new byte[] { 1, 0, 0, 0 }
        };

        var request = new HttpRequestMessage(HttpMethod.Put, "/api/v1/usuario/9999")
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
        var user = await CreateUserAsync("update_stale_rv");

        var dto = new
        {
            id = user.id,
            strNombre = "update_stale_rv_modified",
            strCorreoElectronico = "update_stale_rv_modified@test.com",
            rowVersion = new byte[] { 2, 0, 0, 0 }
        };

        var request = new HttpRequestMessage(HttpMethod.Put, $"/api/v1/usuario/{user.id}")
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
        var user = await CreateUserAsync("update_empty");

        var dto = new
        {
            id = user.id,
            strNombre = "",
            strCorreoElectronico = user.strCorreoElectronico,
            rowVersion = user.RowVersion
        };

        var request = new HttpRequestMessage(HttpMethod.Put, $"/api/v1/usuario/{user.id}")
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
        var user = await CreateUserAsync("update_toolong");

        var dto = new
        {
            id = user.id,
            strNombre = new string('a', 51),
            strCorreoElectronico = user.strCorreoElectronico,
            rowVersion = user.RowVersion
        };

        var request = new HttpRequestMessage(HttpMethod.Put, $"/api/v1/usuario/{user.id}")
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
        var user = await CreateUserAsync("update_bademail");

        var dto = new
        {
            id = user.id,
            strNombre = user.strNombre,
            strCorreoElectronico = "not-an-email",
            rowVersion = user.RowVersion
        };

        var request = new HttpRequestMessage(HttpMethod.Put, $"/api/v1/usuario/{user.id}")
        {
            Content = JsonContent.Create(dto)
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", AdminToken);

        var response = await _client.SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Update_TrimsNombre()
    {
        var user = await CreateUserAsync("update_trim");

        var dto = new
        {
            id = user.id,
            strNombre = "  update_trim_modified  ",
            strCorreoElectronico = user.strCorreoElectronico,
            rowVersion = user.RowVersion
        };

        var request = new HttpRequestMessage(HttpMethod.Put, $"/api/v1/usuario/{user.id}")
        {
            Content = JsonContent.Create(dto)
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", AdminToken);

        await _client.SendAsync(request);

        var updatedUser = await GetUserAsync(user.id);
        updatedUser.strNombre.Should().Be("update_trim_modified");
    }

    [Fact]
    public async Task Update_SelfRename_AllowsSameName()
    {
        var user = await CreateUserAsync("update_selfrename");

        var dto = new
        {
            id = user.id,
            strNombre = "update_selfrename",
            strCorreoElectronico = user.strCorreoElectronico,
            rowVersion = user.RowVersion
        };

        var request = new HttpRequestMessage(HttpMethod.Put, $"/api/v1/usuario/{user.id}")
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
        var user = await CreateUserAsync("delete_valid");

        var dto = new
        {
            id = user.id,
            rowVersion = user.RowVersion
        };

        var request = new HttpRequestMessage(HttpMethod.Delete, $"/api/v1/usuario/{user.id}")
        {
            Content = JsonContent.Create(dto)
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", AdminToken);

        var response = await _client.SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Delete_ValidDelete_RemovesUserFromDatabase()
    {
        var user = await CreateUserAsync("delete_removes");

        var dto = new
        {
            id = user.id,
            rowVersion = user.RowVersion
        };

        var request = new HttpRequestMessage(HttpMethod.Delete, $"/api/v1/usuario/{user.id}")
        {
            Content = JsonContent.Create(dto)
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", AdminToken);

        await _client.SendAsync(request);

        var getRequest = new HttpRequestMessage(HttpMethod.Get, $"/api/v1/usuario/{user.id}");
        getRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", AdminToken);

        var getResponse = await _client.SendAsync(getRequest);
        getResponse.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Delete_RouteIdMismatch_Returns400()
    {
        var user = await CreateUserAsync("delete_idmismatch");

        var dto = new
        {
            id = 999,
            rowVersion = user.RowVersion
        };

        var request = new HttpRequestMessage(HttpMethod.Delete, $"/api/v1/usuario/{user.id}")
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

        var request = new HttpRequestMessage(HttpMethod.Delete, "/api/v1/usuario/9999")
        {
            Content = JsonContent.Create(dto)
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", AdminToken);

        var response = await _client.SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Delete_OnlyRemovesTargetUser()
    {
        var user1 = await CreateUserAsync("delete_only_a");
        var user2 = await CreateUserAsync("delete_only_b");

        var dto = new
        {
            id = user1.id,
            rowVersion = user1.RowVersion
        };

        var request = new HttpRequestMessage(HttpMethod.Delete, $"/api/v1/usuario/{user1.id}")
        {
            Content = JsonContent.Create(dto)
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", AdminToken);

        await _client.SendAsync(request);

        var getDeletedRequest = new HttpRequestMessage(HttpMethod.Get, $"/api/v1/usuario/{user1.id}");
        getDeletedRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", AdminToken);
        var getDeletedResponse = await _client.SendAsync(getDeletedRequest);
        getDeletedResponse.StatusCode.Should().Be(HttpStatusCode.NotFound);

        var getRemainingRequest = new HttpRequestMessage(HttpMethod.Get, $"/api/v1/usuario/{user2.id}");
        getRemainingRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", AdminToken);
        var getRemainingResponse = await _client.SendAsync(getRemainingRequest);
        getRemainingResponse.StatusCode.Should().Be(HttpStatusCode.OK);

        var remainingUser = await getRemainingResponse.Content.ReadFromJsonAsync<SegUsuarioDto>();
        remainingUser!.id.Should().Be(user2.id);
        remainingUser.strNombre.Should().Be("delete_only_b");
    }

    [Fact]
    public async Task Delete_StaleRowVersion_Returns409()
    {
        var user = await CreateUserAsync("delete_stale_rv");

        var dto = new
        {
            id = user.id,
            rowVersion = new byte[] { 2, 0, 0, 0 }
        };

        var request = new HttpRequestMessage(HttpMethod.Delete, $"/api/v1/usuario/{user.id}")
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
        var nombre = "lifecycle_user";
        var dto = TestDataFactory.CreateUsuarioCreateDto(
            nombre: nombre,
            password: "Initial@123",
            correo: $"{nombre}@test.com");

        var createRequest = new HttpRequestMessage(HttpMethod.Post, "/api/v1/usuario")
        {
            Content = JsonContent.Create(dto)
        };
        createRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", AdminToken);

        var createResponse = await _client.SendAsync(createRequest);
        createResponse.StatusCode.Should().Be(HttpStatusCode.Created);

        var createdUser = await createResponse.Content.ReadFromJsonAsync<SegUsuarioDto>();

        var getAfterCreate = await GetUserAsync(createdUser!.id);
        getAfterCreate.strNombre.Should().Be(nombre);
        getAfterCreate.strCorreoElectronico.Should().Be($"{nombre}@test.com");

        var updateDto = new
        {
            id = createdUser.id,
            strNombre = "lifecycle_user_updated",
            strCorreoElectronico = "lifecycle_user_updated@test.com",
            rowVersion = getAfterCreate.RowVersion
        };

        var updateRequest = new HttpRequestMessage(HttpMethod.Put, $"/api/v1/usuario/{createdUser.id}")
        {
            Content = JsonContent.Create(updateDto)
        };
        updateRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", AdminToken);

        var updateResponse = await _client.SendAsync(updateRequest);
        updateResponse.StatusCode.Should().Be(HttpStatusCode.NoContent);

        var getAfterUpdate = await GetUserAsync(createdUser.id);
        getAfterUpdate.strNombre.Should().Be("lifecycle_user_updated");
        getAfterUpdate.strCorreoElectronico.Should().Be("lifecycle_user_updated@test.com");

        var deleteDto = new
        {
            id = createdUser.id,
            rowVersion = getAfterUpdate.RowVersion
        };

        var deleteRequest = new HttpRequestMessage(HttpMethod.Delete, $"/api/v1/usuario/{createdUser.id}")
        {
            Content = JsonContent.Create(deleteDto)
        };
        deleteRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", AdminToken);

        var deleteResponse = await _client.SendAsync(deleteRequest);
        deleteResponse.StatusCode.Should().Be(HttpStatusCode.OK);

        var getAfterDelete = new HttpRequestMessage(HttpMethod.Get, $"/api/v1/usuario/{createdUser.id}");
        getAfterDelete.Headers.Authorization = new AuthenticationHeaderValue("Bearer", AdminToken);

        var finalResponse = await _client.SendAsync(getAfterDelete);
        finalResponse.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }
}
