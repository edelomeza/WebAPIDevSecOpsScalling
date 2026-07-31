using System.Net;
using System.Text.RegularExpressions;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using UnitTest.Common;

namespace IntegrationTest.Middleware;

public class CspNonceIntegrationTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _factory;
    private readonly HttpClient _client;

    public CspNonceIntegrationTests(WebApplicationFactory<Program> factory)
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
    public async Task HealthEndpoint_ContainsContentSecurityPolicy()
    {
        var response = await _client.GetAsync("/health");

        response.Headers.Should().ContainKey("Content-Security-Policy");
    }

    [Fact]
    public async Task HealthEndpoint_CspContainsNonce()
    {
        var response = await _client.GetAsync("/health");

        var csp = response.Headers.GetValues("Content-Security-Policy").First();
        csp.Should().Contain("default-src 'self'");
        csp.Should().Contain("script-src 'self' 'nonce-");
        csp.Should().Contain("style-src 'self' 'unsafe-inline'");
        csp.Should().Contain("img-src 'self' data:");
        csp.Should().Contain("font-src 'self' data:");
        csp.Should().Contain("connect-src 'self'");
    }

    [Fact]
    public async Task HealthEndpoint_CspNonceIsValidBase64()
    {
        var response = await _client.GetAsync("/health");

        var csp = response.Headers.GetValues("Content-Security-Policy").First();
        var nonceMatch = Regex.Match(csp, @"'nonce-([^']+)'");
        nonceMatch.Success.Should().BeTrue();
        var nonceBytes = Convert.FromBase64String(nonceMatch.Groups[1].Value);
        nonceBytes.Should().HaveCount(32);
    }

    [Fact]
    public async Task NonExistentEndpoint_ContainsCsp()
    {
        var response = await _client.GetAsync("/nonexistent-route");

        response.Headers.Should().ContainKey("Content-Security-Policy");
    }

    [Fact]
    public async Task ProtectedEndpoint_ContainsCsp()
    {
        var response = await _client.GetAsync("/api/v1/Ventas/pedido");

        response.Headers.Should().ContainKey("Content-Security-Policy");
    }

    [Fact]
    public async Task EachResponse_HasUniqueNonce()
    {
        var nonces = new List<string>();
        for (int i = 0; i < 3; i++)
        {
            var response = await _client.GetAsync("/health");
            var csp = response.Headers.GetValues("Content-Security-Policy").First();
        var nonceMatch = Regex.Match(csp, @"'nonce-([^']+)'");
            nonces.Add(nonceMatch.Groups[1].Value);
        }

        nonces.Distinct().Should().HaveCount(3);
    }

    [Fact]
    public async Task ContentSecurityPolicyIsNotDuplicated()
    {
        var response = await _client.GetAsync("/health");

        var cspValues = response.Headers.GetValues("Content-Security-Policy").ToList();
        cspValues.Should().ContainSingle();
    }
}
