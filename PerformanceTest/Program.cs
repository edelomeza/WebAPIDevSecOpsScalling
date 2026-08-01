using NBomber.Contracts;
using NBomber.Contracts.Stats;
using NBomber.CSharp;
using PerformanceTest.Scenarios;

var baseUrl = Env("PERF_API_BASE_URL", "http://localhost:5196");
var user = Env("PERF_LOGIN_USER", "admin");
var password = Env("PERF_LOGIN_PASSWORD", "");
var idCliCliente = EnvInt("PERF_CLIENTE_ID", 1);
var idSegUsuario = EnvInt("PERF_USUARIO_ID", 1);

using var client = new HttpClient
{
    Timeout = TimeSpan.FromSeconds(100)
};

var specs = new[]
{
    new ScenarioSpec("login_scenario", LoginScenario.Build(client, baseUrl, user, password),
        LoginScenario.P95ThresholdMs, LoginScenario.ErrorRateThreshold, UseMean: false),
    new ScenarioSpec("producto_scenario", ProductoScenario.Build(client, baseUrl, user, password),
        ProductoScenario.P95ThresholdMs, ProductoScenario.ErrorRateThreshold, UseMean: false),
    new ScenarioSpec("venta_scenario", VentaScenario.Build(client, baseUrl, user, password, idCliCliente, idSegUsuario),
        VentaScenario.P95ThresholdMs, VentaScenario.ErrorRateThreshold, UseMean: false),
    new ScenarioSpec("mixto_scenario", MixtoScenario.Build(client, baseUrl, user, password, idCliCliente, idSegUsuario),
        MixtoScenario.AvgResponseTimeThresholdMs, MixtoScenario.ErrorRateThreshold, UseMean: true)
};

try
{
    var result = NBomberRunner
        .RegisterScenarios(specs.Select(s => s.Props).ToArray())
        .WithTestSuite("performance")
        .WithTestName("fase4-suite")
        .WithReportFolder("reports")
        .WithReportFormats(ReportFormat.Html, ReportFormat.Txt, ReportFormat.Md)
        .Run();

    return ReportVerdicts(specs, result);
}
catch (Exception ex)
{
    Console.WriteLine($"SUITE ERROR: el run no completó (target caído o init fallido): {ex.Message}");
    return 2;
}

static int ReportVerdicts(ScenarioSpec[] specs, NBomber.Contracts.Stats.NodeStats result)
{
    var failedCount = 0;

    foreach (var spec in specs)
    {
        var stats = result.ScenarioStats.Get(spec.Name);
        var latencyMs = spec.UseMean ? stats.Ok.Latency.MeanMs : stats.Ok.Latency.Percent95;
        var errorPercent = stats.Fail.Request.Percent;

        var latencyOk = latencyMs < spec.LatencyThresholdMs;
        var errorOk = errorPercent < spec.ErrorThresholdPercent;
        var verdict = latencyOk && errorOk ? "PASS" : "FAIL";

        if (!latencyOk || !errorOk)
        {
            failedCount++;
        }

        var metric = spec.UseMean ? "avg" : "p95";
        Console.WriteLine(
            $"[{verdict}] {spec.Name}: {metric} = {latencyMs:F1}ms (umbral {spec.LatencyThresholdMs}ms), " +
            $"error = {errorPercent:F3}% (umbral {spec.ErrorThresholdPercent}%)");
    }

    Console.WriteLine(failedCount == 0
        ? "SUITE PASS: todos los thresholds cumplidos"
        : $"SUITE FAIL: {failedCount} escenario(s) incumplen thresholds");

    Console.WriteLine($"Reporte HTML: reports/nbomber_report-{DateTime.Now:yyyy-MM-dd}.html");

    return failedCount == 0 ? 0 : 1;
}

static string Env(string name, string fallback) =>
    Environment.GetEnvironmentVariable(name) ?? fallback;

static int EnvInt(string name, int fallback) =>
    int.TryParse(Environment.GetEnvironmentVariable(name), out var value) ? value : fallback;

record ScenarioSpec(
    string Name,
    NBomber.Contracts.ScenarioProps Props,
    double LatencyThresholdMs,
    double ErrorThresholdPercent,
    bool UseMean);
