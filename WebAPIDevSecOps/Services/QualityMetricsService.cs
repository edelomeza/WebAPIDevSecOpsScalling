using System.Diagnostics.Metrics;

namespace WebAPIDevSecOps.Services
{
    public sealed class QualityMetricsService
    {
        public const string MeterName = "WebAPIDevSecOps.QualityMetrics";

        public const string TestCoverageMetricName = "test_coverage_percent";
        public const string MutationScoreMetricName = "mutation_score";
        public const string SonarQualityGateMetricName = "sonar_quality_gate_passed";
        public const string P95LatencyMetricName = "p95_latency_ms";

        private readonly Meter _meter;
        private readonly ObservableGauge<double> _testCoverageGauge;
        private readonly ObservableGauge<double> _mutationScoreGauge;
        private readonly ObservableGauge<int> _sonarQualityGateGauge;
        private readonly ObservableGauge<double> _p95LatencyGauge;

        private double _testCoveragePercent;
        private double _mutationScore;
        private int _sonarQualityGatePassed;
        private double _p95LatencyMs;

        public QualityMetricsService(IConfiguration configuration)
        {
            _meter = new Meter(MeterName, "1.0.0");

            _testCoverageGauge = _meter.CreateObservableGauge(
                TestCoverageMetricName,
                () => _testCoveragePercent,
                unit: "percent",
                description: "Cobertura de código medida por check_coverage.py");

            _mutationScoreGauge = _meter.CreateObservableGauge(
                MutationScoreMetricName,
                () => _mutationScore,
                unit: "percent",
                description: "Mutation score de Stryker (porcentaje de mutantes matados)");

            _sonarQualityGateGauge = _meter.CreateObservableGauge(
                SonarQualityGateMetricName,
                () => _sonarQualityGatePassed,
                unit: "1",
                description: "Resultado del Quality Gate de SonarCloud: 1 = PASS, 0 = FAIL");

            _p95LatencyGauge = _meter.CreateObservableGauge(
                P95LatencyMetricName,
                () => _p95LatencyMs,
                unit: "ms",
                description: "Latencia P95 de los escenarios NBomber (login/producto/venta)");

            _testCoveragePercent = configuration.GetValue<double>("QualityMetrics:TestCoveragePercent", 0);
            _mutationScore = configuration.GetValue<double>("QualityMetrics:MutationScore", 0);
            _sonarQualityGatePassed = configuration.GetValue<bool>("QualityMetrics:SonarQualityGatePassed", false) ? 1 : 0;
            _p95LatencyMs = configuration.GetValue<double>("QualityMetrics:P95LatencyMs", 0);
        }

        public void Update(double testCoveragePercent, double mutationScore, bool sonarQualityGatePassed, double p95LatencyMs)
        {
            _testCoveragePercent = testCoveragePercent;
            _mutationScore = mutationScore;
            _sonarQualityGatePassed = sonarQualityGatePassed ? 1 : 0;
            _p95LatencyMs = p95LatencyMs;
        }
    }
}
