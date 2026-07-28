using Microsoft.EntityFrameworkCore;
using WebAPIDevSecOps.Models;

namespace WebAPIDevSecOps.Context
{
    public class AppDbContext : DbContext
    {
        public AppDbContext(DbContextOptions<AppDbContext>
           options) : base(options)
        {
        }

        public DbSet<EmpCatTipoEmpleado> EmpCatTipoEmpleado { get; set; } = default!;

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.Entity<SegUsuario>()
                .HasIndex(u => u.strNombre)
                .IsUnique();

            modelBuilder.Entity<EmpEmpleado>()
                .HasIndex(e => e.strCURP)
                .IsUnique()
                .HasFilter("[strCURP] IS NOT NULL");

            modelBuilder.Entity<EmpEmpleado>()
                .HasOne(e => e.EmpCatTipoEmpleado)
                .WithMany()
                .HasForeignKey(e => e.idEmpCatTipoEmpleado)
                .OnDelete(DeleteBehavior.SetNull);

            modelBuilder.Entity<CliCliente>()
                .HasIndex(c => c.strNombreCliente)
                .HasDatabaseName("IX_CliCliente_strNombreCliente");

            modelBuilder.Entity<CliCliente>()
                .HasIndex(c => c.strCorreoElectronico)
                .IsUnique()
                .HasDatabaseName("IX_CliCliente_strCorreoElectronico");

            modelBuilder.Entity<ProProducto>()
                .HasIndex(p => p.strNombreProducto)
                .HasDatabaseName("IX_ProProducto_strNombreProducto");

            modelBuilder.Entity<VenVenta>()
                .HasOne(v => v.CliCliente)
                .WithMany()
                .HasForeignKey(v => v.idCliCliente)
                .OnDelete(DeleteBehavior.Restrict);

            modelBuilder.Entity<VenVenta>()
                .HasOne(v => v.SegUsuario)
                .WithMany()
                .HasForeignKey(v => v.idSegUsuario)
                .OnDelete(DeleteBehavior.Restrict);

            modelBuilder.Entity<VenVenta>()
                .HasOne(v => v.VenCatEstado)
                .WithMany()
                .HasForeignKey(v => v.idVenCatEstado)
                .OnDelete(DeleteBehavior.Restrict);

            modelBuilder.Entity<VenVenta>()
                .HasIndex(v => v.strClaveVenta)
                .IsUnique()
                .HasDatabaseName("IX_VenVenta_strClaveVenta");

            modelBuilder.Entity<VenVenta>()
                .HasIndex(v => v.dteFechaHoraCompra)
                .HasDatabaseName("IX_VenVenta_dteFechaHoraCompra");

            modelBuilder.Entity<VenVentaDetalle>()
                .HasOne(vd => vd.VenVenta)
                .WithMany()
                .HasForeignKey(vd => vd.idVenVenta)
                .OnDelete(DeleteBehavior.Restrict);

            modelBuilder.Entity<VenVentaDetalle>()
                .HasOne(vd => vd.ProProducto)
                .WithMany()
                .HasForeignKey(vd => vd.idProProducto)
                .OnDelete(DeleteBehavior.Restrict);

        }

        public DbSet<CliCliente> CliCliente { get; set; } = default!;
        public DbSet<SegUsuario> SegUsuario { get; set; } = default!;
        public DbSet<EmpEmpleado> EmpEmpleado { get; set; } = default!;
        public DbSet<ProProducto> ProProducto { get; set; } = default!;
        public DbSet<VenCatEstado> VenCatEstado { get; set; } = default!;
        public DbSet<VenVenta> VenVenta { get; set; } = default!;
        public DbSet<VenVentaDetalle> VenVentaDetalle { get; set; } = default!;
    }
}
