using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using WebAPIDevSecOps.Context;
using WebAPIDevSecOps.Models;

namespace UnitTest.Common
{
    public class TestDbContext : AppDbContext
    {
        public TestDbContext(DbContextOptions<AppDbContext> options) : base(options) { }

        public override Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
        {
            foreach (var entry in ChangeTracker.Entries<SegUsuario>())
            {
                if (entry.State == EntityState.Added)
                {
                    entry.Entity.RowVersion ??= new byte[] { 1, 0, 0, 0 };
                }
            }

            foreach (var entry in ChangeTracker.Entries<EmpEmpleado>())
            {
                if (entry.State == EntityState.Added)
                {
                    entry.Entity.RowVersion ??= new byte[] { 1, 0, 0, 0 };
                }
            }

            foreach (var entry in ChangeTracker.Entries<CliCliente>())
            {
                if (entry.State == EntityState.Added)
                {
                    entry.Entity.RowVersion ??= new byte[] { 1, 0, 0, 0 };
                }
            }

            foreach (var entry in ChangeTracker.Entries<ProProducto>())
            {
                if (entry.State == EntityState.Added)
                {
                    entry.Entity.RowVersion ??= new byte[] { 1, 0, 0, 0 };
                }
            }

            foreach (var entry in ChangeTracker.Entries<VenVenta>())
            {
                if (entry.State == EntityState.Added)
                {
                    entry.Entity.RowVersion ??= new byte[] { 1, 0, 0, 0 };
                }
            }

            foreach (var entry in ChangeTracker.Entries<VenVentaDetalle>())
            {
                if (entry.State == EntityState.Added)
                {
                    entry.Entity.RowVersion ??= new byte[] { 1, 0, 0, 0 };
                }
            }

            foreach (var entry in ChangeTracker.Entries<VenPedido>())
            {
                if (entry.State == EntityState.Added)
                {
                    entry.Entity.RowVersion ??= new byte[] { 1, 0, 0, 0 };
                }
            }

            foreach (var entry in ChangeTracker.Entries<VenPedidoDetalle>())
            {
                if (entry.State == EntityState.Added)
                {
                    entry.Entity.RowVersion ??= new byte[] { 1, 0, 0, 0 };
                }
            }

            foreach (var entry in ChangeTracker.Entries<SegRefreshToken>())
            {
                if (entry.State == EntityState.Added)
                {
                    entry.Entity.RowVersion ??= new byte[] { 1, 0, 0, 0 };
                }
            }

            return base.SaveChangesAsync(cancellationToken);
        }
    }
}
