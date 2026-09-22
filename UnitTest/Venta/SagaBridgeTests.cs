using FluentAssertions;
using MassTransit;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using UnitTest.Common;
using WebAPIDevSecOps.Consumers;
using WebAPIDevSecOps.Context;
using WebAPIDevSecOps.Dto;
using WebAPIDevSecOps.Events;
using WebAPIDevSecOps.Interfaces;
using WebAPIDevSecOps.Models;
using WebAPIDevSecOps.Services;

namespace UnitTest.Venta
{
    /// <summary>
    /// PostDespliegue9: criterios de aceptación del puente legacy -&gt; saga.
    /// BridgeOff = comportamiento legacy exacto. BridgeOn = dual-write + finalize PUT 1-&gt;2.
    /// </summary>
    public class SagaBridgeTests
    {
        private static DbResilienceService CreateDbResilience()
        {
            var options = Options.Create(new ResilienceOptions());
            var logger = new Mock<ILogger<DbResilienceService>>();
            return new DbResilienceService(options, logger.Object);
        }

        private static VentaService CreateVentaService(AppDbContext context, bool bridgeOn, Mock<IEventPublisher> publisher)
        {
            var config = bridgeOn ? SagaBridgeTestConfig.BridgeOn() : SagaBridgeTestConfig.BridgeOff();
            return new VentaService(context, CreateDbResilience(), publisher.Object, config,
                Mock.Of<ILogger<VentaService>>());
        }

        private static VentaDetalleService CreateDetalleService(AppDbContext context, bool bridgeOn, string username = "Test User")
        {
            var userMock = new Mock<IUserAccessor>();
            userMock.Setup(u => u.GetCurrentUsername()).Returns(username);
            var config = bridgeOn ? SagaBridgeTestConfig.BridgeOn() : SagaBridgeTestConfig.BridgeOff();
            return new VentaDetalleService(context, CreateDbResilience(), userMock.Object, config,
                Mock.Of<ILogger<VentaDetalleService>>());
        }

        private static async Task<(CliCliente cliente, SegUsuario usuario, ProProducto producto)> SeedAsync(AppDbContext context)
        {
            var cliente = new CliCliente
            {
                strNombreCliente = "Bridge Cliente",
                strCorreoElectronico = $"bridge{Guid.NewGuid():N}@test.com",
                strNumeroTelefono = "5512345678",
            };
            var usuario = new SegUsuario
            {
                strNombre = "Bridge User",
                strPWD = "hash",
                strCorreoElectronico = $"buser{Guid.NewGuid():N}@test.com",
            };
            context.CliCliente.Add(cliente);
            context.SegUsuario.Add(usuario);
            if (!await context.VenCatEstado.AnyAsync())
            {
                context.VenCatEstado.AddRange(
                    new VenCatEstado { id = 1, strValor = "En compra usuario", strDescripcion = "Se realiza la compra por el usuario" },
                    new VenCatEstado { id = 2, strValor = "Fin compra usuario", strDescripcion = "Se concluye compra por usuario" });
            }
            var producto = new ProProducto
            {
                strNombreProducto = "Bridge Producto",
                intNumeroExistencia = 10,
                decPrecio = 100m,
            };
            context.ProProducto.Add(producto);
            await context.SaveChangesAsync();
            return (cliente, usuario, producto);
        }

        [Fact]
        public async Task Create_BridgeOn_CreaEspejoPendiente_SinPublicar()
        {
            var context = DbContextMock.GetDbContext();
            var (cliente, usuario, _) = await SeedAsync(context);
            var publisher = new Mock<IEventPublisher>();
            var service = CreateVentaService(context, bridgeOn: true, publisher);

            var venta = await service.CreateAsync(new VenVentaCreateDto { idCliCliente = cliente.id, idSegUsuario = usuario.id });

            var pedido = await context.VenPedido.AsNoTracking().FirstOrDefaultAsync(p => p.LegacyVentaId == venta.id);
            pedido.Should().NotBeNull();
            pedido!.strEstadoSaga.Should().Be("Pendiente");
            pedido.decTotal.Should().Be(0);
            publisher.Verify(p => p.PublishAsync(It.IsAny<PedidoCreadoEvent>()), Times.Never);
        }

