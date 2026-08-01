using System.Net.Http.Json;
using System.Text.Json;
using WebAPIDevSecOps.Dto;

namespace PerformanceTest.Scenarios
{
    internal static class AuthHelper
    {
        private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNamingPolicy = null };

        public static async Task<string> LoginAndGetTokenAsync(HttpClient client, string loginUrl, string user, string password)
        {
            var loginResponse = await client.PostAsJsonAsync(loginUrl, new LoginRequest(user, password), JsonOptions);
            loginResponse.EnsureSuccessStatusCode();

            using var doc = JsonDocument.Parse(await loginResponse.Content.ReadAsStringAsync());
            var token = doc.RootElement.TryGetProperty("token", out var tokenProperty)
                ? tokenProperty.GetString()
                : null;

            return token ?? throw new InvalidOperationException("Login de init no devolvió 'token' (¿2FA habilitado para el usuario de perf?).");
        }
    }
}
