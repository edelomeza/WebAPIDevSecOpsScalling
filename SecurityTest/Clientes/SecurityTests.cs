using Microsoft.AspNetCore.Mvc.Testing;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using WebAPIDevSecOps.Dto;
using WebAPIDevSecOps.Services;
using UnitTest.Common;

namespace SecurityTest.Clientes
{
    public class SecurityTests : IClassFixture<WebApplicationFactory<Program>>, IAsyncLifetime
    {
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
                builder.UseSetting("InMemoryDatabaseName", $"SecurityTestDb_Cli_{Guid.NewGuid():N}");
            });
            _client = _factory.CreateClient();
        }

        public Task InitializeAsync()
        {
            return Task.CompletedTask;
        }

        public Task DisposeAsync() => Task.CompletedTask;

        private string AdminToken => TokenHelper.GenerateValidToken(JwtKey, JwtIssuer, JwtAudience);

        private async Task<CliClienteDto?> CreateClienteAsync(string uniqueName)
        {
            var safeName = uniqueName.Replace("_", "");
            var dto = TestDataFactory.CreateClienteCreateDto(
                nombre: safeName,
                correo: $"{safeName}@test.com",
                telefono: "5512345678");

            var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/cliente")
            {
                Content = JsonContent.Create(dto)
            };
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", AdminToken);

            var response = await _client.SendAsync(request);
            if (!response.IsSuccessStatusCode)
            {
                var body = await response.Content.ReadAsStringAsync();
                throw new InvalidOperationException($"Create cliente failed: {(int)response.StatusCode} {response.ReasonPhrase} - {body}");
            }

            return await response.Content.ReadFromJsonAsync<CliClienteDto>();
        }

        // 1 — SIN AUTENTICACIÓN
        [Fact]
        public async Task Should_Reject_Request_Without_Token()
        {
            var getResponse = await _client.GetAsync("/api/v1/cliente");
            Assert.Equal(HttpStatusCode.Unauthorized, getResponse.StatusCode);

            var postResponse = await _client.PostAsJsonAsync("/api/v1/cliente", new { });
            Assert.Equal(HttpStatusCode.Unauthorized, postResponse.StatusCode);

            var putRequest = new HttpRequestMessage(HttpMethod.Put, "/api/v1/cliente/1")
            {
                Content = JsonContent.Create(new { })
            };
            var putResponse = await _client.SendAsync(putRequest);
            Assert.Equal(HttpStatusCode.Unauthorized, putResponse.StatusCode);

            var deleteResponse = await _client.DeleteAsync("/api/v1/cliente/1");
            Assert.Equal(HttpStatusCode.Unauthorized, deleteResponse.StatusCode);

            var autoResponse = await _client.GetAsync("/api/v1/cliente/autocomplete?texto=test");
            Assert.Equal(HttpStatusCode.Unauthorized, autoResponse.StatusCode);
        }

        // 2 — ROL INCORRECTO
        [Fact]
        public async Task Should_Reject_Token_With_Wrong_Role()
        {
            var userToken = TokenHelper.GenerateTokenWithRole(JwtKey, JwtIssuer, JwtAudience, "User");

            var request = new HttpRequestMessage(HttpMethod.Get, "/api/v1/cliente");
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", userToken);

            var response = await _client.SendAsync(request);
            Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        }

        // 3 — TOKEN EXPIRADO
        [Fact]
        public async Task Should_Reject_Expired_Token()
        {
            var expiredToken = TokenHelper.GenerateExpiredToken(JwtKey, JwtIssuer, JwtAudience);

            var request = new HttpRequestMessage(HttpMethod.Get, "/api/v1/cliente");
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", expiredToken);

            var response = await _client.SendAsync(request);
            Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        }

        // 4 — TOKEN EN BLACKLIST
        [Fact]
        public async Task Should_Reject_Blacklisted_Token()
        {
            var token = await UnitTest.Common.BlacklistHelper.GenerateAndBlacklistTokenAsync(_factory.Services, "01123581321345589144233377610987", "edelmeza.com", "edelmeza.com");

            var request = new HttpRequestMessage(HttpMethod.Get, "/api/v1/cliente");
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

            var response = await _client.SendAsync(request);
            Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        }

        // 5 — TOKEN ALTERADO
        [Fact]
        public async Task Should_Reject_Tampered_Token()
        {
            var token = TokenHelper.GenerateValidToken(JwtKey, JwtIssuer, JwtAudience);
            var tamperedToken = token[..^2] + "xx";

            var request = new HttpRequestMessage(HttpMethod.Get, "/api/v1/cliente");
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", tamperedToken);

            var response = await _client.SendAsync(request);
            Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        }

        // 6 — FIRMA INVÁLIDA
        [Fact]
        public async Task Should_Reject_Invalid_Signature()
        {
            var fakeKey = "clave_falsa_super_insegura_123456789";
            var fakeToken = TokenHelper.GenerateValidToken(fakeKey, JwtIssuer, JwtAudience);

            var request = new HttpRequestMessage(HttpMethod.Get, "/api/v1/cliente");
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", fakeToken);

            var response = await _client.SendAsync(request);
            Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        }

        // 7 — INYECCIÓN SQL
        [Fact]
        public async Task Should_Reject_SQL_Injection_In_Nombre()
        {
            var dto = TestDataFactory.CreateClienteCreateDto(
                nombre: "'; DROP TABLE CliCliente; --",
                correo: "inject@test.com",
                telefono: "5512345678");

            var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/cliente")
            {
                Content = JsonContent.Create(dto)
            };
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", AdminToken);

            var response = await _client.SendAsync(request);
            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        }

        // 8 — XSS
        [Fact]
        public async Task Should_Reject_XSS_In_Nombre()
        {
            var dto = TestDataFactory.CreateClienteCreateDto(
                nombre: "<script>alert('xss')</script>",
                correo: "xss@test.com",
                telefono: "5512345678");

            var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/cliente")
            {
                Content = JsonContent.Create(dto)
            };
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", AdminToken);

            var response = await _client.SendAsync(request);
            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        }

        // 9 — NOMBRE VACÍO
        [Fact]
        public async Task Should_Reject_Empty_Nombre()
        {
            var dto = TestDataFactory.CreateClienteCreateDto(
                nombre: "",
                correo: "empty@test.com",
                telefono: "5512345678");

            var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/cliente")
            {
                Content = JsonContent.Create(dto)
            };
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", AdminToken);

            var response = await _client.SendAsync(request);
            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        }

        // 10 — NOMBRE EXCESIVAMENTE LARGO
        [Fact]
        public async Task Should_Reject_Overly_Long_Nombre()
        {
            var dto = TestDataFactory.CreateClienteCreateDto(
                nombre: new string('a', 101),
                correo: "long@test.com",
                telefono: "5512345678");

            var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/cliente")
            {
                Content = JsonContent.Create(dto)
            };
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", AdminToken);

            var response = await _client.SendAsync(request);
            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        }

        // 11 — ID NEGATIVO
        [Fact]
        public async Task Should_Reject_Negative_Id()
        {
            var token = AdminToken;

            var getRequest = new HttpRequestMessage(HttpMethod.Get, "/api/v1/cliente/-1");
            getRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

            var getResponse = await _client.SendAsync(getRequest);
            Assert.Equal(HttpStatusCode.NotFound, getResponse.StatusCode);
        }

        // 12 — CONCURRENCIA (ROWVERSION DESACTUALIZADA)
        [Fact]
        public async Task Should_Reject_Stale_RowVersion()
        {
            var cliente = await CreateClienteAsync("conflicttestcli");
            Assert.NotNull(cliente);

            var request = new HttpRequestMessage(HttpMethod.Put, $"/api/v1/cliente/{cliente.id}")
            {
                Content = JsonContent.Create(new
                {
                    id = cliente.id,
                    strNombreCliente = "conflicttestcliupdated",
                    strCorreoElectronico = "conflicttestcliupdated@test.com",
                    strNumeroTelefono = "5512345678",
                    rowVersion = new byte[] { 2, 0, 0, 0 }
                })
            };
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", AdminToken);

            var response = await _client.SendAsync(request);
            Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        }

        // 13 — CORREO DUPLICADO
        [Fact]
        public async Task Should_Reject_Duplicate_Correo()
        {
            var cliente = await CreateClienteAsync("duplicatecorreotest");
            Assert.NotNull(cliente);

            var dto = TestDataFactory.CreateClienteCreateDto(
                nombre: "duplicatecorreoother",
                correo: "duplicatecorreotest@test.com",
                telefono: "5512345678");

            var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/cliente")
            {
                Content = JsonContent.Create(dto)
            };
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", AdminToken);

            var response = await _client.SendAsync(request);
            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        }

        // 14 — CLIENTE INEXISTENTE
        [Fact]
        public async Task Should_Reject_NonExistent_Cliente()
        {
            var token = AdminToken;

            var getRequest = new HttpRequestMessage(HttpMethod.Get, "/api/v1/cliente/9999");
            getRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

            var getResponse = await _client.SendAsync(getRequest);
            Assert.Equal(HttpStatusCode.NotFound, getResponse.StatusCode);

            var deleteRequest = new HttpRequestMessage(HttpMethod.Delete, "/api/v1/cliente/9999")
            {
                Content = JsonContent.Create(new { id = 9999, rowVersion = new byte[] { 1, 0, 0, 0 } })
            };
            deleteRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

            var deleteResponse = await _client.SendAsync(deleteRequest);
            Assert.Equal(HttpStatusCode.NotFound, deleteResponse.StatusCode);
        }

        // 15 — HEADERS DE SEGURIDAD
        [Fact]
        public async Task Should_Contain_Security_Headers()
        {
            var response = await _client.GetAsync("/api/v1/cliente");

            Assert.True(response.Headers.Contains("X-Content-Type-Options"));
            Assert.True(response.Headers.Contains("X-Frame-Options"));
            Assert.True(response.Headers.Contains("X-XSS-Protection"));
            Assert.False(response.Headers.Contains("Strict-Transport-Security"));
        }

        // 16 — CACHE CONTROL
        [Fact]
        public async Task Should_Not_Cache_Authenticated_Responses()
        {
            var request = new HttpRequestMessage(HttpMethod.Get, "/api/v1/cliente");
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", AdminToken);

            var response = await _client.SendAsync(request);

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.Contains("no-store", response.Headers.CacheControl?.ToString() ?? "");
        }

        // 17 — BUSCAR SIN TEXTO
        [Fact]
        public async Task Should_Reject_Search_Without_Texto()
        {
            var request = new HttpRequestMessage(HttpMethod.Get, "/api/v1/cliente/buscar");
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", AdminToken);

            var response = await _client.SendAsync(request);

            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        }

        // 18 — BUSCAR CON TEXTO VACÍO
        [Fact]
        public async Task Should_Reject_Search_With_Empty_Texto()
        {
            var request = new HttpRequestMessage(HttpMethod.Get, "/api/v1/cliente/buscar?texto=");
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", AdminToken);

            var response = await _client.SendAsync(request);

            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        }

        // 19 — BUSCAR CON ROL NO ADMIN
        [Fact]
        public async Task Should_Reject_Search_With_NonAdmin_Token()
        {
            var userToken = TokenHelper.GenerateTokenWithRole(JwtKey, JwtIssuer, JwtAudience, "User");

            var request = new HttpRequestMessage(HttpMethod.Get, "/api/v1/cliente/buscar?texto=test");
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", userToken);

            var response = await _client.SendAsync(request);

            Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        }

        // 20 — TELÉFONO CON LETRAS
        [Fact]
        public async Task Should_Reject_Telefono_With_Letters()
        {
            var dto = TestDataFactory.CreateClienteCreateDto(
                nombre: "telefonoletters",
                correo: "telefonoletters@test.com",
                telefono: "ABCDEFGHIJ");

            var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/cliente")
            {
                Content = JsonContent.Create(dto)
            };
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", AdminToken);

            var response = await _client.SendAsync(request);
            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        }

        // 21 — TELÉFONO MENOS DE 10 DÍGITOS
        [Fact]
        public async Task Should_Reject_Telefono_Too_Short()
        {
            var dto = TestDataFactory.CreateClienteCreateDto(
                nombre: "telefonoshort",
                correo: "telefonoshort@test.com",
                telefono: "12345");

            var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/cliente")
            {
                Content = JsonContent.Create(dto)
            };
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", AdminToken);

            var response = await _client.SendAsync(request);
            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        }

        // 22 — TELÉFONO MÁS DE 10 DÍGITOS
        [Fact]
        public async Task Should_Reject_Telefono_Too_Long()
        {
            var dto = TestDataFactory.CreateClienteCreateDto(
                nombre: "telefonolong",
                correo: "telefonolong@test.com",
                telefono: "12345678901");

            var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/cliente")
            {
                Content = JsonContent.Create(dto)
            };
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", AdminToken);

            var response = await _client.SendAsync(request);
            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        }

        // 23 — AUTOCOMPLETE SIN AUTENTICACIÓN
        [Fact]
        public async Task Should_Reject_Autocomplete_Without_Token()
        {
            var response = await _client.GetAsync("/api/v1/cliente/autocomplete?texto=test");
            Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        }

        // 24 — AUTOCOMPLETE CON ROL INCORRECTO
        [Fact]
        public async Task Should_Reject_Autocomplete_With_Wrong_Role()
        {
            var userToken = TokenHelper.GenerateTokenWithRole(JwtKey, JwtIssuer, JwtAudience, "User");

            var request = new HttpRequestMessage(HttpMethod.Get, "/api/v1/cliente/autocomplete?texto=test");
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", userToken);

            var response = await _client.SendAsync(request);
            Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        }

        // 25 — AUTOCOMPLETE CON TEXTO VACÍO
        [Fact]
        public async Task Should_Reject_Autocomplete_With_Empty_Texto()
        {
            var request = new HttpRequestMessage(HttpMethod.Get, "/api/v1/cliente/autocomplete?texto=");
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", AdminToken);

            var response = await _client.SendAsync(request);
            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        }

        // 26 — AUTOCOMPLETE RETORNA 200 CON TOKEN VÁLIDO
        [Fact]
        public async Task Should_Accept_Autocomplete_With_Valid_Token()
        {
            var request = new HttpRequestMessage(HttpMethod.Get, "/api/v1/cliente/autocomplete?texto=test");
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", AdminToken);

            var response = await _client.SendAsync(request);
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        }

        // 27 — AUTOCOMPLETE RETORNA LISTA PLANA
        [Fact]
        public async Task Autocomplete_Returns_Flat_List()
        {
            var dto = TestDataFactory.CreateClienteCreateDto(
                nombre: "AutocompleteTest",
                correo: "autocompletetest@test.com",
                telefono: "5512345678");

            var createRequest = new HttpRequestMessage(HttpMethod.Post, "/api/v1/cliente")
            {
                Content = JsonContent.Create(dto)
            };
            createRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", AdminToken);
            var createResponse = await _client.SendAsync(createRequest);
            createResponse.EnsureSuccessStatusCode();

            var request = new HttpRequestMessage(HttpMethod.Get, "/api/v1/cliente/autocomplete?texto=Auto");
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", AdminToken);

            var response = await _client.SendAsync(request);
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);

            var body = await response.Content.ReadAsStringAsync();
            Assert.StartsWith("[", body.Trim());
            Assert.Contains("AutocompleteTest", body);
        }
        
    }
}
