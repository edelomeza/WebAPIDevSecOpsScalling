using System.Diagnostics.Metrics;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using WebAPIDevSecOps.Services;

namespace UnitTest.Services;

public class QualityMetricsServiceTests
{
    private static IConfiguration CreateConfig(
        double coverage = 0, double mutationScore = 0,
        bool sonarGatePassed = false, double p95LatencyMs = 0)
    {
        return new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["QualityMetrics:TestCoveragePercent"] = coverage.ToString(),
                ["QualityMetrics:MutationScore"] = mutationScore.ToString(),
                ["QualityMetrics:SonarQualityGatePassed"] = sonarGatePassed.ToString(),
                ["QualityMetrics:P95LatencyMs"] = p95LatencyMs.ToString()
            })
            .Build();
    }

    private static Dictionary<string, object?> Observe(QualityMetricsService service)
    {
        var values = new Dictionary<string, object?>();
        using var listener = new MeterListener();
        listener.InstrumentPublished = (instrument, l) =>
        {
            if (instrument.Meter.Name == QualityMetricsService.MeterName)
                l.EnableMeasurementEvents(instrument);
        };
        listener.SetMeasurementEventCallback<double>((instrument, value, tags, state) => values[instrument.Name] = value);
        listener.SetMeasurementEventCallback<int>((instrument, value, tags, state) => values[instrument.Name] = value);
        listener.Start();
        listener.RecordObservableInstruments();
        return values;
    }

    [Fact]
    public void Initializes_From_Configuration()
    {
        var service = new QualityMetricsService(CreateConfig(
            coverage: 46.1, mutationScore: 80.7, sonarGatePassed: true, p95LatencyMs: 123.4));

        var values = Observe(service);

        values[QualityMetricsService.TestCoverageMetricName].Should().Be(46.1);
        values[QualityMetricsService.MutationScoreMetricName].Should().Be(80.7);
        values[QualityMetricsService.SonarQualityGateMetricName].Should().Be(1);
        values[QualityMetricsService.P95LatencyMetricName].Should().Be(123.4);
    }

    [Fact]
    public void Defaults_To_Zero_When_Not_Configured()
    {
        var service = new QualityMetricsService(new ConfigurationBuilder().Build());

        var values = Observe(service);

        values[QualityMetricsService.TestCoverageMetricName].Should().Be(0.0);
        values[QualityMetricsService.MutationScoreMetricName].Should().Be(0.0);
        values[QualityMetricsService.SonarQualityGateMetricName].Should().Be(0);
        values[QualityMetricsService.P95LatencyMetricName].Should().Be(0.0);
    }

    [Fact]
    public void Update_Overrides_Values_From_Configuration()
    {
        var service = new QualityMetricsService(CreateConfig(coverage: 10, mutationScore: 20));

        service.Update(testCoveragePercent: 75.5, mutationScore: 88.9, sonarQualityGatePassed: true, p95LatencyMs: 250);

        var values = Observe(service);

        values[QualityMetricsService.TestCoverageMetricName].Should().Be(75.5);
        values[QualityMetricsService.MutationScoreMetricName].Should().Be(88.9);
        values[QualityMetricsService.SonarQualityGateMetricName].Should().Be(1);
        values[QualityMetricsService.P95LatencyMetricName].Should().Be(250);
    }

    [Fact]
    public void Update_With_Failed_Gate_Records_Zero()
    {
        var service = new QualityMetricsService(CreateConfig(sonarGatePassed: true));

        service.Update(testCoveragePercent: 75.5, mutationScore: 88.9, sonarQualityGatePassed: false, p95LatencyMs: 250);

        var values = Observe(service);

        values[QualityMetricsService.SonarQualityGateMetricName].Should().Be(0);
    }
}
