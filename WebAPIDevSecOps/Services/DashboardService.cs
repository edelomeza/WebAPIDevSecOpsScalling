using Amazon.SQS;
using Amazon.SQS.Model;
using Microsoft.EntityFrameworkCore;
using WebAPIDevSecOps.Context;
using WebAPIDevSecOps.Dto;
using WebAPIDevSecOps.Interfaces;
using WebAPIDevSecOps.Models;

namespace WebAPIDevSecOps.Services
{
    public class DashboardService : IDashboardService
    {
        private readonly AppDbContext _context;
        private readonly ILogger<DashboardService> _logger;
        private readonly IAmazonSQS? _sqs;
        private readonly IConfiguration? _configuration;

        public DashboardService(AppDbContext context, ILogger<DashboardService> logger, IAmazonSQS? amazonSqs = null, IConfiguration? configuration = null)
        {
            _context = context;
            _logger = logger;
            _sqs = amazonSqs;
            _configuration = configuration;
        }

        public async Task<DashboardDto> GetDashboardAsync()
        {
            var inicioHoy = DateTime.UtcNow.Date;
            var finHoy = inicioHoy.AddDays(1);

            var pedidosHoy = await _context.Set<VenPedido>()
                .AsNoTracking()
                .Where(p => p.dteFechaPedido >= inicioHoy && p.dteFechaPedido < finHoy)
                .ToListAsync();

            var pedidosPorEstado = await _context.Set<VenPedido>()
                .AsNoTracking()
                .GroupBy(p => p.strEstadoSaga)
                .Select(g => new EstadoSagaCountDto
                {
                    strEstadoSaga = g.Key,
                    intCantidad = g.Count(),
                })
                .OrderByDescending(e => e.intCantidad)
                .ToListAsync();

            var dashboard = new DashboardDto
            {
                intTotalPedidosHoy = pedidosHoy.Count,
                decVentasHoy = pedidosHoy
                    .Where(p => p.strEstadoSaga == "Facturado")
                    .Sum(p => p.decTotal),
                lstPedidosPorEstado = pedidosPorEstado,
                dctProfundidadColas = await GetQueueDepthAsync(),
            };

            return dashboard;
        }

        public async Task<SagaTimelineDto?> GetTimelineAsync(Guid id)
        {
            var pedido = await _context.Set<VenPedido>()
                .AsNoTracking()
                .Include(p => p.Pagos)
                .Include(p => p.Facturas)
                .FirstOrDefaultAsync(p => p.id == id);

            if (pedido == null)
                return null;

            var eventos = new List<SagaEventDto>
            {
                new()
                {
                    strEtapa = "PedidoCreado",
                    dteFecha = pedido.dteFechaPedido,
                    strEstado = "Pendiente",
                    strDetalle = $"Total del pedido: {pedido.decTotal:N2}",
                },
            };

            var estadosConStockValidado = new[]
            {
                "StockValidado", "Pagado", "PagoRechazado", "Facturado",
                "CompensadoPago", "CompensadoFactura",
            };

            if (pedido.strEstadoSaga == "StockRechazado")
            {
                eventos.Add(new SagaEventDto
                {
                    strEtapa = "StockRechazado",
                    dteFecha = null,
                    strEstado = pedido.strEstadoSaga,
                    strDetalle = pedido.strMotivoRechazo,
                });
            }
            else if (estadosConStockValidado.Contains(pedido.strEstadoSaga))
            {
                eventos.Add(new SagaEventDto
                {
                    strEtapa = "StockValidado",
                    dteFecha = null,
                    strEstado = "StockValidado",
                    strDetalle = "Stock descontado (sin timestamp persistido)",
                });
            }

            if (pedido.Pagos is not null)
            {
                foreach (var pago in pedido.Pagos.OrderBy(p => p.dteFechaPago))
                {
                    eventos.Add(new SagaEventDto
                    {
                        strEtapa = pago.strEstado switch
                        {
                            "Reembolsado" => "ReembolsoPago",
                            "Rechazado" => "PagoRechazado",
                            _ => "PagoProcesado",
                        },
                        dteFecha = pago.dteFechaPago,
                        strEstado = pago.strEstado,
                        strDetalle = $"Monto: {pago.decMonto:N2}; Transacción: {pago.strIdTransaccion ?? "N/A"}",
                    });
                }
            }

            if (pedido.Facturas is not null)
            {
                foreach (var factura in pedido.Facturas.OrderBy(f => f.dteFechaEmision))
                {
                    eventos.Add(new SagaEventDto
                    {
                        strEtapa = "FacturaGenerada",
                        dteFecha = factura.dteFechaEmision,
                        strEstado = factura.strEstado,
                        strDetalle = $"Folio: {factura.strFolioFactura}",
                    });
                }
            }

            if (pedido.strEstadoSaga == "CompensadoPago")
            {
                eventos.Add(new SagaEventDto
                {
                    strEtapa = "CompensacionPago",
                    dteFecha = null,
                    strEstado = pedido.strEstadoSaga,
                    strDetalle = "Liberar stock descontado",
                });
            }
            else if (pedido.strEstadoSaga == "CompensadoFactura")
            {
                eventos.Add(new SagaEventDto
                {
                    strEtapa = "CompensacionFactura",
                    dteFecha = null,
                    strEstado = pedido.strEstadoSaga,
                    strDetalle = "Reembolso de pago + liberar stock",
                });
            }

            return new SagaTimelineDto
            {
                id = pedido.id,
                strEstadoSaga = pedido.strEstadoSaga,
                strMotivoRechazo = pedido.strMotivoRechazo,
                lstEventos = eventos,
            };
        }

