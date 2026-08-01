using NBomber.Contracts;
using NBomber.CSharp;
using NBomber.Http.CSharp;

namespace PerformanceTest.Scenarios
{
    public static class ProductoScenario
    {
        public const double P95ThresholdMs = 200;
        public const double ErrorRateThreshold = 0.001;
        public const int Users = 100;
        public static readonly TimeSpan Duration = TimeSpan.FromMinutes(2);

        private static string? _bearerToken;

        public static ScenarioProps Build(HttpClient client, string baseUrl, string user, string password)
        {
            var baseUrlClean = baseUrl.TrimEnd('/');
            var loginUrl = $"{baseUrlClean}/api/v1/login/login";
            var productosUrl = $"{baseUrlClean}/api/v1/producto";

            var scenario = Scenario.Create("producto_scenario", async context =>
            {
                var request = Http.CreateRequest("GET", productosUrl)
                    .WithHeader("Authorization", $"Bearer {_bearerToken}");

                return await Http.Send(client, request);
            });

            return Scenario.WithLoadSimulations(
                Scenario.WithInit(scenario, async initContext =>
                {
                    _bearerToken = await AuthHelper.LoginAndGetTokenAsync(client, loginUrl, user, password);
                    Console.WriteLine($"[ProductoScenario] Token JWT obtenido para {user}");
                }),
                Simulation.KeepConstant(Users, Duration));
        }
    }
}
