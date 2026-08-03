using System.Diagnostics;
using System.Net;
using DotNet.Testcontainers.Builders;
using DotNet.Testcontainers.Containers;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using UnitTest.Common;

namespace IntegrationTest;

public class RecoveryTests : IAsyncLifetime
{
    private const int HostPort = 14344;
    private const string SaPassword = "yourStrong(!)Password";

    private readonly IContainer _sqlContainer;
    private WebApplicationFactory<Program> _factory = null!;
    private HttpClient _client = null!;

    public RecoveryTests()
    {
        _sqlContainer = new ContainerBuilder("mcr.microsoft.com/mssql/server:2022-latest")
            .WithPortBinding(HostPort, 1433)
            .WithEnvironment("ACCEPT_EULA", "Y")
            .WithEnvironment("MSSQL_SA_PASSWORD", SaPassword)
            .WithWaitStrategy(Wait.ForUnixContainer()
                .UntilCommandIsCompleted(
                    $"/bin/bash -c 'until /opt/mssql-tools18/bin/sqlcmd -C -S localhost -U sa -P \"{SaPassword}\" -Q \"SELECT 1\" > /dev/null 2>&1; do sleep 1; done'"))
            .Build();
    }

    public async Task InitializeAsync()
    {
        await _sqlContainer.StartAsync();

        _factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseSetting("UseInMemoryDatabase", "false");
            builder.UseSetting("ConnectionStrings:DefaultConnection", GetConnectionString());
            builder.UseSetting("Jwt:Key", JwtTestConfig.Key);
            builder.UseSetting("Jwt:Issuer", JwtTestConfig.Issuer);
            builder.UseSetting("Jwt:Audience", JwtTestConfig.Audience);
        });
        _client = _factory.CreateClient();
    }

    public async Task DisposeAsync()
    {
        _client.Dispose();
        _factory.Dispose();
        await _sqlContainer.DisposeAsync();
    }

    private static string GetConnectionString() =>
        $"Server=127.0.0.1,{HostPort};Database=master;User Id=sa;Password={SaPassword};TrustServerCertificate=True";

    [Fact]
    public async Task HealthReady_SqlServerUp_Returns200()
    {
        var response = await _client.GetAsync("/health/ready");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task HealthReady_SqlServerDown_Returns503()
    {
        await _sqlContainer.StopAsync();

        try
        {
            var response = await _client.GetAsync("/health/ready");

            response.StatusCode.Should().Be(HttpStatusCode.ServiceUnavailable);
        }
        finally
        {
            await _sqlContainer.StartAsync();
        }
    }

    [Fact]
    public async Task HealthReady_AfterRestart_RecoversTo200()
    {
        await _sqlContainer.StopAsync();
        var downResponse = await _client.GetAsync("/health/ready");
        downResponse.StatusCode.Should().Be(HttpStatusCode.ServiceUnavailable);

        await _sqlContainer.StartAsync();

        var stopwatch = Stopwatch.StartNew();
        var status = HttpStatusCode.ServiceUnavailable;

        while (stopwatch.Elapsed < TimeSpan.FromSeconds(90) && status != HttpStatusCode.OK)
        {
            var response = await _client.GetAsync("/health/ready");
            status = response.StatusCode;
            if (status != HttpStatusCode.OK)
                await Task.Delay(500);
        }

        status.Should().Be(HttpStatusCode.OK);
    }
}
