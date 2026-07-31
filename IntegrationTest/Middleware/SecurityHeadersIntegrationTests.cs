using System.Net;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using UnitTest.Common;

namespace IntegrationTest.Middleware;

public class SecurityHeadersIntegrationTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _factory;
    private readonly HttpClient _client;

    public SecurityHeadersIntegrationTests(WebApplicationFactory<Program> factory)
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

    [Fact]
    public async Task HealthEndpoint_ContainsXContentTypeOptions()
    {
        var response = await _client.GetAsync("/health");
        response.Headers.GetValues("X-Content-Type-Options").First().Should().Be("nosniff");
    }

    [Fact]
    public async Task HealthEndpoint_ContainsXFrameOptions()
    {
        var response = await _client.GetAsync("/health");
        response.Headers.GetValues("X-Frame-Options").First().Should().Be("DENY");
    }

    [Fact]
    public async Task HealthEndpoint_ContainsReferrerPolicy()
    {
        var response = await _client.GetAsync("/health");
        response.Headers.GetValues("Referrer-Policy").First().Should().Be("no-referrer");
    }

    [Fact]
    public async Task HealthEndpoint_ContainsXXSSProtection()
    {
        var response = await _client.GetAsync("/health");
        response.Headers.GetValues("X-XSS-Protection").First().Should().Be("1; mode=block");
    }

    [Fact]
    public async Task HealthEndpoint_ContainsStrictTransportSecurity()
    {
        var response = await _client.GetAsync("/health");
        response.Headers.GetValues("Strict-Transport-Security").First()
            .Should().Be("max-age=31536000; includeSubDomains; preload");
    }

    [Fact]
    public async Task HealthEndpoint_ContainsPermissionsPolicy()
    {
        var response = await _client.GetAsync("/health");
        response.Headers.GetValues("Permissions-Policy").First().Should().Be("geolocation=()");
    }

    [Fact]
    public async Task NonExistentEndpoint_ContainsAllSecurityHeaders()
    {
        var response = await _client.GetAsync("/nonexistent-route");

        response.Headers.GetValues("X-Content-Type-Options").First().Should().Be("nosniff");
        response.Headers.GetValues("X-Frame-Options").First().Should().Be("DENY");
        response.Headers.GetValues("Referrer-Policy").First().Should().Be("no-referrer");
        response.Headers.GetValues("X-XSS-Protection").First().Should().Be("1; mode=block");
        response.Headers.GetValues("Permissions-Policy").First().Should().Be("geolocation=()");
    }

    [Fact]
    public async Task ProtectedEndpoint_ContainsAllSecurityHeaders()
    {
        var response = await _client.GetAsync("/api/v1/Ventas/pedido");

        response.Headers.GetValues("X-Content-Type-Options").First().Should().Be("nosniff");
        response.Headers.GetValues("X-Frame-Options").First().Should().Be("DENY");
        response.Headers.GetValues("Referrer-Policy").First().Should().Be("no-referrer");
        response.Headers.GetValues("Permissions-Policy").First().Should().Be("geolocation=()");
    }
}
