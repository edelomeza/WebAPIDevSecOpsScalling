using Microsoft.AspNetCore.Mvc.Testing;
using System.Net;
using System.Net.Http.Headers;
using UnitTest.Common;
using WebAPIDevSecOps.Services;

namespace SecurityTest.EstadoVenta;

public class SecurityTests : IClassFixture<WebApplicationFactory<Program>>, IAsyncLifetime
{
    private readonly HttpClient _client;
    private const string JwtKey = "01123581321345589144233377610987";
    private const string JwtIssuer = "edelmeza.com";
    private const string JwtAudience = "edelmeza.com";

    public SecurityTests(WebApplicationFactory<Program> factory)
    {
        _client = factory.WithWebHostBuilder(builder =>
        {
            builder.UseSetting("ConnectionStrings:DefaultConnection", "Server=.;Database=Test;Trusted_Connection=True;");
            builder.UseSetting("Jwt:Key", JwtKey);
            builder.UseSetting("Jwt:Issuer", JwtIssuer);
            builder.UseSetting("Jwt:Audience", JwtAudience);
            builder.UseSetting("UseInMemoryDatabase", "true");
            builder.UseSetting("InMemoryDatabaseName", $"SecurityTestEstadoDb_{Guid.NewGuid():N}");
        }).CreateClient();
    }

    public Task InitializeAsync()
    {
        return Task.CompletedTask;
    }

    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task Should_Reject_Request_Without_Token()
    {
        var getResponse = await _client.GetAsync("/api/v1/estadoventa");
        Assert.Equal(HttpStatusCode.Unauthorized, getResponse.StatusCode);

        var getByIdResponse = await _client.GetAsync("/api/v1/estadoventa/1");
        Assert.Equal(HttpStatusCode.Unauthorized, getByIdResponse.StatusCode);
    }

    [Fact]
    public async Task Should_Reject_Token_With_Wrong_Role()
    {
        var userToken = TokenHelper.GenerateTokenWithRole(JwtKey, JwtIssuer, JwtAudience, "User");

        var request = new HttpRequestMessage(HttpMethod.Get, "/api/v1/estadoventa");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", userToken);

        var response = await _client.SendAsync(request);
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Should_Contain_Security_Headers()
    {
        var response = await _client.GetAsync("/api/v1/estadoventa");

        Assert.True(response.Headers.Contains("X-Content-Type-Options"));
        Assert.True(response.Headers.Contains("X-Frame-Options"));
        Assert.True(response.Headers.Contains("X-XSS-Protection"));
        Assert.True(response.Headers.Contains("Strict-Transport-Security"));
    }

    [Fact]
    public async Task Should_Not_Cache_Authenticated_Responses()
    {
        var token = TokenHelper.GenerateValidToken(JwtKey, JwtIssuer, JwtAudience);

        var request = new HttpRequestMessage(HttpMethod.Get, "/api/v1/estadoventa");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var response = await _client.SendAsync(request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("no-store", response.Headers.CacheControl?.ToString() ?? "");
    }

    [Fact]
    public async Task Should_Reject_Expired_Token()
    {
        var expiredToken = TokenHelper.GenerateExpiredToken(JwtKey, JwtIssuer, JwtAudience);

        var request = new HttpRequestMessage(HttpMethod.Get, "/api/v1/estadoventa");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", expiredToken);

        var response = await _client.SendAsync(request);
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }
}
