using System.Text.Json;
using NBomber.Contracts;
using NBomber.CSharp;
using NBomber.Http.CSharp;
using WebAPIDevSecOps.Dto;

namespace PerformanceTest.Scenarios
{
    public static class LoginScenario
    {
        public const double P95ThresholdMs = 500;
        public const double ErrorRateThreshold = 0.001;
        public const int MaxUsers = 50;
        public static readonly TimeSpan Duration = TimeSpan.FromMinutes(2);

        private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNamingPolicy = null };

        public static ScenarioProps Build(HttpClient client, string baseUrl, string user, string password)
        {
            var url = $"{baseUrl.TrimEnd('/')}/api/v1/login/login";

            return Scenario.WithLoadSimulations(
                Scenario.Create("login_scenario", async context =>
                {
                    var request = Http.CreateRequest("POST", url)
                        .WithJsonBody(new LoginRequest(user, password), JsonOptions);

                    return await Http.Send(client, request);
                }),
                Simulation.RampingConstant(MaxUsers, Duration));
        }
    }
}
