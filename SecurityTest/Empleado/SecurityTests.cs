using Microsoft.AspNetCore.Mvc.Testing;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Threading;
using WebAPIDevSecOps.Dto;
using WebAPIDevSecOps.Services;
using UnitTest.Common;

namespace SecurityTest.Empleado;

public class SecurityTests : IClassFixture<WebApplicationFactory<Program>>, IAsyncLifetime
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
    private const string JwtKey = "01123581321345589144233377610987";
    private const string JwtIssuer = "edelmeza.com";
    private const string JwtAudience = "edelmeza.com";

    public SecurityTests(WebApplicationFactory<Program> factory)
    {
        _factory = factory.WithWebHostBuilder(builder =>
        {
            builder.UseSetting("ConnectionStrings:DefaultConnection", "Server=.;Database=Test;Trusted_Connection=True;");
            builder.UseSetting("Jwt:Key", JwtKey);
            builder.UseSetting("Jwt:Issuer", JwtIssuer);
            builder.UseSetting("Jwt:Audience", JwtAudience);
            builder.UseSetting("UseInMemoryDatabase", "true");
            builder.UseSetting("InMemoryDatabaseName", $"SecurityTestEmpDb_{Guid.NewGuid():N}");
        });
        _client = _factory.CreateClient();
    }

    public Task InitializeAsync()
    {
        return Task.CompletedTask;
    }

    public Task DisposeAsync() => Task.CompletedTask;

    private string AdminToken => TokenHelper.GenerateValidToken(JwtKey, JwtIssuer, JwtAudience);

    private async Task<EmpEmpleadoDto?> CreateEmpleadoAsync(string uniqueName)
    {
        var dto = new EmpEmpleadoCreateDto
        {
            strNombre = uniqueName,
            strAPaterno = "Test",
            strCURP = NextCurp()
        };

        var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/empleado")
        {
            Content = JsonContent.Create(dto)
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", AdminToken);

        var response = await _client.SendAsync(request);
        if (!response.IsSuccessStatusCode)
        {
            var body = await response.Content.ReadAsStringAsync();
            throw new InvalidOperationException($"Create failed: {(int)response.StatusCode} - {body}");
        }

        return await response.Content.ReadFromJsonAsync<EmpEmpleadoDto>();
    }

    [Fact]
    public async Task Should_Reject_Request_Without_Token()
    {
        var getResponse = await _client.GetAsync("/api/v1/empleado");
        Assert.Equal(HttpStatusCode.Unauthorized, getResponse.StatusCode);

        var getByIdResponse = await _client.GetAsync("/api/v1/empleado/1");
        Assert.Equal(HttpStatusCode.Unauthorized, getByIdResponse.StatusCode);

        var searchResponse = await _client.GetAsync("/api/v1/empleado/buscar?texto=juan");
        Assert.Equal(HttpStatusCode.Unauthorized, searchResponse.StatusCode);

        var postResponse = await _client.PostAsJsonAsync("/api/v1/empleado", new { });
        Assert.Equal(HttpStatusCode.Unauthorized, postResponse.StatusCode);

        var putRequest = new HttpRequestMessage(HttpMethod.Put, "/api/v1/empleado/1") { Content = JsonContent.Create(new { }) };
        var putResponse = await _client.SendAsync(putRequest);
        Assert.Equal(HttpStatusCode.Unauthorized, putResponse.StatusCode);

        var deleteResponse = await _client.DeleteAsync("/api/v1/empleado/1");
        Assert.Equal(HttpStatusCode.Unauthorized, deleteResponse.StatusCode);
    }

    [Fact]
    public async Task Should_Reject_Token_With_Wrong_Role()
    {
        var userToken = TokenHelper.GenerateTokenWithRole(JwtKey, JwtIssuer, JwtAudience, "User");

        var request = new HttpRequestMessage(HttpMethod.Get, "/api/v1/empleado");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", userToken);

        var response = await _client.SendAsync(request);
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Should_Reject_Expired_Token()
    {
        var expiredToken = TokenHelper.GenerateExpiredToken(JwtKey, JwtIssuer, JwtAudience);

        var request = new HttpRequestMessage(HttpMethod.Get, "/api/v1/empleado");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", expiredToken);

        var response = await _client.SendAsync(request);
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Should_Reject_Blacklisted_Token()
    {
        var token = await UnitTest.Common.BlacklistHelper.GenerateAndBlacklistTokenAsync(_factory.Services, "01123581321345589144233377610987", "edelmeza.com", "edelmeza.com");

        var request = new HttpRequestMessage(HttpMethod.Get, "/api/v1/empleado");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var response = await _client.SendAsync(request);
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Should_Reject_Tampered_Token()
    {
        var token = TokenHelper.GenerateValidToken(JwtKey, JwtIssuer, JwtAudience);
        var tampered = token[..^2] + "xx";

        var request = new HttpRequestMessage(HttpMethod.Get, "/api/v1/empleado");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", tampered);

        var response = await _client.SendAsync(request);
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Should_Reject_Invalid_Signature()
    {
        var fakeKey = "clave_falsa_super_insegura_123456789";
        var fakeToken = TokenHelper.GenerateValidToken(fakeKey, JwtIssuer, JwtAudience);

        var request = new HttpRequestMessage(HttpMethod.Get, "/api/v1/empleado");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", fakeToken);

        var response = await _client.SendAsync(request);
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Should_Reject_SQL_Injection_In_Nombre()
    {
        var dto = new EmpEmpleadoCreateDto
        {
            strNombre = "'; DROP TABLE EmpEmpleado; --",
            strAPaterno = "Test",
            strCURP = "SQLINJECT123456789"
        };

        var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/empleado")
        {
            Content = JsonContent.Create(dto)
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", AdminToken);

        var response = await _client.SendAsync(request);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Should_Reject_XSS_In_Nombre()
    {
        var dto = new EmpEmpleadoCreateDto
        {
            strNombre = "<script>alert('xss')</script>",
            strAPaterno = "Test",
            strCURP = "XSS123456789012345"
        };

        var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/empleado")
        {
            Content = JsonContent.Create(dto)
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", AdminToken);

        var response = await _client.SendAsync(request);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Should_Reject_Empty_Nombre()
    {
        var dto = new EmpEmpleadoCreateDto
        {
            strNombre = "",
            strCURP = "EMPTY1234567890123"
        };

        var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/empleado")
        {
            Content = JsonContent.Create(dto)
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", AdminToken);

        var response = await _client.SendAsync(request);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Should_Reject_Overly_Long_Nombre()
    {
        var dto = new EmpEmpleadoCreateDto
        {
            strNombre = new string('a', 51),
            strCURP = "LONG12345678901234"
        };

        var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/empleado")
        {
            Content = JsonContent.Create(dto)
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", AdminToken);

        var response = await _client.SendAsync(request);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Should_Reject_Negative_Id()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "/api/v1/empleado/-1");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", AdminToken);

        var response = await _client.SendAsync(request);
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Should_Reject_Stale_RowVersion()
    {
        var emp = await CreateEmpleadoAsync("conflict_test");
        Assert.NotNull(emp);

        var request = new HttpRequestMessage(HttpMethod.Put, $"/api/v1/empleado/{emp.id}")
        {
            Content = JsonContent.Create(new
            {
                id = emp.id,
                strNombre = "conflict_test_updated",
                strAPaterno = "Updated",
                rowVersion = new byte[] { 2, 0, 0, 0 }
            })
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", AdminToken);

        var response = await _client.SendAsync(request);
        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    [Fact]
    public async Task Should_Reject_Duplicate_CURP()
    {
        var emp = await CreateEmpleadoAsync("dup_curp_test");
        Assert.NotNull(emp);

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
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Should_Reject_NonExistent_Empleado()
    {
        var token = AdminToken;

        var getRequest = new HttpRequestMessage(HttpMethod.Get, "/api/v1/empleado/9999");
        getRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        var getResponse = await _client.SendAsync(getRequest);
        Assert.Equal(HttpStatusCode.NotFound, getResponse.StatusCode);

        var deleteRequest = new HttpRequestMessage(HttpMethod.Delete, "/api/v1/empleado/9999")
        {
            Content = JsonContent.Create(new { id = 9999, rowVersion = new byte[] { 1, 0, 0, 0 } })
        };
        deleteRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        var deleteResponse = await _client.SendAsync(deleteRequest);
        Assert.Equal(HttpStatusCode.NotFound, deleteResponse.StatusCode);
    }

    [Fact]
    public async Task Should_Contain_Security_Headers()
    {
        var response = await _client.GetAsync("/api/v1/empleado");

        Assert.True(response.Headers.Contains("X-Content-Type-Options"));
        Assert.True(response.Headers.Contains("X-Frame-Options"));
        Assert.True(response.Headers.Contains("X-XSS-Protection"));
        Assert.True(response.Headers.Contains("Strict-Transport-Security"));
    }

    [Fact]
    public async Task Should_Not_Cache_Authenticated_Responses()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "/api/v1/empleado");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", AdminToken);

        var response = await _client.SendAsync(request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("no-store", response.Headers.CacheControl?.ToString() ?? "");
    }
}