        [Fact]
        public async Task Create_BridgeOff_NoCreaEspejo_LegacyExacto()
        {
            var context = DbContextMock.GetDbContext();
            var (cliente, usuario, _) = await SeedAsync(context);
            var publisher = new Mock<IEventPublisher>();
            var service = CreateVentaService(context, bridgeOn: false, publisher);

            var venta = await service.CreateAsync(new VenVentaCreateDto { idCliCliente = cliente.id, idSegUsuario = usuario.id });

            (await context.VenPedido.CountAsync()).Should().Be(0);
            (await context.VenVenta.CountAsync()).Should().Be(1);
            publisher.Verify(p => p.PublishAsync(It.IsAny<PedidoCreadoEvent>()), Times.Never);
        }

        [Fact]
        public async Task Detalle_BridgeOn_AcumulaSinDescontarStock()
        {
            var context = DbContextMock.GetDbContext();
            var (cliente, usuario, producto) = await SeedAsync(context);
            var publisher = new Mock<IEventPublisher>();
            var ventaService = CreateVentaService(context, bridgeOn: true, publisher);
            var detalleService = CreateDetalleService(context, bridgeOn: true);

            var venta = await ventaService.CreateAsync(new VenVentaCreateDto { idCliCliente = cliente.id, idSegUsuario = usuario.id });
            await detalleService.CreateAsync(new VenVentaDetalleCreateDto { idVenVenta = venta.id, idProProducto = producto.id, intPiezaVenta = 2 });

            var pedido = await context.VenPedido.Include(p => p.Detalles).FirstAsync(p => p.LegacyVentaId == venta.id);
            pedido.Detalles.Should().HaveCount(1);
            pedido.Detalles.First().intCantidad.Should().Be(2);
            pedido.decTotal.Should().Be(200m);

            var productoDb = await context.ProProducto.AsNoTracking().FirstAsync(p => p.id == producto.id);
            productoDb.intNumeroExistencia.Should().Be(10, "la saga es el único propietario del decremento");
            publisher.Verify(p => p.PublishAsync(It.IsAny<PedidoCreadoEvent>()), Times.Never);
        }

        [Fact]
        public async Task Detalle_BridgeOn_SinEspejo_LanzaInvalidOperation()
        {
            var context = DbContextMock.GetDbContext();
            var (_, _, producto) = await SeedAsync(context);
            var detalleService = CreateDetalleService(context, bridgeOn: true);

            // Venta legacy insertada directo (sin pasar por VentaService): no hay espejo.
            var venta = new VenVenta
            {
                idCliCliente = 1,
                idSegUsuario = 1,
                idVenCatEstado = 1,
                dteFechaHoraCompra = DateTime.UtcNow,
                strClaveVenta = $"B{Guid.NewGuid():N}"[..10],
            };
            context.VenVenta.Add(venta);
            await context.SaveChangesAsync();

            var act = () => detalleService.CreateAsync(new VenVentaDetalleCreateDto
            {
                idVenVenta = venta.id,
                idProProducto = producto.id,
                intPiezaVenta = 1,
            });

            await act.Should().ThrowAsync<InvalidOperationException>();
        }

        [Fact]
        public async Task Finalize_PUT2_PublicaUnEvento_ConTotalRecalculado()
        {
            var context = DbContextMock.GetDbContext();
            var (cliente, usuario, producto) = await SeedAsync(context);
            var publisher = new Mock<IEventPublisher>();
            var ventaService = CreateVentaService(context, bridgeOn: true, publisher);
            var detalleService = CreateDetalleService(context, bridgeOn: true);

            var venta = await ventaService.CreateAsync(new VenVentaCreateDto { idCliCliente = cliente.id, idSegUsuario = usuario.id });
            await detalleService.CreateAsync(new VenVentaDetalleCreateDto { idVenVenta = venta.id, idProProducto = producto.id, intPiezaVenta = 2 });

            await ventaService.UpdateAsync(venta.id, new VenVentaUpdateDto
            {
                id = venta.id,
                idCliCliente = cliente.id,
                idSegUsuario = usuario.id,
                idVenCatEstado = 2,
            });

            publisher.Verify(p => p.PublishAsync(It.Is<PedidoCreadoEvent>(e =>
                e.Total == 200m && e.Detalles.Count == 1 && e.Detalles[0].intCantidad == 2)), Times.Once);
        }

