using System.Net;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc.Testing;
using WebAPIDevSecOps;

namespace IntegrationTest
{
    public class MetricsTests : IClassFixture<WebApplicationFactory<Program>>
    {
        private readonly WebApplicationFactory<Program> _factory;

        public MetricsTests(WebApplicationFactory<Program> factory)
        {
            _factory = factory;
        }

        [Fact]
        public async Task Metrics_Endpoint_Returns_200_With_Quality_Metrics()
        {
            var client = _factory.WithWebHostBuilder(builder =>
            {
                builder.UseSetting("Jwt:Key", "01123581321345589144233377610987");
                builder.UseSetting("Jwt:Issuer", "edelmeza.com");
                builder.UseSetting("Jwt:Audience", "edelmeza.com");
                builder.UseSetting("UseInMemoryDatabase", "true");
                builder.UseSetting("ConnectionStrings:DefaultConnection", "Server=.;Database=Test;Trusted_Connection=True;");
            }).CreateClient();

            var response = await client.GetAsync("/metrics");

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.StartsWith("text/plain", response.Content.Headers.ContentType?.ToString());

            var body = await response.Content.ReadAsStringAsync();
            Assert.Contains("test_coverage_percent", body);
            Assert.Contains("mutation_score", body);
            Assert.Contains("sonar_quality_gate_passed", body);
            Assert.Contains("p95_latency_ms", body);
        }
    }
}
