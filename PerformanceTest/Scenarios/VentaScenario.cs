using System.Text.Json;
using NBomber.Contracts;
using NBomber.CSharp;
using NBomber.Http.CSharp;
using WebAPIDevSecOps.Dto;

namespace PerformanceTest.Scenarios
{
    public static class VentaScenario
    {
        public const double P95ThresholdMs = 1000;
        public const double ErrorRateThreshold = 0.005;
        public const int MaxUsers = 30;
        public static readonly TimeSpan Duration = TimeSpan.FromMinutes(2);

        private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNamingPolicy = null };
        private static string? _bearerToken;

        public static ScenarioProps Build(HttpClient client, string baseUrl, string user, string password, int idCliCliente, int idSegUsuario)
        {
            var baseUrlClean = baseUrl.TrimEnd('/');
            var loginUrl = $"{baseUrlClean}/api/v1/login/login";
            var ventaUrl = $"{baseUrlClean}/api/v1/venta";

            var scenario = Scenario.Create("venta_scenario", async context =>
            {
                var request = Http.CreateRequest("POST", ventaUrl)
                    .WithHeader("Authorization", $"Bearer {_bearerToken}")
                    .WithJsonBody(new VenVentaCreateDto
                    {
                        idCliCliente = idCliCliente,
                        idSegUsuario = idSegUsuario
                    }, JsonOptions);

                return await Http.Send(client, request);
            });

            return Scenario.WithLoadSimulations(
                Scenario.WithInit(scenario, async initContext =>
                {
                    _bearerToken = await AuthHelper.LoginAndGetTokenAsync(client, loginUrl, user, password);
                    Console.WriteLine($"[VentaScenario] Token JWT obtenido para {user}");
                }),
                Simulation.RampingConstant(Env.Int("PERF_VENTA_USERS", MaxUsers), Duration));
        }
    }
}