        [Fact]
        public async Task Finalize_DoblePUT2_PublicaUnaSolaVez()
        {
            var context = DbContextMock.GetDbContext();
            var (cliente, usuario, producto) = await SeedAsync(context);
            var publisher = new Mock<IEventPublisher>();
            var ventaService = CreateVentaService(context, bridgeOn: true, publisher);
            var detalleService = CreateDetalleService(context, bridgeOn: true);

            var venta = await ventaService.CreateAsync(new VenVentaCreateDto { idCliCliente = cliente.id, idSegUsuario = usuario.id });
            await detalleService.CreateAsync(new VenVentaDetalleCreateDto { idVenVenta = venta.id, idProProducto = producto.id, intPiezaVenta = 1 });

            var dto = new VenVentaUpdateDto
            {
                id = venta.id,
                idCliCliente = cliente.id,
                idSegUsuario = usuario.id,
                idVenCatEstado = 2,
            };
            await ventaService.UpdateAsync(venta.id, dto);
            await ventaService.UpdateAsync(venta.id, dto);

            publisher.Verify(p => p.PublishAsync(It.IsAny<PedidoCreadoEvent>()), Times.Once);
        }

        [Fact]
        public async Task Finalize_PUT1_NoPublica()
        {
            var context = DbContextMock.GetDbContext();
            var (cliente, usuario, producto) = await SeedAsync(context);
            var publisher = new Mock<IEventPublisher>();
            var ventaService = CreateVentaService(context, bridgeOn: true, publisher);
            var detalleService = CreateDetalleService(context, bridgeOn: true);

            var venta = await ventaService.CreateAsync(new VenVentaCreateDto { idCliCliente = cliente.id, idSegUsuario = usuario.id });
            await detalleService.CreateAsync(new VenVentaDetalleCreateDto { idVenVenta = venta.id, idProProducto = producto.id, intPiezaVenta = 1 });

            await ventaService.UpdateAsync(venta.id, new VenVentaUpdateDto
            {
                id = venta.id,
                idCliCliente = cliente.id,
                idSegUsuario = usuario.id,
                idVenCatEstado = 1,
            });

            publisher.Verify(p => p.PublishAsync(It.IsAny<PedidoCreadoEvent>()), Times.Never);
        }

        [Fact]
        public async Task Finalize_PUT2_SinDetalles_Rechaza()
        {
            var context = DbContextMock.GetDbContext();
            var (cliente, usuario, _) = await SeedAsync(context);
            var publisher = new Mock<IEventPublisher>();
            var ventaService = CreateVentaService(context, bridgeOn: true, publisher);

            var venta = await ventaService.CreateAsync(new VenVentaCreateDto { idCliCliente = cliente.id, idSegUsuario = usuario.id });

            var act = () => ventaService.UpdateAsync(venta.id, new VenVentaUpdateDto
            {
                id = venta.id,
                idCliCliente = cliente.id,
                idSegUsuario = usuario.id,
                idVenCatEstado = 2,
            });

            await act.Should().ThrowAsync<ArgumentException>();
            publisher.Verify(p => p.PublishAsync(It.IsAny<PedidoCreadoEvent>()), Times.Never);
        }

        [Fact]
        public async Task Consumer_DobleConsume_Idempotente()
        {
            var context = DbContextMock.GetDbContext();
            var (_, _, producto) = await SeedAsync(context);
            var pedidoId = Guid.NewGuid();
            context.VenPedido.Add(new VenPedido
            {
                id = pedidoId,
                idCliCliente = 1,
                dteFechaPedido = DateTime.UtcNow,
                decTotal = 300m,
                strEstadoSaga = "Pendiente",
                Detalles = new List<VenPedidoDetalle>
                {
                    new() { idVenPedido = pedidoId, idProProducto = producto.id, intCantidad = 3, decPrecioUnitario = 100m },
                },
            });
            await context.SaveChangesAsync();

            var consumer = new StockValidatorConsumer(context, CreateDbResilience(), Mock.Of<ILogger<StockValidatorConsumer>>());
            var evento = new PedidoCreadoEvent
            {
                PedidoId = pedidoId,
                ClienteId = 1,
                Total = 300m,
                Detalles = new List<PedidoCreadoDetalleItem>
                {
                    new() { idProProducto = producto.id, intCantidad = 3, decPrecioUnitario = 100m },
                },
                FechaCreacion = DateTime.UtcNow,
            };

            var ctxMock1 = new Mock<ConsumeContext<PedidoCreadoEvent>>();
            ctxMock1.SetupGet(c => c.Message).Returns(evento);
            await consumer.Consume(ctxMock1.Object);

            var ctxMock2 = new Mock<ConsumeContext<PedidoCreadoEvent>>();
            ctxMock2.SetupGet(c => c.Message).Returns(evento);
            await consumer.Consume(ctxMock2.Object);

            var productoDb = await context.ProProducto.AsNoTracking().FirstAsync(p => p.id == producto.id);
            productoDb.intNumeroExistencia.Should().Be(7, "el redelivery no debe redecrementar");
            ctxMock1.Verify(c => c.Publish(It.IsAny<StockValidadoEvent>(), It.IsAny<CancellationToken>()), Times.Once);
            ctxMock2.Verify(c => c.Publish(It.IsAny<StockValidadoEvent>(), It.IsAny<CancellationToken>()), Times.Never);
        }

