using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Hosting;
using Microsoft.IdentityModel.Tokens;
using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Net.Http.Headers;
using System.Security.Claims;
using System.Text;
using System.Text.Json;

namespace SecurityTest;

public class JwtAlgorithmConfusionTests : IClassFixture<WebApplicationFactory<Program>>, IAsyncLifetime
{
    private readonly WebApplicationFactory<Program> _factory;
    private HttpClient _client = null!;

    public JwtAlgorithmConfusionTests(WebApplicationFactory<Program> factory)
    {
        _factory = factory.WithWebHostBuilder(builder =>
        {
            builder.UseSetting("ConnectionStrings:DefaultConnection", "Server=.;Database=Test;Trusted_Connection=True;");
            builder.UseSetting("Jwt:Key", "01123581321345589144233377610987");
            builder.UseSetting("Jwt:Issuer", "edelmeza.com");
            builder.UseSetting("Jwt:Audience", "edelmeza.com");
            builder.UseSetting("UseInMemoryDatabase", "true");
            builder.UseSetting("InMemoryDatabaseName", $"JwtAlgConfusionDb_{Guid.NewGuid():N}");
        });
    }

    public Task InitializeAsync()
    {
        _client = _factory.CreateClient();
        return Task.CompletedTask;
    }

    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task Token_With_Alg_None_Should_Be_Rejected()
    {
        var header = "{\"alg\":\"none\",\"typ\":\"JWT\"}";
        var payload = JsonSerializer.Serialize(new
        {
            sub = "admin",
            jti = Guid.NewGuid().ToString(),
            role = "Admin",
            exp = DateTimeOffset.UtcNow.AddMinutes(5).ToUnixTimeSeconds(),
            iss = "edelmeza.com",
            aud = "edelmeza.com"
        });

        var headerBase64 = Base64UrlEncoder.Encode(Encoding.UTF8.GetBytes(header));
        var payloadBase64 = Base64UrlEncoder.Encode(Encoding.UTF8.GetBytes(payload));
        var token = $"{headerBase64}.{payloadBase64}.";

        var request = new HttpRequestMessage(HttpMethod.Get, "/api/v1/test/secure");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var response = await _client.SendAsync(request);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Valid_Token_Should_Still_Work()
    {
        var key = new SymmetricSecurityKey(
            Encoding.UTF8.GetBytes("01123581321345589144233377610987"));
        var creds = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);

        var token = new JwtSecurityToken(
            issuer: "edelmeza.com",
            audience: "edelmeza.com",
            claims: new[]
            {
                new Claim(JwtRegisteredClaimNames.Sub, "admin"),
                new Claim(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString()),
                new Claim("role", "Admin")
            },
            expires: DateTime.UtcNow.AddMinutes(5),
            signingCredentials: creds
        );

        var tokenString = new JwtSecurityTokenHandler().WriteToken(token);

        var request = new HttpRequestMessage(HttpMethod.Get, "/api/v1/test/secure");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", tokenString);

        var response = await _client.SendAsync(request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }
}
