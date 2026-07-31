using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Testcontainers.MsSql;
using WebAPIDevSecOps.Context;

namespace DatabaseTest;

public class MigrationsTests : IAsyncLifetime
{
    private readonly MsSqlContainer _container;
    private string _connectionString = null!;

    public MigrationsTests()
    {
        _container = new MsSqlBuilder()
            .WithImage("mcr.microsoft.com/mssql/server:2022-latest")
            .Build();
    }

    public async Task InitializeAsync()
    {
        await _container.StartAsync();
        _connectionString = _container.GetConnectionString();
    }

    public async Task DisposeAsync()
    {
        await _container.DisposeAsync();
    }

    private AppDbContext CreateDbContext()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlServer(_connectionString)
            .Options;
        return new AppDbContext(options);
    }

    private async Task<List<string>> GetTableNamesAsync(AppDbContext context)
    {
        var tables = await context.Database
            .SqlQuery<string>($@"
                SELECT TABLE_NAME
                FROM INFORMATION_SCHEMA.TABLES
                WHERE TABLE_TYPE = 'BASE TABLE'
                  AND TABLE_SCHEMA = 'dbo'
                  AND TABLE_NAME NOT LIKE '__EFMigrationsHistory'
                  AND TABLE_NAME NOT LIKE 'spt_%'
                  AND TABLE_NAME <> 'MSreplication_options'
                ORDER BY TABLE_NAME")
            .ToListAsync();
        return tables;
    }

    [Fact]
    public async Task ApplyMigrations_AllTablesExist()
    {
        using var context = CreateDbContext();

        await context.Database.MigrateAsync();

        var tables = await GetTableNamesAsync(context);
        tables.Should().HaveCount(13);
        tables.Should().Contain("CliCliente");
        tables.Should().Contain("EmpCatTipoEmpleado");
        tables.Should().Contain("EmpEmpleado");
        tables.Should().Contain("ProProducto");
        tables.Should().Contain("SegRefreshToken");
        tables.Should().Contain("SegUsuario");
        tables.Should().Contain("VenCatEstado");
        tables.Should().Contain("VenPedido");
        tables.Should().Contain("VenPedidoDetalle");
        tables.Should().Contain("VenPedidoFactura");
        tables.Should().Contain("VenPedidoPago");
        tables.Should().Contain("VenVenta");
        tables.Should().Contain("VenVentaDetalle");
    }

    [Fact]
    public async Task ApplyMigrations_MigrationHistoryHasEntries()
    {
        using var context = CreateDbContext();

        await context.Database.MigrateAsync();

        var migrations = await context.Database.GetAppliedMigrationsAsync();
        migrations.Should().NotBeEmpty();
    }

    [Fact]
    public async Task RollbackMigrations_HistoryTableIsEmpty()
    {
        using var context = CreateDbContext();

        await context.Database.MigrateAsync();

        await context.Database.MigrateAsync("0");

        var migrations = await context.Database.GetAppliedMigrationsAsync();
        migrations.Should().BeEmpty();
    }

    [Fact]
    public async Task RollbackThenReapplyMigrations_TablesExist()
    {
        using var context = CreateDbContext();

        await context.Database.MigrateAsync();
        await context.Database.MigrateAsync("0");
        await context.Database.MigrateAsync();

        var tables = await GetTableNamesAsync(context);
        tables.Should().HaveCount(13);
    }

    [Fact]
    public async Task SeedData_CatalogTablesHaveData()
    {
        using var context = CreateDbContext();

        await context.Database.MigrateAsync();

        var estados = await context.Set<WebAPIDevSecOps.Models.VenCatEstado>().CountAsync();
        estados.Should().BeGreaterThan(0);
    }
}
