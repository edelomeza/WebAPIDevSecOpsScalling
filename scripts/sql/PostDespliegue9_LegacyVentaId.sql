BEGIN TRANSACTION;
IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260922023016_AddLegacyVentaIdToVenPedido'
)
BEGIN
    ALTER TABLE [VenPedido] ADD [LegacyVentaId] int NULL;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260922023016_AddLegacyVentaIdToVenPedido'
)
BEGIN
    EXEC(N'CREATE UNIQUE INDEX [IX_VenPedido_LegacyVentaId] ON [VenPedido] ([LegacyVentaId]) WHERE [LegacyVentaId] IS NOT NULL');
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260922023016_AddLegacyVentaIdToVenPedido'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20260922023016_AddLegacyVentaIdToVenPedido', N'10.0.9');
END;

COMMIT;
GO

