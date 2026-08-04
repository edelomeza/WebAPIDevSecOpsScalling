using System.Security.Cryptography;
using System.Text.Json;
using NBomber.Contracts;
using NBomber.CSharp;
using NBomber.Http.CSharp;
using WebAPIDevSecOps.Dto;

namespace PerformanceTest.Scenarios
{
    public static class MixtoScenario
    {
        public const double AvgResponseTimeThresholdMs = 800;
        public const double ErrorRateThreshold = 0.005;
        public const int Users = 80;
        public static readonly TimeSpan Duration = TimeSpan.FromMinutes(3);

        private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNamingPolicy = null };
        private static string? _bearerToken;

        public static ScenarioProps Build(HttpClient client, string baseUrl, string user, string password, int idCliCliente, int idSegUsuario)
        {
            var baseUrlClean = baseUrl.TrimEnd('/');
            var loginUrl = $"{baseUrlClean}/api/v1/login/login";
            var productosUrl = $"{baseUrlClean}/api/v1/producto";
            var ventaUrl = $"{baseUrlClean}/api/v1/venta";

            var scenario = Scenario.Create("mixto_scenario", async context =>
            {
                var roll = RandomNumberGenerator.GetInt32(100);

                if (roll < 60)
                {
                    var getProductos = Http.CreateRequest("GET", productosUrl)
                        .WithHeader("Authorization", $"Bearer {_bearerToken}");

                    return await Http.Send(client, getProductos);
                }

                if (roll < 80)
                {
                    var postLogin = Http.CreateRequest("POST", loginUrl)
                        .WithJsonBody(new LoginRequest(user, password), JsonOptions);

                    return await Http.Send(client, postLogin);
                }

                var postVenta = Http.CreateRequest("POST", ventaUrl)
                    .WithHeader("Authorization", $"Bearer {_bearerToken}")
                    .WithJsonBody(new VenVentaCreateDto
                    {
                        idCliCliente = idCliCliente,
                        idSegUsuario = idSegUsuario
                    }, JsonOptions);

                return await Http.Send(client, postVenta);
            });

            return Scenario.WithLoadSimulations(
                Scenario.WithInit(scenario, async initContext =>
                {
                    _bearerToken = await AuthHelper.LoginAndGetTokenAsync(client, loginUrl, user, password);
                    Console.WriteLine($"[MixtoScenario] Token JWT obtenido para {user}");
                }),
                Simulation.KeepConstant(Env.Int("PERF_MIXTO_USERS", Users), Duration));
        }
    }
}
