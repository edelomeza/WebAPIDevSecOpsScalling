IF OBJECT_ID(N'[__EFMigrationsHistory]') IS NULL
BEGIN
    CREATE TABLE [__EFMigrationsHistory] (
        [MigrationId] nvarchar(150) NOT NULL,
        [ProductVersion] nvarchar(32) NOT NULL,
        CONSTRAINT [PK___EFMigrationsHistory] PRIMARY KEY ([MigrationId])
    );
END;
GO

BEGIN TRANSACTION;

                IF NOT EXISTS (SELECT 1 FROM sys.tables WHERE name = 'SegUsuario')
                BEGIN
                    CREATE TABLE [SegUsuario] (
                        [id] int NOT NULL IDENTITY,
                        [strNombre] nvarchar(50) NOT NULL,
                        [strPWD] nvarchar(200) NOT NULL,
                        [strCorreoElectronico] nvarchar(50) NOT NULL,
                        [dteFechaRegistro] datetime2 NULL,
                        [RowVersion] rowversion NOT NULL,
                        CONSTRAINT [PK_SegUsuario] PRIMARY KEY ([id])
                    );
                END


                IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_SegUsuario_strNombre')
                BEGIN
                    CREATE UNIQUE INDEX [IX_SegUsuario_strNombre] ON [SegUsuario] ([strNombre]);
                END

INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
VALUES (N'20260626232940_InitialCreate', N'10.0.9');

COMMIT;
GO

BEGIN TRANSACTION;

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
                END


                IF NOT EXISTS (SELECT 1 FROM sys.tables WHERE name = 'EmpCatTipoEmpleado')
                BEGIN
                    CREATE TABLE [EmpCatTipoEmpleado] (
                        [id] int NOT NULL IDENTITY,
                        [strValor] nvarchar(50) NOT NULL,
                        [strDescripcion] nvarchar(150) NOT NULL,
                        CONSTRAINT [PK_EmpCatTipoEmpleado] PRIMARY KEY ([id])
                    );
                END


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
                END


                IF NOT EXISTS (SELECT 1 FROM sys.tables WHERE name = 'VenCatEstado')
                BEGIN
                    CREATE TABLE [VenCatEstado] (
                        [id] int NOT NULL IDENTITY,
                        [strValor] nvarchar(50) NOT NULL,
                        [strDescripcion] nvarchar(200) NULL,
                        CONSTRAINT [PK_VenCatEstado] PRIMARY KEY ([id])
                    );
                END


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
                END


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
                            ON DELETE RESTRICT,
                        CONSTRAINT [FK_VenVenta_SegUsuario_idSegUsuario]
                            FOREIGN KEY ([idSegUsuario])
                            REFERENCES [SegUsuario]([id])
                            ON DELETE RESTRICT,
                        CONSTRAINT [FK_VenVenta_VenCatEstado_idVenCatEstado]
                            FOREIGN KEY ([idVenCatEstado])
                            REFERENCES [VenCatEstado]([id])
                            ON DELETE RESTRICT
                    );
                    CREATE INDEX [IX_VenVenta_idCliCliente] ON [VenVenta] ([idCliCliente]);
                    CREATE INDEX [IX_VenVenta_idSegUsuario] ON [VenVenta] ([idSegUsuario]);
                    CREATE INDEX [IX_VenVenta_idVenCatEstado] ON [VenVenta] ([idVenCatEstado]);
                    CREATE UNIQUE INDEX [IX_VenVenta_strClaveVenta] ON [VenVenta] ([strClaveVenta]);
                    CREATE INDEX [IX_VenVenta_dteFechaHoraCompra] ON [VenVenta] ([dteFechaHoraCompra]);
                END


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
                            ON DELETE RESTRICT,
                        CONSTRAINT [FK_VenVentaDetalle_VenVenta_idVenVenta]
                            FOREIGN KEY ([idVenVenta])
                            REFERENCES [VenVenta]([id])
                            ON DELETE RESTRICT
                    );
                    CREATE INDEX [IX_VenVentaDetalle_idProProducto] ON [VenVentaDetalle] ([idProProducto]);
                    CREATE INDEX [IX_VenVentaDetalle_idVenVenta] ON [VenVentaDetalle] ([idVenVenta]);
                END

INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
VALUES (N'20260714185913_V2_AddTables', N'10.0.9');

COMMIT;
GO

