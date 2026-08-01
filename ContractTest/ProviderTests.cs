using System.Diagnostics;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using PactNet;
using PactNet.Infrastructure.Outputters;
using PactNet.Verifier;
using Xunit.Abstractions;

namespace ContractTest
{
    public class ProviderTests
    {
        private const string JwtKey = "01123581321345589144233377610987";
        private const string JwtIssuer = "edelmeza.com";
        private const string JwtAudience = "edelmeza.com";

        private readonly ITestOutputHelper _output;

        public ProviderTests(ITestOutputHelper output)
        {
            _output = output;
        }

        [Fact]
        public async Task Verify_Api_Cumple_Contratos_Pact()
        {
            var port = GetFreePort();
            var baseUrl = $"http://127.0.0.1:{port}";
            var process = StartApiProcess(port);

            try
            {
                await WaitUntilReadyAsync(baseUrl, process);
                _output.WriteLine($"API de proveedor iniciada en {baseUrl}");

                var config = new PactVerifierConfig
                {
                    LogLevel = PactLogLevel.Information,
                    Outputters = new IOutput[] { new XunitOutput(_output) }
                };

                using var verifier = new PactVerifier("WebAPIDevSecOps", config);
                verifier
                    .WithHttpEndpoint(new Uri(baseUrl))
                    .WithDirectorySource(new DirectoryInfo(Path.Combine(AppContext.BaseDirectory, "pacts")), new[] { "web-app" })
                    .WithProviderStateUrl(new Uri(baseUrl + "/provider-states"))
                    .Verify();
            }
            finally
            {
                if (!process.HasExited)
                {
                    process.Kill(entireProcessTree: true);
                    process.WaitForExit();
                }

                process.Dispose();
            }
        }

        private Process StartApiProcess(int port)
        {
            var apiDll = Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "WebAPIDevSecOps", "bin", "Release", "net10.0", "WebAPIDevSecOps.dll");
            if (!File.Exists(apiDll))
            {
                apiDll = Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "WebAPIDevSecOps", "bin", "Debug", "net10.0", "WebAPIDevSecOps.dll");
            }

            if (!File.Exists(apiDll))
                throw new InvalidOperationException($"No se encontró WebAPIDevSecOps.dll en la ruta esperada: {apiDll}");

            var psi = new ProcessStartInfo
            {
                FileName = "dotnet",
                Arguments = $"\"{apiDll}\"",
                WorkingDirectory = Path.GetDirectoryName(apiDll),
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            };

            psi.Environment["PORT"] = port.ToString();
            psi.Environment["ASPNETCORE_ENVIRONMENT"] = "Development";
            psi.Environment["UseInMemoryDatabase"] = "true";
            psi.Environment["InMemoryDatabaseName"] = $"ContractTestDb_{Guid.NewGuid():N}";
            psi.Environment["EnableProviderStates"] = "true";
            psi.Environment["Jwt__Key"] = JwtKey;
            psi.Environment["Jwt__Issuer"] = JwtIssuer;
            psi.Environment["Jwt__Audience"] = JwtAudience;

            var process = new Process { StartInfo = psi };
            process.OutputDataReceived += (_, e) => { if (!string.IsNullOrEmpty(e.Data)) _output.WriteLine("[API] " + e.Data); };
            process.ErrorDataReceived += (_, e) => { if (!string.IsNullOrEmpty(e.Data)) _output.WriteLine("[API-ERR] " + e.Data); };

            if (!process.Start())
                throw new InvalidOperationException("No se pudo iniciar el proceso de la API.");

            process.BeginOutputReadLine();
            process.BeginErrorReadLine();

            return process;
        }

        private async Task WaitUntilReadyAsync(string baseUrl, Process process)
        {
            using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(3) };
            var deadline = DateTime.UtcNow.AddSeconds(60);

            while (DateTime.UtcNow < deadline)
            {
                if (process.HasExited)
                    throw new InvalidOperationException($"La API salió prematuramente con código {process.ExitCode}.");

                try
                {
                    using var response = await http.GetAsync(baseUrl + "/health");
                    if (response.IsSuccessStatusCode)
                        return;
                }
                catch
                {
                    // La API aún está arrancando
                }

                await Task.Delay(500);
            }

            throw new TimeoutException("La API no respondió en /health dentro de 60 segundos.");
        }

        private static int GetFreePort()
        {
            using var listener = new TcpListener(IPAddress.Loopback, 0);
            listener.Start();
            var port = ((IPEndPoint)listener.LocalEndpoint).Port;
            listener.Stop();
            return port;
        }

        private sealed class XunitOutput : IOutput
        {
            private readonly ITestOutputHelper _output;

            public XunitOutput(ITestOutputHelper output) => _output = output;

            public void WriteLine(string line) => _output.WriteLine(line);
        }
    }
}
