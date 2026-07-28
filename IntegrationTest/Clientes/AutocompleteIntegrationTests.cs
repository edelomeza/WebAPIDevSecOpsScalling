using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using UnitTest.Common;
using WebAPIDevSecOps.Dto;
using WebAPIDevSecOps.Services;

namespace IntegrationTest.Clientes;

public class AutocompleteIntegrationTests : IClassFixture<WebApplicationFactory<Program>>, IAsyncLifetime
{
    private readonly WebApplicationFactory<Program> _factory;
    private readonly HttpClient _client;

    public AutocompleteIntegrationTests(WebApplicationFactory<Program> factory)
    {
        _factory = factory.WithWebHostBuilder(builder =>
        {
            builder.UseSetting("ConnectionStrings:DefaultConnection", "Server=.;Database=Test;Trusted_Connection=True;");
            builder.UseSetting("Jwt:Key", JwtTestConfig.Key);
            builder.UseSetting("Jwt:Issuer", JwtTestConfig.Issuer);
            builder.UseSetting("Jwt:Audience", JwtTestConfig.Audience);
            builder.UseSetting("UseInMemoryDatabase", "true");
            builder.UseSetting("InMemoryDatabaseName", $"IntegrationTestDb_CliAuto_{Guid.NewGuid():N}");
        });
        _client = _factory.CreateClient();
    }

    public Task InitializeAsync()
    {
        return Task.CompletedTask;
    }

    public Task DisposeAsync() => Task.CompletedTask;

    private string AdminToken => JwtTestConfig.AdminToken;

    private async Task SeedClientesAsync(params string[] nombres)
    {
        foreach (var nombre in nombres)
        {
            var dto = TestDataFactory.CreateClienteCreateDto(
                nombre: nombre,
                correo: $"{nombre.ToLowerInvariant()}@test.com",
                telefono: "5512345678");

            var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/cliente")
            {
                Content = JsonContent.Create(dto)
            };
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", AdminToken);
            var response = await _client.SendAsync(request);
            response.EnsureSuccessStatusCode();
        }
    }

    [Fact]
    public async Task Autocomplete_ReturnsMatchingResults()
    {
        await SeedClientesAsync("Juan Perez", "Juan Ramirez", "Maria Lopez");

        var request = new HttpRequestMessage(HttpMethod.Get, "/api/v1/cliente/autocomplete?texto=Juan");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", AdminToken);
        var response = await _client.SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var items = await response.Content.ReadFromJsonAsync<List<CliClienteAutocompleteDto>>();
        items.Should().HaveCount(2);
        items.Should().OnlyContain(i => i.strNombreCliente.StartsWith("Juan"));
    }

    [Fact]
    public async Task Autocomplete_ReturnsEmpty_WhenNoMatch()
    {
        await SeedClientesAsync("Juan Perez");

        var request = new HttpRequestMessage(HttpMethod.Get, "/api/v1/cliente/autocomplete?texto=Zzzz");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", AdminToken);
        var response = await _client.SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var items = await response.Content.ReadFromJsonAsync<List<CliClienteAutocompleteDto>>();
        items.Should().BeEmpty();
    }

    [Fact]
    public async Task Autocomplete_ReturnsLimitedResults()
    {
        await SeedClientesAsync("Ana Lopez", "Ana Martinez", "Ana Garcia", "Ana Torres", "Ana Ruiz");

        var request = new HttpRequestMessage(HttpMethod.Get, "/api/v1/cliente/autocomplete?texto=Ana&maxResultados=3");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", AdminToken);
        var response = await _client.SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var items = await response.Content.ReadFromJsonAsync<List<CliClienteAutocompleteDto>>();
        items.Should().HaveCount(3);
    }

    [Fact]
    public async Task Autocomplete_WithoutAuth_Returns401()
    {
        var response = await _client.GetAsync("/api/v1/cliente/autocomplete?texto=Juan");
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Autocomplete_WithEmptyTexto_Returns400()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "/api/v1/cliente/autocomplete?texto=");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", AdminToken);
        var response = await _client.SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Autocomplete_ReturnsOrderedResults()
    {
        await SeedClientesAsync("Zara Uno", "Ana Dos");

        var request = new HttpRequestMessage(HttpMethod.Get, "/api/v1/cliente/autocomplete?texto=a");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", AdminToken);
        var response = await _client.SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var items = await response.Content.ReadFromJsonAsync<List<CliClienteAutocompleteDto>>();
        items.Should().HaveCount(2);
        items[0].strNombreCliente.Should().Be("Ana Dos");
        items[1].strNombreCliente.Should().Be("Zara Uno");
    }
}
