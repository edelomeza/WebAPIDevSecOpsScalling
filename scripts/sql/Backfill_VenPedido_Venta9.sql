-- Backfill espejo SagaBridge para VenVenta 9 (huérfana: creada con flag OFF, sin VenPedido).
-- Idempotente: re-ejecutable sin duplicar (guarda IF NOT EXISTS + índice único IX_VenPedido_LegacyVentaId).
-- Requisito previo: columna VenPedido.LegacyVentaId existe (PostDespliegue9_LegacyVentaId.sql / migración 20260922023016).
-- Uso: sqlcmd -S <host>,1433 -d db45497 -U <user> -P "<pass>" -C -i scripts/sql/Backfill_VenPedido_Venta9.sql
-- Para otra venta huérfana: cambiar @VentaId. Detección general al final del archivo.
DECLARE @VentaId int = 9;

-- 0. Pre-chequeos (solo lectura, no abortan): venta, detalles y DDL esperado.
IF COL_LENGTH('VenPedido', 'LegacyVentaId') IS NULL
BEGIN
    RAISERROR('Backfill abortado: falta columna VenPedido.LegacyVentaId. Aplicar PostDespliegue9_LegacyVentaId.sql primero.', 16, 1);
    RETURN;
END;

IF NOT EXISTS (SELECT 1 FROM [VenVenta] WHERE [id] = @VentaId)
BEGIN
    RAISERROR('Backfill abortado: VenVenta %d no existe.', 16, 1, @VentaId);
    RETURN;
END;

IF NOT EXISTS (SELECT 1 FROM [VenVentaDetalle] WHERE [idVenVenta] = @VentaId)
BEGIN
    RAISERROR('Backfill abortado: VenVenta %d no tiene detalles; no hay espejo que crear.', 16, 1, @VentaId);
    RETURN;
END;

BEGIN TRANSACTION;

-- 1. Cabecera espejo (solo si falta). Total recalculado desde ProProducto.decPrecio
--    (misma fuente de verdad que VentaService.FinalizarSagaAsync, no del DTO).
IF NOT EXISTS (SELECT 1 FROM [VenPedido] WHERE [LegacyVentaId] = @VentaId)
BEGIN
    INSERT INTO [VenPedido] ([id], [idCliCliente], [dteFechaPedido], [decTotal], [strEstadoSaga], [strMotivoRechazo], [LegacyVentaId])
    SELECT
        NEWID(),
        v.[idCliCliente],
        ISNULL(v.[dteFechaHoraCompra], SYSUTCDATETIME()),
        (SELECT SUM(CAST(vd.[intPiezaVenta] AS decimal(18,2)) * p.[decPrecio])
         FROM [VenVentaDetalle] vd
         JOIN [ProProducto] p ON p.[id] = vd.[idProProducto]
         WHERE vd.[idVenVenta] = @VentaId),
        N'Pendiente',
        NULL,
        @VentaId
    FROM [VenVenta] v
    WHERE v.[id] = @VentaId;
END;

-- 2. Detalles espejo (solo productos faltantes; re-ejecutable).
INSERT INTO [VenPedidoDetalle] ([idVenPedido], [idProProducto], [intCantidad], [decPrecioUnitario])
SELECT
    ped.[id],
    vd.[idProProducto],
    vd.[intPiezaVenta],
    p.[decPrecio]
FROM [VenPedido] ped
JOIN [VenVentaDetalle] vd ON vd.[idVenVenta] = ped.[LegacyVentaId]
JOIN [ProProducto] p ON p.[id] = vd.[idProProducto]
WHERE ped.[LegacyVentaId] = @VentaId
  AND NOT EXISTS (
      SELECT 1 FROM [VenPedidoDetalle] x
      WHERE x.[idVenPedido] = ped.[id]
        AND x.[idProProducto] = vd.[idProProducto]
  );

-- 3. Re-acumular decTotal por si el paso 2 añadió filas en una re-ejecución parcial.
UPDATE ped
SET [decTotal] = (
    SELECT SUM(CAST(d.[intCantidad] AS decimal(18,2)) * d.[decPrecioUnitario])
    FROM [VenPedidoDetalle] d
    WHERE d.[idVenPedido] = ped.[id]
)
FROM [VenPedido] ped
WHERE ped.[LegacyVentaId] = @VentaId;

COMMIT;
GO

-- 4. Verificación E2E del backfill (venta 9):
-- SELECT id, LegacyVentaId, strEstadoSaga, decTotal FROM [VenPedido] WHERE [LegacyVentaId] = 9;
-- SELECT d.idVenPedido, d.idProProducto, d.intCantidad, d.decPrecioUnitario
-- FROM [VenPedidoDetalle] d JOIN [VenPedido] p ON p.id = d.idVenPedido WHERE p.[LegacyVentaId] = 9;
-- SELECT SUM(CAST(intPiezaVenta AS decimal(18,2)) * (SELECT decPrecio FROM [ProProducto] pp WHERE pp.id = vd.idProProducto))
-- FROM [VenVentaDetalle] vd WHERE vd.[idVenVenta] = 9;  -- debe coincidir con VenPedido.decTotal

-- 5. Detección general de huérfanas (otras ventas creadas con flag OFF):
-- SELECT v.id AS VentaHuerfana, v.dteFechaHoraCompra
-- FROM [VenVenta] v LEFT JOIN [VenPedido] p ON p.[LegacyVentaId] = v.id
-- WHERE p.id IS NULL;