        [Fact]
        public async Task Consumer_Concurrente_MismoProducto_SoloUnoValida()
        {
            var dbName = $"bridge-conc-{Guid.NewGuid():N}";
            AppDbContext NewCtx()
            {
                var options = new DbContextOptionsBuilder<AppDbContext>()
                    .UseInMemoryDatabase(dbName)
                    .EnableSensitiveDataLogging()
                    .Options;
                var ctx = new TestDbContext(options);
                ctx.Database.EnsureCreated();
                return ctx;
            }

            using var seed = NewCtx();
            var cliente = new CliCliente { strNombreCliente = "Conc", strCorreoElectronico = $"conc{Guid.NewGuid():N}@test.com", strNumeroTelefono = "5512345678" };
            seed.CliCliente.Add(cliente);
            var producto = new ProProducto { strNombreProducto = "Conc Prod", intNumeroExistencia = 5, decPrecio = 10m };
            seed.ProProducto.Add(producto);
            await seed.SaveChangesAsync();

            var pedidoA = Guid.NewGuid();
            var pedidoB = Guid.NewGuid();
            seed.VenPedido.Add(new VenPedido
            {
                id = pedidoA,
                idCliCliente = cliente.id,
                dteFechaPedido = DateTime.UtcNow,
                decTotal = 50m,
                strEstadoSaga = "Pendiente",
                Detalles = new List<VenPedidoDetalle>
                {
                    new() { idVenPedido = pedidoA, idProProducto = producto.id, intCantidad = 5, decPrecioUnitario = 10m },
                },
            });
            seed.VenPedido.Add(new VenPedido
            {
                id = pedidoB,
                idCliCliente = cliente.id,
                dteFechaPedido = DateTime.UtcNow,
                decTotal = 50m,
                strEstadoSaga = "Pendiente",
                Detalles = new List<VenPedidoDetalle>
                {
                    new() { idVenPedido = pedidoB, idProProducto = producto.id, intCantidad = 5, decPrecioUnitario = 10m },
                },
            });
            await seed.SaveChangesAsync();

            PedidoCreadoEvent Evento(Guid id) => new()
            {
                PedidoId = id,
                ClienteId = cliente.id,
                Total = 50m,
                Detalles = new List<PedidoCreadoDetalleItem>
                {
                    new() { idProProducto = producto.id, intCantidad = 5, decPrecioUnitario = 10m },
                },
                FechaCreacion = DateTime.UtcNow,
            };

            using var ctxA = NewCtx();
            using var ctxB = NewCtx();
            var consumerA = new StockValidatorConsumer(ctxA, CreateDbResilience(), Mock.Of<ILogger<StockValidatorConsumer>>());
            var consumerB = new StockValidatorConsumer(ctxB, CreateDbResilience(), Mock.Of<ILogger<StockValidatorConsumer>>());
            var mockA = new Mock<ConsumeContext<PedidoCreadoEvent>>();
            mockA.SetupGet(c => c.Message).Returns(Evento(pedidoA));
            var mockB = new Mock<ConsumeContext<PedidoCreadoEvent>>();
            mockB.SetupGet(c => c.Message).Returns(Evento(pedidoB));

            await Task.WhenAll(consumerA.Consume(mockA.Object), consumerB.Consume(mockB.Object));

            using var verify = NewCtx();
            var stock = (await verify.ProProducto.AsNoTracking().FirstAsync(p => p.id == producto.id)).intNumeroExistencia;
            stock.Should().Be(0);
            var estados = await verify.VenPedido.AsNoTracking()
                .Where(p => p.id == pedidoA || p.id == pedidoB)
                .Select(p => p.strEstadoSaga)
                .ToListAsync();
            estados.Should().ContainSingle(s => s == "StockValidado");
            estados.Should().ContainSingle(s => s == "StockRechazado");
        }
    }
}