BEGIN TRANSACTION;
CREATE TABLE [VenPedido] (
    [id] uniqueidentifier NOT NULL,
    [idCliCliente] int NOT NULL,
    [dteFechaPedido] datetime2 NOT NULL,
    [decTotal] decimal(18,2) NOT NULL,
    [strEstadoSaga] nvarchar(50) NOT NULL,
    [strMotivoRechazo] nvarchar(500) NULL,
    [RowVersion] rowversion NOT NULL,
    CONSTRAINT [PK_VenPedido] PRIMARY KEY ([id]),
    CONSTRAINT [FK_VenPedido_CliCliente_idCliCliente] FOREIGN KEY ([idCliCliente]) REFERENCES [CliCliente] ([id]) ON DELETE NO ACTION
);

CREATE TABLE [VenPedidoDetalle] (
    [id] int NOT NULL IDENTITY,
    [idVenPedido] uniqueidentifier NOT NULL,
    [idProProducto] int NOT NULL,
    [intCantidad] int NOT NULL,
    [decPrecioUnitario] decimal(18,2) NOT NULL,
    [RowVersion] rowversion NOT NULL,
    CONSTRAINT [PK_VenPedidoDetalle] PRIMARY KEY ([id]),
    CONSTRAINT [FK_VenPedidoDetalle_ProProducto_idProProducto] FOREIGN KEY ([idProProducto]) REFERENCES [ProProducto] ([id]) ON DELETE NO ACTION,
    CONSTRAINT [FK_VenPedidoDetalle_VenPedido_idVenPedido] FOREIGN KEY ([idVenPedido]) REFERENCES [VenPedido] ([id]) ON DELETE NO ACTION
);

CREATE TABLE [VenPedidoFactura] (
    [id] int NOT NULL IDENTITY,
    [idVenPedido] uniqueidentifier NOT NULL,
    [strFolioFactura] nvarchar(50) NOT NULL,
    [strRFC] nvarchar(13) NULL,
    [decTotal] decimal(18,2) NOT NULL,
    [dteFechaEmision] datetime2 NOT NULL,
    [strEstado] nvarchar(20) NOT NULL,
    [RowVersion] rowversion NOT NULL,
    CONSTRAINT [PK_VenPedidoFactura] PRIMARY KEY ([id]),
    CONSTRAINT [FK_VenPedidoFactura_VenPedido_idVenPedido] FOREIGN KEY ([idVenPedido]) REFERENCES [VenPedido] ([id]) ON DELETE NO ACTION
);

CREATE TABLE [VenPedidoPago] (
    [id] int NOT NULL IDENTITY,
    [idVenPedido] uniqueidentifier NOT NULL,
    [decMonto] decimal(18,2) NOT NULL,
    [strMetodoPago] nvarchar(50) NULL,
    [strIdTransaccion] nvarchar(100) NULL,
    [strEstado] nvarchar(20) NOT NULL,
    [dteFechaPago] datetime2 NOT NULL,
    [RowVersion] rowversion NOT NULL,
    CONSTRAINT [PK_VenPedidoPago] PRIMARY KEY ([id]),
    CONSTRAINT [FK_VenPedidoPago_VenPedido_idVenPedido] FOREIGN KEY ([idVenPedido]) REFERENCES [VenPedido] ([id]) ON DELETE NO ACTION
);

CREATE INDEX [IX_VenPedido_idCliCliente] ON [VenPedido] ([idCliCliente]);

CREATE INDEX [IX_VenPedido_strEstadoSaga] ON [VenPedido] ([strEstadoSaga]);

CREATE INDEX [IX_VenPedidoDetalle_idProProducto] ON [VenPedidoDetalle] ([idProProducto]);

CREATE INDEX [IX_VenPedidoDetalle_idVenPedido] ON [VenPedidoDetalle] ([idVenPedido]);

CREATE INDEX [IX_VenPedidoFactura_idVenPedido] ON [VenPedidoFactura] ([idVenPedido]);

CREATE UNIQUE INDEX [IX_VenPedidoFactura_strFolioFactura] ON [VenPedidoFactura] ([strFolioFactura]);

CREATE INDEX [IX_VenPedidoPago_idVenPedido] ON [VenPedidoPago] ([idVenPedido]);

CREATE UNIQUE INDEX [IX_VenPedidoPago_strIdTransaccion] ON [VenPedidoPago] ([strIdTransaccion]) WHERE [strIdTransaccion] IS NOT NULL;

INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
VALUES (N'20260729004703_SagaVentas', N'10.0.9');

COMMIT;
GO