        private async Task<Dictionary<string, int>> GetQueueDepthAsync()
        {
            var transport = _configuration?.GetValue<string>("Transport");
            if (_sqs == null || !string.Equals(transport, "SQS", StringComparison.OrdinalIgnoreCase))
            {
                _logger.LogWarning(
                    "Profundidad de colas no disponible: el transporte actual es MassTransit InMemory. " +
                    "Con el transporte AmazonSQS (paso 2.11) se consultará ApproximateNumberOfMessages por cola.");
                return new Dictionary<string, int>();
            }

            // Prefijo por stack: CFN crea colas como "<stack>-pedidos.fifo" (cloudformation.yml:193,199,213,227).
            // Program.cs añade Scope("<stack>-", true) para endpoints MassTransit. Dashboard debe usar mismo prefijo.
            var stackPrefix = _configuration?.GetValue<string>("StackName")
                ?? _configuration?.GetValue<string>("STACK_NAME")
                ?? Environment.GetEnvironmentVariable("STACK_NAME")
                ?? Environment.GetEnvironmentVariable("StackName")
                ?? "webapidevsecops-prod";
            if (!stackPrefix.EndsWith("-", StringComparison.Ordinal))
                stackPrefix += "-";

            var baseQueueNames = new[] { "pedidos.fifo", "pedidos-pago.fifo", "pedidos-factura.fifo", "pedidos-dlq.fifo" };
            var queueNames = baseQueueNames.Select(q => $"{stackPrefix}{q}").ToArray();
            var result = new Dictionary<string, int>();

            foreach (var queueName in queueNames)
            {
                try
                {
                    string queueUrl;
                    try
                    {
                        var urlResp = await _sqs.GetQueueUrlAsync(queueName);
                        queueUrl = urlResp.QueueUrl;
                    }
                    catch (QueueDoesNotExistException)
                    {
                        var prefix = queueName.Split('.')[0];
                        var listResp = await _sqs.ListQueuesAsync(prefix);
                        queueUrl = listResp.QueueUrls.FirstOrDefault(u => u.EndsWith(queueName, StringComparison.Ordinal) || u.Contains(queueName, StringComparison.Ordinal))
                            ?? throw new QueueDoesNotExistException($"Queue {queueName} not found");
                    }

                    var attrResp = await _sqs.GetQueueAttributesAsync(new GetQueueAttributesRequest
                    {
                        QueueUrl = queueUrl,
                        AttributeNames = new List<string> { "ApproximateNumberOfMessages", "ApproximateNumberOfMessagesNotVisible" }
                    });

                    var visible = attrResp.Attributes.TryGetValue("ApproximateNumberOfMessages", out var v) && int.TryParse(v, out var iv) ? iv : 0;
                    var notVisible = attrResp.Attributes.TryGetValue("ApproximateNumberOfMessagesNotVisible", out var v2) && int.TryParse(v2, out var iv2) ? iv2 : 0;
                    // Clave corta para DashboardDto (sin prefijo stack) para estabilidad del contrato API
                    var shortKey = queueName.StartsWith(stackPrefix, StringComparison.Ordinal) ? queueName.Substring(stackPrefix.Length) : queueName;
                    result[shortKey] = visible + notVisible;
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Error consultando cola SQS {Queue}", queueName);
                    var shortKeyFallback = queueName.StartsWith(stackPrefix, StringComparison.Ordinal) ? queueName.Substring(stackPrefix.Length) : queueName;
                    result[shortKeyFallback] = 0;
                }
            }

            // Complemento B1: profundidad de colas MassTransit (standard, con prefijo stack) via ListQueues filtrando por stackPrefix
            try
            {
                var listAll = await _sqs.ListQueuesAsync(stackPrefix.TrimEnd('-'));
                foreach (var url in listAll.QueueUrls.Where(u => u.Contains(stackPrefix, StringComparison.Ordinal)))
                {
                    var name = url.Split('/').Last();
                    if (result.ContainsKey(name) || baseQueueNames.Contains(name) || baseQueueNames.Any(b => $"{stackPrefix}{b}" == name))
                        continue;
                    try
                    {
                        var attrResp = await _sqs.GetQueueAttributesAsync(new GetQueueAttributesRequest
                        {
                            QueueUrl = url,
                            AttributeNames = new List<string> { "ApproximateNumberOfMessages", "ApproximateNumberOfMessagesNotVisible" }
                        });
                        var visible = attrResp.Attributes.TryGetValue("ApproximateNumberOfMessages", out var v) && int.TryParse(v, out var iv) ? iv : 0;
                        var notVisible = attrResp.Attributes.TryGetValue("ApproximateNumberOfMessagesNotVisible", out var v2) && int.TryParse(v2, out var iv2) ? iv2 : 0;
                        var shortMt = name.StartsWith(stackPrefix, StringComparison.Ordinal) ? name.Substring(stackPrefix.Length) : name;
                        result[shortMt] = visible + notVisible;
                    }
                    catch { /* ignorar colas efímeras MT */ }
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Error listando colas MassTransit con prefijo {Prefix}", stackPrefix);
            }

            return result;
        }
    }
}
