using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using UnitTest.Common;
using WebAPIDevSecOps.Context;
using WebAPIDevSecOps.Dto;
using WebAPIDevSecOps.Models;
using WebAPIDevSecOps.Services;

namespace IntegrationTest.EstadoVenta;

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
            builder.UseSetting("InMemoryDatabaseName", $"EstadoVentaTestDb_{Guid.NewGuid():N}");
        });
        _client = _factory.CreateClient();
    }

    public async Task InitializeAsync()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        db.VenCatEstado.RemoveRange(db.VenCatEstado);
        await db.SaveChangesAsync();
    }

    public Task DisposeAsync() => Task.CompletedTask;

    private string AdminToken => JwtTestConfig.AdminToken;

    private async Task<VenCatEstadoDto> SeedEstadoAsync(string valor, string? descripcion = null)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var estado = new VenCatEstado { strValor = valor, strDescripcion = descripcion };
        db.VenCatEstado.Add(estado);
        await db.SaveChangesAsync();
        return new VenCatEstadoDto { id = estado.id, strValor = estado.strValor, strDescripcion = estado.strDescripcion };
    }

    [Fact]
    public async Task GetAll_EmptyDatabase_ReturnsEmptyPagedResult()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "/api/v1/estadoventa");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", AdminToken);

        var response = await _client.SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var result = await response.Content.ReadFromJsonAsync<PagedResult<VenCatEstadoDto>>();
        result!.Items.Should().BeEmpty();
        result.TotalCount.Should().Be(0);
    }

    [Fact]
    public async Task GetAll_WithData_ReturnsItems()
    {
        await SeedEstadoAsync("Activo", "Estado activo");
        await SeedEstadoAsync("Inactivo", "Estado inactivo");

        var request = new HttpRequestMessage(HttpMethod.Get, "/api/v1/estadoventa");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", AdminToken);

        var response = await _client.SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var result = await response.Content.ReadFromJsonAsync<PagedResult<VenCatEstadoDto>>();
        result!.Items.Should().HaveCount(2);
        result.TotalCount.Should().Be(2);
    }

    [Fact]
    public async Task GetAll_Pagination_Works()
    {
        for (int i = 0; i < 5; i++)
            await SeedEstadoAsync($"Estado{i}", $"Desc{i}");

        var request = new HttpRequestMessage(HttpMethod.Get, "/api/v1/estadoventa?pageSize=2&pageNumber=2");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", AdminToken);

        var response = await _client.SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var result = await response.Content.ReadFromJsonAsync<PagedResult<VenCatEstadoDto>>();
        result!.Items.Should().HaveCount(2);
        result.PageNumber.Should().Be(2);
        result.PageSize.Should().Be(2);
    }

    [Fact]
    public async Task GetAll_NoStoreCacheHeader()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "/api/v1/estadoventa");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", AdminToken);

        var response = await _client.SendAsync(request);

        response.Headers.CacheControl.Should().NotBeNull();
        response.Headers.CacheControl!.NoStore.Should().BeTrue();
    }

    [Fact]
    public async Task GetById_Existing_ReturnsDto()
    {
        var estado = await SeedEstadoAsync("Activo", "Estado activo");

        var request = new HttpRequestMessage(HttpMethod.Get, $"/api/v1/estadoventa/{estado.id}");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", AdminToken);

        var response = await _client.SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var dto = await response.Content.ReadFromJsonAsync<VenCatEstadoDto>();
        dto!.strValor.Should().Be("Activo");
    }

    [Fact]
    public async Task GetById_NonExistent_ReturnsNotFound()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "/api/v1/estadoventa/9999");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", AdminToken);

        var response = await _client.SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task GetById_NegativeId_ReturnsNotFound()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "/api/v1/estadoventa/-1");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", AdminToken);

        var response = await _client.SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task GetById_ZeroId_ReturnsNotFound()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "/api/v1/estadoventa/0");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", AdminToken);

        var response = await _client.SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }
}
