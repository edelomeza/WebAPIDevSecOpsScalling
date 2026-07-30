using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using UnitTest.Common;
using WebAPIDevSecOps.Dto;

namespace IntegrationTest;

public class AuthorizationTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _factory;
    private readonly HttpClient _client;

    private static readonly string UserAToken = TokenHelper.GenerateValidToken(
        JwtTestConfig.Key, JwtTestConfig.Issuer, JwtTestConfig.Audience,
        role: "User", sub: "usuarioA");

    private static readonly string UserBToken = TokenHelper.GenerateValidToken(
        JwtTestConfig.Key, JwtTestConfig.Issuer, JwtTestConfig.Audience,
        role: "User", sub: "usuarioB");

    public AuthorizationTests(WebApplicationFactory<Program> factory)
    {
        _factory = factory.WithWebHostBuilder(builder =>
        {
            builder.UseSetting("ConnectionStrings:DefaultConnection", "Server=.;Database=Test;Trusted_Connection=True;");
            builder.UseSetting("Jwt:Key", JwtTestConfig.Key);
            builder.UseSetting("Jwt:Issuer", JwtTestConfig.Issuer);
            builder.UseSetting("Jwt:Audience", JwtTestConfig.Audience);
            builder.UseSetting("UseInMemoryDatabase", "true");
            builder.UseSetting("InMemoryDatabaseName", $"IntegrationTest_Auth_{Guid.NewGuid():N}");
        });
        _client = _factory.CreateClient();
    }

    [Fact]
    public async Task UsuarioA_NoPuedeVer_ClienteDeUsuarioB()
    {
        var createDto = TestDataFactory.CreateClienteCreateDto(
            nombre: "clientea",
            correo: "clientea@test.com",
            telefono: "5512345678");

        var createRequest = new HttpRequestMessage(HttpMethod.Post, "/api/v1/cliente")
        {
            Content = JsonContent.Create(createDto)
        };
        createRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", UserAToken);
        var createResponse = await _client.SendAsync(createRequest);
        createResponse.StatusCode.Should().Be(HttpStatusCode.Created);
        var createdCliente = await createResponse.Content.ReadFromJsonAsync<CliClienteDto>();
        createdCliente.Should().NotBeNull();

        var getRequest = new HttpRequestMessage(HttpMethod.Get, $"/api/v1/cliente/{createdCliente!.id}");
        getRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", UserBToken);
        var getResponse = await _client.SendAsync(getRequest);

        getResponse.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task UsuarioA_NoPuedeModificar_ProductoDeUsuarioB()
    {
        var createDto = TestDataFactory.CreateProductoCreateDto(
            nombre: "productob",
            existencia: 10,
            precio: 100m);

        var createRequest = new HttpRequestMessage(HttpMethod.Post, "/api/v1/producto")
        {
            Content = JsonContent.Create(createDto)
        };
        createRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", UserBToken);
        var createResponse = await _client.SendAsync(createRequest);
        createResponse.StatusCode.Should().Be(HttpStatusCode.Created);
        var createdProducto = await createResponse.Content.ReadFromJsonAsync<ProProductoDto>();
        createdProducto.Should().NotBeNull();

        var updateDto = new
        {
            id = createdProducto!.id,
            strNombreProducto = "modificadopora",
            intNumeroExistencia = 5,
            decPrecio = 200m,
            rowVersion = createdProducto.RowVersion
        };

        var updateRequest = new HttpRequestMessage(HttpMethod.Put, $"/api/v1/producto/{createdProducto.id}")
        {
            Content = JsonContent.Create(updateDto)
        };
        updateRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", UserAToken);
        var updateResponse = await _client.SendAsync(updateRequest);

        updateResponse.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Admin_PuedeVerClienteDeOtroUsuario()
    {
        var createDto = TestDataFactory.CreateClienteCreateDto(
            nombre: "clienteadmin",
            correo: "clienteadmin@test.com",
            telefono: "5512345678");

        var createRequest = new HttpRequestMessage(HttpMethod.Post, "/api/v1/cliente")
        {
            Content = JsonContent.Create(createDto)
        };
        createRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", UserAToken);
        var createResponse = await _client.SendAsync(createRequest);
        createResponse.StatusCode.Should().Be(HttpStatusCode.Created);
        var createdCliente = await createResponse.Content.ReadFromJsonAsync<CliClienteDto>();

        var getRequest = new HttpRequestMessage(HttpMethod.Get, $"/api/v1/cliente/{createdCliente!.id}");
        getRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", JwtTestConfig.AdminToken);
        var getResponse = await _client.SendAsync(getRequest);

        getResponse.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Admin_PuedeModificarProductoDeOtroUsuario()
    {
        var createDto = TestDataFactory.CreateProductoCreateDto(
            nombre: "productoadmin",
            existencia: 20,
            precio: 250m);

        var createRequest = new HttpRequestMessage(HttpMethod.Post, "/api/v1/producto")
        {
            Content = JsonContent.Create(createDto)
        };
        createRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", UserBToken);
        var createResponse = await _client.SendAsync(createRequest);
        createResponse.StatusCode.Should().Be(HttpStatusCode.Created);
        var createdProducto = await createResponse.Content.ReadFromJsonAsync<ProProductoDto>();

        var updateDto = new
        {
            id = createdProducto!.id,
            strNombreProducto = "modificadoadmin",
            intNumeroExistencia = 15,
            decPrecio = 300m,
            rowVersion = createdProducto.RowVersion
        };

        var updateRequest = new HttpRequestMessage(HttpMethod.Put, $"/api/v1/producto/{createdProducto.id}")
        {
            Content = JsonContent.Create(updateDto)
        };
        updateRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", JwtTestConfig.AdminToken);
        var updateResponse = await _client.SendAsync(updateRequest);

        updateResponse.StatusCode.Should().Be(HttpStatusCode.NoContent);
    }
}
