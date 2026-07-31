using Microsoft.AspNetCore.Mvc.Testing;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using WebAPIDevSecOps.Dto;
using WebAPIDevSecOps.Services;
using UnitTest.Common;

namespace SecurityTest.Usuarios
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
                builder.UseSetting("InMemoryDatabaseName", $"SecurityTestDb_{Guid.NewGuid():N}");
            });
            _client = _factory.CreateClient();
        }

        public Task InitializeAsync()
        {
            return Task.CompletedTask;
        }

        public Task DisposeAsync() => Task.CompletedTask;

        private string AdminToken => TokenHelper.GenerateValidToken(JwtKey, JwtIssuer, JwtAudience);

        private async Task<SegUsuarioDto?> CreateUserAsync(string uniqueName)
        {
            var dto = TestDataFactory.CreateUsuarioCreateDto(
                nombre: uniqueName,
                password: "Test@1234",
                correo: $"{uniqueName}@test.com");

            var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/usuario")
            {
                Content = JsonContent.Create(dto)
            };
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", AdminToken);

            var response = await _client.SendAsync(request);
            if (!response.IsSuccessStatusCode)
            {
                var body = await response.Content.ReadAsStringAsync();
                throw new InvalidOperationException($"Create user failed: {(int)response.StatusCode} {response.ReasonPhrase} - {body}");
            }

            return await response.Content.ReadFromJsonAsync<SegUsuarioDto>();
        }

        // 1 — SIN AUTENTICACIÓN
        [Fact]
        public async Task Should_Reject_Request_Without_Token()
        {
            var getResponse = await _client.GetAsync("/api/v1/usuario");
            Assert.Equal(HttpStatusCode.Unauthorized, getResponse.StatusCode);

            var postResponse = await _client.PostAsJsonAsync("/api/v1/usuario", new { });
            Assert.Equal(HttpStatusCode.Unauthorized, postResponse.StatusCode);

            var putRequest = new HttpRequestMessage(HttpMethod.Put, "/api/v1/usuario/1")
            {
                Content = JsonContent.Create(new { })
            };
            var putResponse = await _client.SendAsync(putRequest);
            Assert.Equal(HttpStatusCode.Unauthorized, putResponse.StatusCode);

            var deleteResponse = await _client.DeleteAsync("/api/v1/usuario/1");
            Assert.Equal(HttpStatusCode.Unauthorized, deleteResponse.StatusCode);
        }

        // 2 — ROL INCORRECTO
        [Fact]
        public async Task Should_Reject_Token_With_Wrong_Role()
        {
            var userToken = TokenHelper.GenerateTokenWithRole(JwtKey, JwtIssuer, JwtAudience, "User");

            var request = new HttpRequestMessage(HttpMethod.Get, "/api/v1/usuario");
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", userToken);

            var response = await _client.SendAsync(request);
            Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        }

        // 3 — TOKEN EXPIRADO
        [Fact]
        public async Task Should_Reject_Expired_Token()
        {
            var expiredToken = TokenHelper.GenerateExpiredToken(JwtKey, JwtIssuer, JwtAudience);

            var request = new HttpRequestMessage(HttpMethod.Get, "/api/v1/usuario");
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", expiredToken);

            var response = await _client.SendAsync(request);
            Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        }

        // 4 — TOKEN EN BLACKLIST
        [Fact]
        public async Task Should_Reject_Blacklisted_Token()
        {
            var token = await UnitTest.Common.BlacklistHelper.GenerateAndBlacklistTokenAsync(_factory.Services, "01123581321345589144233377610987", "edelmeza.com", "edelmeza.com");

            var request = new HttpRequestMessage(HttpMethod.Get, "/api/v1/usuario");
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

            var request = new HttpRequestMessage(HttpMethod.Get, "/api/v1/usuario");
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

            var request = new HttpRequestMessage(HttpMethod.Get, "/api/v1/usuario");
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", fakeToken);

            var response = await _client.SendAsync(request);
            Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        }

        // 7 — INYECCIÓN SQL
        [Fact]
        public async Task Should_Reject_SQL_Injection_In_Nombre()
        {
            var dto = TestDataFactory.CreateUsuarioCreateDto(
                nombre: "'; DROP TABLE SegUsuario; --",
                password: "Test@1234",
                correo: "inject@test.com");

            var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/usuario")
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
            var dto = TestDataFactory.CreateUsuarioCreateDto(
                nombre: "<script>alert('xss')</script>",
                password: "Test@1234",
                correo: "xss@test.com");

            var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/usuario")
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
            var dto = TestDataFactory.CreateUsuarioCreateDto(
                nombre: "",
                password: "Test@1234",
                correo: "empty@test.com");

            var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/usuario")
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
            var dto = TestDataFactory.CreateUsuarioCreateDto(
                nombre: new string('a', 51),
                password: "Test@1234",
                correo: "long@test.com");

            var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/usuario")
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

            var getRequest = new HttpRequestMessage(HttpMethod.Get, "/api/v1/usuario/-1");
            getRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

            var getResponse = await _client.SendAsync(getRequest);
            Assert.Equal(HttpStatusCode.NotFound, getResponse.StatusCode);
        }

        // 12 — CONCURRENCIA (ROWVERSION DESACTUALIZADA)
        [Fact]
        public async Task Should_Reject_Stale_RowVersion()
        {
            var user = await CreateUserAsync("conflict_test");
            Assert.NotNull(user);

            var request = new HttpRequestMessage(HttpMethod.Put, $"/api/v1/usuario/{user.id}")
            {
                Content = JsonContent.Create(new
                {
                    id = user.id,
                    strNombre = "conflict_test_updated",
                    strCorreoElectronico = "conflict_test_updated@test.com",
                    rowVersion = new byte[] { 2, 0, 0, 0 }
                })
            };
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", AdminToken);

            var response = await _client.SendAsync(request);
            Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        }

        // 13 — NOMBRE DE USUARIO DUPLICADO
        [Fact]
        public async Task Should_Reject_Duplicate_Username()
        {
            var user = await CreateUserAsync("duplicate_test");
            Assert.NotNull(user);

            var dto = TestDataFactory.CreateUsuarioCreateDto(
                nombre: "duplicate_test",
                password: "Test@1234",
                correo: "duplicate_other@test.com");

            var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/usuario")
            {
                Content = JsonContent.Create(dto)
            };
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", AdminToken);

            var response = await _client.SendAsync(request);
            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        }

        // 14 — USUARIO INEXISTENTE
        [Fact]
        public async Task Should_Reject_NonExistent_User()
        {
            var token = AdminToken;

            var getRequest = new HttpRequestMessage(HttpMethod.Get, "/api/v1/usuario/9999");
            getRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

            var getResponse = await _client.SendAsync(getRequest);
            Assert.Equal(HttpStatusCode.NotFound, getResponse.StatusCode);

            var deleteRequest = new HttpRequestMessage(HttpMethod.Delete, "/api/v1/usuario/9999")
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
            var response = await _client.GetAsync("/api/v1/usuario");

            Assert.True(response.Headers.Contains("X-Content-Type-Options"));
            Assert.True(response.Headers.Contains("X-Frame-Options"));
            Assert.True(response.Headers.Contains("X-XSS-Protection"));
            Assert.False(response.Headers.Contains("Strict-Transport-Security"));
        }

        // 16 — CACHE CONTROL
        [Fact]
        public async Task Should_Not_Cache_Authenticated_Responses()
        {
            var request = new HttpRequestMessage(HttpMethod.Get, "/api/v1/usuario");
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", AdminToken);

            var response = await _client.SendAsync(request);

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.Contains("no-store", response.Headers.CacheControl?.ToString() ?? "");
        }

        // 17 — BUSCAR SIN TEXTO
        [Fact]
        public async Task Should_Reject_Search_Without_Texto()
        {
            var request = new HttpRequestMessage(HttpMethod.Get, "/api/v1/usuario/buscar");
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", AdminToken);

            var response = await _client.SendAsync(request);

            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        }

        // 18 — BUSCAR CON TEXTO VACÍO
        [Fact]
        public async Task Should_Reject_Search_With_Empty_Texto()
        {
            var request = new HttpRequestMessage(HttpMethod.Get, "/api/v1/usuario/buscar?texto=");
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", AdminToken);

            var response = await _client.SendAsync(request);

            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        }

        // 19 — AUTOCOMPLETE SIN TEXTO
        [Fact]
        public async Task Should_Reject_Autocomplete_With_Empty_Texto()
        {
            var request = new HttpRequestMessage(HttpMethod.Get, "/api/v1/usuario/autocomplete?texto=");
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", AdminToken);

            var response = await _client.SendAsync(request);

            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        }

        // 20 — AUTOCOMPLETE SIN AUTENTICACIÓN
        [Fact]
        public async Task Should_Reject_Autocomplete_Without_Token()
        {
            var response = await _client.GetAsync("/api/v1/usuario/autocomplete?texto=test");

            Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        }

        // 21 — AUTOCOMPLETE CON ROL NO ADMIN
        [Fact]
        public async Task Should_Reject_Autocomplete_With_NonAdmin_Token()
        {
            var userToken = TokenHelper.GenerateTokenWithRole(JwtKey, JwtIssuer, JwtAudience, "User");

            var request = new HttpRequestMessage(HttpMethod.Get, "/api/v1/usuario/autocomplete?texto=test");
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", userToken);

            var response = await _client.SendAsync(request);

            Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        }

        // 19 — BUSCAR CON ROL NO ADMIN
        [Fact]
        public async Task Should_Reject_Search_With_NonAdmin_Token()
        {
            var userToken = TokenHelper.GenerateTokenWithRole(JwtKey, JwtIssuer, JwtAudience, "User");

            var request = new HttpRequestMessage(HttpMethod.Get, "/api/v1/usuario/buscar?texto=test");
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", userToken);

            var response = await _client.SendAsync(request);

            Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        }
    }
}
