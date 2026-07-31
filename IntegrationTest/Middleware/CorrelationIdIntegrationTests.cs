using System.Net;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using UnitTest.Common;

namespace IntegrationTest.Middleware;

public class CorrelationIdIntegrationTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _factory;
    private readonly HttpClient _client;

    public CorrelationIdIntegrationTests(WebApplicationFactory<Program> factory)
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
    public async Task Request_WithoutCorrelationId_ResponseContainsGeneratedCorrelationId()
    {
        var response = await _client.GetAsync("/health");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        response.Headers.Should().ContainKey("X-Correlation-Id");
        var correlationId = response.Headers.GetValues("X-Correlation-Id").First();
        Guid.TryParse(correlationId, out _).Should().BeTrue();
    }

    [Fact]
    public async Task Request_WithCorrelationId_ResponseContainsSameCorrelationId()
    {
        var expectedId = "test-correlation-id-456";

        var request = new HttpRequestMessage(HttpMethod.Get, "/health");
        request.Headers.Add("X-Correlation-Id", expectedId);
        var response = await _client.SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        response.Headers.Should().ContainKey("X-Correlation-Id");
        var correlationId = response.Headers.GetValues("X-Correlation-Id").First();
        correlationId.Should().Be(expectedId);
    }

    [Fact]
    public async Task Request_WithEmptyCorrelationId_GeneratesNewOne()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "/health");
        request.Headers.Add("X-Correlation-Id", "");
        var response = await _client.SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        response.Headers.Should().ContainKey("X-Correlation-Id");
        var correlationId = response.Headers.GetValues("X-Correlation-Id").First();
        Guid.TryParse(correlationId, out _).Should().BeTrue();
    }

    [Fact]
    public async Task MultipleRequests_EachGetUniqueCorrelationId()
    {
        var ids = new List<string>();
        for (int i = 0; i < 5; i++)
        {
            var response = await _client.GetAsync("/health");
            ids.Add(response.Headers.GetValues("X-Correlation-Id").First());
        }

        ids.Distinct().Should().HaveCount(5);
    }

    [Fact]
    public async Task Request_ToProtectedEndpoint_StillReturnsCorrelationId()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "/api/v1/Ventas/pedido");
        var response = await _client.SendAsync(request);

        response.Headers.Should().ContainKey("X-Correlation-Id");
        var correlationId = response.Headers.GetValues("X-Correlation-Id").First();
        Guid.TryParse(correlationId, out _).Should().BeTrue();
    }

    [Fact]
    public async Task Request_ToNonExistentEndpoint_StillReturnsCorrelationId()
    {
        var response = await _client.GetAsync("/nonexistent-route");

        response.Headers.Should().ContainKey("X-Correlation-Id");
        var correlationId = response.Headers.GetValues("X-Correlation-Id").First();
        Guid.TryParse(correlationId, out _).Should().BeTrue();
    }
}
