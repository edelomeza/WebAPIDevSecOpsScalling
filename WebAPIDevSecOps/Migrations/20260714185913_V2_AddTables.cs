using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace WebAPIDevSecOps.Migrations
{
    /// <inheritdoc />
    public partial class V2_AddTables : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"
                IF NOT EXISTS (SELECT 1 FROM sys.tables WHERE name = 'CliCliente')
                BEGIN
                    CREATE TABLE [CliCliente] (
                        [id] int NOT NULL IDENTITY,
                        [strNombreCliente] nvarchar(100) NOT NULL,
                        [strDireccionCliente] nvarchar(200) NULL,
                        [RowVersion] rowversion NOT NULL,
                        [strCorreoElectronico] nvarchar(100) NOT NULL,
                        [strNumeroTelefono] nvarchar(10) NOT NULL,
                        CONSTRAINT [PK_CliCliente] PRIMARY KEY ([id])
                    );
                    CREATE UNIQUE INDEX [IX_CliCliente_strCorreoElectronico] ON [CliCliente] ([strCorreoElectronico]);
                    CREATE INDEX [IX_CliCliente_strNombreCliente] ON [CliCliente] ([strNombreCliente]);
                END");

            migrationBuilder.Sql(@"
                IF NOT EXISTS (SELECT 1 FROM sys.tables WHERE name = 'EmpCatTipoEmpleado')
                BEGIN
                    CREATE TABLE [EmpCatTipoEmpleado] (
                        [id] int NOT NULL IDENTITY,
                        [strValor] nvarchar(50) NOT NULL,
                        [strDescripcion] nvarchar(150) NOT NULL,
                        CONSTRAINT [PK_EmpCatTipoEmpleado] PRIMARY KEY ([id])
                    );
                END");

            migrationBuilder.Sql(@"
                IF NOT EXISTS (SELECT 1 FROM sys.tables WHERE name = 'ProProducto')
                BEGIN
                    CREATE TABLE [ProProducto] (
                        [id] int NOT NULL IDENTITY,
                        [strNombreProducto] nvarchar(50) NOT NULL,
                        [strURLImagen] nvarchar(300) NULL,
                        [strDescripcion] nvarchar(250) NULL,
                        [intNumeroExistencia] int NOT NULL,
                        [decPrecio] decimal(18,2) NOT NULL,
                        [RowVersion] rowversion NOT NULL,
                        CONSTRAINT [PK_ProProducto] PRIMARY KEY ([id])
                    );
                    CREATE INDEX [IX_ProProducto_strNombreProducto] ON [ProProducto] ([strNombreProducto]);
                END");

            migrationBuilder.Sql(@"
                IF NOT EXISTS (SELECT 1 FROM sys.tables WHERE name = 'VenCatEstado')
                BEGIN
                    CREATE TABLE [VenCatEstado] (
                        [id] int NOT NULL IDENTITY,
                        [strValor] nvarchar(50) NOT NULL,
                        [strDescripcion] nvarchar(200) NULL,
                        CONSTRAINT [PK_VenCatEstado] PRIMARY KEY ([id])
                    );

                    INSERT INTO [VenCatEstado] ([strValor], [strDescripcion]) VALUES
                        ('PENDIENTE', 'Pedido pendiente de procesar'),
                        ('EN_PROCESO', 'Pedido en proceso'),
                        ('COMPLETADO', 'Pedido completado exitosamente'),
                        ('CANCELADO', 'Pedido cancelado'),
                        ('REEMBOLSADO', 'Pedido reembolsado'),
                        ('ENVIADO', 'Pedido enviado al cliente'),
                        ('ENTREGADO', 'Pedido entregado al cliente');
                END");

            migrationBuilder.Sql(@"
                IF NOT EXISTS (SELECT 1 FROM sys.tables WHERE name = 'EmpEmpleado')
                BEGIN
                    CREATE TABLE [EmpEmpleado] (
                        [id] int NOT NULL IDENTITY,
                        [strNombre] nvarchar(50) NOT NULL,
                        [strAPaterno] nvarchar(50) NULL,
                        [strAMaterno] nvarchar(50) NULL,
                        [strCURP] nvarchar(18) NULL,
                        [idEmpCatTipoEmpleado] int NULL,
                        [RowVersion] rowversion NULL,
                        CONSTRAINT [PK_EmpEmpleado] PRIMARY KEY ([id]),
                        CONSTRAINT [FK_EmpEmpleado_EmpCatTipoEmpleado_idEmpCatTipoEmpleado]
                            FOREIGN KEY ([idEmpCatTipoEmpleado])
                            REFERENCES [EmpCatTipoEmpleado]([id])
                            ON DELETE SET NULL
                    );
                    CREATE INDEX [IX_EmpEmpleado_idEmpCatTipoEmpleado] ON [EmpEmpleado] ([idEmpCatTipoEmpleado]);
                    CREATE UNIQUE INDEX [IX_EmpEmpleado_strCURP] ON [EmpEmpleado] ([strCURP]) WHERE [strCURP] IS NOT NULL;
                END");

            migrationBuilder.Sql(@"
                IF NOT EXISTS (SELECT 1 FROM sys.tables WHERE name = 'VenVenta')
                BEGIN
                    CREATE TABLE [VenVenta] (
                        [id] int NOT NULL IDENTITY,
                        [idCliCliente] int NOT NULL,
                        [idSegUsuario] int NOT NULL,
                        [idVenCatEstado] int NOT NULL,
                        [dteFechaHoraCompra] datetime2 NULL,
                        [strClaveVenta] nvarchar(10) NOT NULL,
                        [RowVersion] rowversion NULL,
                        CONSTRAINT [PK_VenVenta] PRIMARY KEY ([id]),
                        CONSTRAINT [FK_VenVenta_CliCliente_idCliCliente]
                            FOREIGN KEY ([idCliCliente])
                            REFERENCES [CliCliente]([id])
                            ON DELETE NO ACTION,
                        CONSTRAINT [FK_VenVenta_SegUsuario_idSegUsuario]
                            FOREIGN KEY ([idSegUsuario])
                            REFERENCES [SegUsuario]([id])
                            ON DELETE NO ACTION,
                        CONSTRAINT [FK_VenVenta_VenCatEstado_idVenCatEstado]
                            FOREIGN KEY ([idVenCatEstado])
                            REFERENCES [VenCatEstado]([id])
                            ON DELETE NO ACTION
                    );
                    CREATE INDEX [IX_VenVenta_idCliCliente] ON [VenVenta] ([idCliCliente]);
                    CREATE INDEX [IX_VenVenta_idSegUsuario] ON [VenVenta] ([idSegUsuario]);
                    CREATE INDEX [IX_VenVenta_idVenCatEstado] ON [VenVenta] ([idVenCatEstado]);
                    CREATE UNIQUE INDEX [IX_VenVenta_strClaveVenta] ON [VenVenta] ([strClaveVenta]);
                    CREATE INDEX [IX_VenVenta_dteFechaHoraCompra] ON [VenVenta] ([dteFechaHoraCompra]);
                END");

            migrationBuilder.Sql(@"
                IF NOT EXISTS (SELECT 1 FROM sys.tables WHERE name = 'VenVentaDetalle')
                BEGIN
                    CREATE TABLE [VenVentaDetalle] (
                        [id] int NOT NULL IDENTITY,
                        [idVenVenta] int NOT NULL,
                        [idProProducto] int NOT NULL,
                        [intPiezaVenta] int NOT NULL,
                        [decTotalVenta] decimal(18,2) NOT NULL,
                        [RowVersion] rowversion NULL,
                        CONSTRAINT [PK_VenVentaDetalle] PRIMARY KEY ([id]),
                        CONSTRAINT [FK_VenVentaDetalle_ProProducto_idProProducto]
                            FOREIGN KEY ([idProProducto])
                            REFERENCES [ProProducto]([id])
                            ON DELETE NO ACTION,
                        CONSTRAINT [FK_VenVentaDetalle_VenVenta_idVenVenta]
                            FOREIGN KEY ([idVenVenta])
                            REFERENCES [VenVenta]([id])
                            ON DELETE NO ACTION
                    );
                    CREATE INDEX [IX_VenVentaDetalle_idProProducto] ON [VenVentaDetalle] ([idProProducto]);
                    CREATE INDEX [IX_VenVentaDetalle_idVenVenta] ON [VenVentaDetalle] ([idVenVenta]);
                END");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(name: "EmpEmpleado");
            migrationBuilder.DropTable(name: "VenVentaDetalle");
            migrationBuilder.DropTable(name: "EmpCatTipoEmpleado");
            migrationBuilder.DropTable(name: "ProProducto");
            migrationBuilder.DropTable(name: "VenVenta");
            migrationBuilder.DropTable(name: "CliCliente");
            migrationBuilder.DropTable(name: "VenCatEstado");
        }
    }
}
