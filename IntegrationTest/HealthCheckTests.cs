using Microsoft.AspNetCore.Mvc.Testing;
using System.Net;
using System.Threading.Tasks;
using WebAPIDevSecOps;

namespace IntegrationTest
{
    public class HealthCheckTests : IClassFixture<WebApplicationFactory<Program>>, IDisposable
    {
        private readonly WebApplicationFactory<Program> _factory;
        private bool _disposed;

        public HealthCheckTests(WebApplicationFactory<Program> factory)
        {
            _factory = factory;
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            // Null-guard para evitar NullReferenceException durante xUnit Test Class Cleanup (IClassFixture)
            try { _factory?.Dispose(); } catch { /* ignore */ }
            GC.SuppressFinalize(this);
        }

        [Fact]
        public async Task Health_Endpoint_Returns_200()
        {
            var client = _factory.WithWebHostBuilder(builder =>
            {
                builder.UseSetting("Jwt:Key", "01123581321345589144233377610987");
                builder.UseSetting("Jwt:Issuer", "edelmeza.com");
                builder.UseSetting("Jwt:Audience", "edelmeza.com");
                builder.UseSetting("UseInMemoryDatabase", "true");
                builder.UseSetting("ConnectionStrings:DefaultConnection", "Server=.;Database=Test;Trusted_Connection=True;");
            }).CreateClient();

            var response = await client.GetAsync("/health");

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        }

        [Fact]
        public async Task Health_Ready_Endpoint_Returns_200_With_DB()
        {
            var client = _factory.WithWebHostBuilder(builder =>
            {
                builder.UseSetting("Jwt:Key", "01123581321345589144233377610987");
                builder.UseSetting("Jwt:Issuer", "edelmeza.com");
                builder.UseSetting("Jwt:Audience", "edelmeza.com");
                builder.UseSetting("UseInMemoryDatabase", "true");
                builder.UseSetting("InMemoryDatabaseName", $"HealthCheckDb_{System.Guid.NewGuid():N}");
                builder.UseSetting("ConnectionStrings:DefaultConnection", "Server=.;Database=Test;Trusted_Connection=True;");
            }).CreateClient();

            var response = await client.GetAsync("/health/ready");

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        }

        [Fact]
        public async Task Health_Ready_Endpoint_Returns_503_When_DB_Fails()
        {
            var client = _factory.WithWebHostBuilder(builder =>
            {
                builder.UseSetting("Jwt:Key", "01123581321345589144233377610987");
                builder.UseSetting("Jwt:Issuer", "edelmeza.com");
                builder.UseSetting("Jwt:Audience", "edelmeza.com");
                builder.UseSetting("UseInMemoryDatabase", "false");
                builder.UseSetting("ConnectionStrings:DefaultConnection", "Server=.;Database=NonExistentDB_That_Will_Fail;Trusted_Connection=True;TrustServerCertificate=True;");
            }).CreateClient();

            var response = await client.GetAsync("/health/ready");

            Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        }
    }
}
