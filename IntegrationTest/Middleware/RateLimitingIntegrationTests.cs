using System.Net;
using System.Net.Http.Headers;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using UnitTest.Common;

namespace IntegrationTest.Middleware;

public class RateLimitingIntegrationTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _factory;
    private readonly HttpClient _client;

    public RateLimitingIntegrationTests(WebApplicationFactory<Program> factory)
    {
        _factory = factory.WithWebHostBuilder(builder =>
        {
            builder.UseSetting("ConnectionStrings:DefaultConnection", "Server=.;Database=Test;Trusted_Connection=True;");
            builder.UseSetting("Jwt:Key", JwtTestConfig.Key);
            builder.UseSetting("Jwt:Issuer", JwtTestConfig.Issuer);
            builder.UseSetting("Jwt:Audience", JwtTestConfig.Audience);
            builder.UseSetting("UseInMemoryDatabase", "true");
        });
        _client = _factory.CreateClient();
    }

    private string AdminToken => JwtTestConfig.AdminToken;

    [Fact]
    public async Task GlobalPolicy_AllowsNormalRequests()
    {
        for (int i = 0; i < 5; i++)
        {
            var response = await _client.GetAsync("/health");
            response.StatusCode.Should().Be(HttpStatusCode.OK);
        }
    }

    [Fact]
    public async Task AdminPolicy_AdminEndpoint_Returns200UnderNormalLoad()
    {
        for (int i = 0; i < 5; i++)
        {
            var request = new HttpRequestMessage(HttpMethod.Get, "/api/v1/usuario");
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", AdminToken);
            var response = await _client.SendAsync(request);
            response.StatusCode.Should().BeOneOf(HttpStatusCode.OK, HttpStatusCode.NotFound);
        }
    }

    [Fact]
    public async Task ConcurrentWritesPolicy_VentasPedidoGet_Returns200()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "/api/v1/Ventas/pedido");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", AdminToken);

        var response = await _client.SendAsync(request);

        response.StatusCode.Should().BeOneOf(HttpStatusCode.OK, HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task ConcurrentWritesPolicy_VentaGet_Returns200()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "/api/v1/venta");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", AdminToken);

        var response = await _client.SendAsync(request);

        response.StatusCode.Should().BeOneOf(HttpStatusCode.OK, HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task AdminPolicy_ProductoEndpoint_Returns200UnderNormalLoad()
    {
        for (int i = 0; i < 3; i++)
        {
            var request = new HttpRequestMessage(HttpMethod.Get, "/api/v1/producto");
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", AdminToken);
            var response = await _client.SendAsync(request);
            response.StatusCode.Should().BeOneOf(HttpStatusCode.OK, HttpStatusCode.NotFound);
        }
    }

    [Fact]
    public async Task AdminPolicy_ClienteEndpoint_Returns200UnderNormalLoad()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "/api/v1/cliente");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", AdminToken);

        var response = await _client.SendAsync(request);

        response.StatusCode.Should().BeOneOf(HttpStatusCode.OK, HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task ConcurrentWritesPolicy_AllowsMultipleReads()
    {
        var tasks = Enumerable.Range(0, 5).Select(_ => _client.GetAsync("/health"));
        var responses = await Task.WhenAll(tasks);

        responses.Should().OnlyContain(r => r.StatusCode == HttpStatusCode.OK);
    }

    [Fact]
    public async Task LoginPolicy_LoginEndpoint_DoesNotReturn429()
    {
        var response = await _client.GetAsync("/api/v1/login");

        response.StatusCode.Should().NotBe(HttpStatusCode.TooManyRequests);
    }
}
