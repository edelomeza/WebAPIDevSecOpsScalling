using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using UnitTest.Common;
using WebAPIDevSecOps.Context;
using WebAPIDevSecOps.Dto;
using WebAPIDevSecOps.Events;
using WebAPIDevSecOps.Interfaces;
using WebAPIDevSecOps.Models;
using WebAPIDevSecOps.Services;

namespace UnitTest.VentasPedido
{
    public class VentasPedidoServiceTests
    {
        private readonly DbResilienceService _dbResilience;

        public VentasPedidoServiceTests()
        {
            _dbResilience = CreateDbResilience();
        }

        private static DbResilienceService CreateDbResilience()
        {
            var options = Options.Create(new ResilienceOptions());
            var logger = new Mock<ILogger<DbResilienceService>>();
            return new DbResilienceService(options, logger.Object);
        }

        private static VentasPedidoService CreateService(AppDbContext context, out Mock<IEventPublisher> eventPublisherMock)
        {
            eventPublisherMock = new Mock<IEventPublisher>();
            return new VentasPedidoService(context, CreateDbResilience(), eventPublisherMock.Object);
        }

        private static async Task<(CliCliente cliente, ProProducto producto1, ProProducto producto2)> SeedDependenciesAsync(AppDbContext context)
        {
            var cliente = TestDataFactory.CreateCliente();
            var producto1 = TestDataFactory.CreateProducto(nombre: "Producto Uno", existencia: 10, precio: 100m);
            var producto2 = TestDataFactory.CreateProducto(nombre: "Producto Dos", existencia: 10, precio: 50m);
            context.CliCliente.Add(cliente);
            context.ProProducto.AddRange(producto1, producto2);
            await context.SaveChangesAsync();
            return (cliente, producto1, producto2);
        }

        private static PedidoCreateDto CreatePedidoDto(int idCliCliente, int idProducto, int cantidad = 1)
        {
            return new PedidoCreateDto
            {
                idCliCliente = idCliCliente,
                Detalles = new List<PedidoDetalleCreateDto>
                {
                    new() { idProProducto = idProducto, intCantidad = cantidad },
                },
            };
        }

        [Fact]
        public async Task CrearPedido_ClienteNoExiste_ThrowsWithMessage()
        {
            var context = DbContextMock.GetDbContext();
            var service = CreateService(context, out _);
            var dto = CreatePedidoDto(9999, 1);

            var act = () => service.CrearPedidoAsync(dto);

            await act.Should().ThrowAsync<ArgumentException>()
                .WithMessage("El cliente especificado no existe.");
        }

        [Fact]
        public async Task CrearPedido_ProductoNoExiste_ThrowsWithMessage()
        {
            var context = DbContextMock.GetDbContext();
            var (cliente, _, _) = await SeedDependenciesAsync(context);
            var service = CreateService(context, out _);
            var dto = CreatePedidoDto(cliente.id, 9999);

            var act = () => service.CrearPedidoAsync(dto);

            await act.Should().ThrowAsync<ArgumentException>()
                .WithMessage("El producto con ID 9999 no existe.");
        }

        [Fact]
        public async Task CrearPedido_EventoContieneDetallesYTotal()
        {
            var context = DbContextMock.GetDbContext();
            var (cliente, producto1, producto2) = await SeedDependenciesAsync(context);
            var service = CreateService(context, out var eventPublisherMock);
            var dto = new PedidoCreateDto
            {
                idCliCliente = cliente.id,
                Detalles = new List<PedidoDetalleCreateDto>
                {
                    new() { idProProducto = producto1.id, intCantidad = 2 },
                    new() { idProProducto = producto2.id, intCantidad = 1 },
                },
            };

            var result = await service.CrearPedidoAsync(dto);

            result.Should().NotBeNull();
            result.strEstadoSaga.Should().Be("Pendiente");
            result.decTotal.Should().Be(250m);
            result.Detalles.Should().HaveCount(2);
            eventPublisherMock.Verify(
                p => p.PublishAsync(It.Is<PedidoCreadoEvent>(e =>
                    e.PedidoId == result.id
                    && e.ClienteId == cliente.id
                    && e.Total == 250m
                    && e.Detalles.Count == 2
                    && e.Detalles.Any(d => d.idProProducto == producto1.id && d.intCantidad == 2 && d.decPrecioUnitario == 100m)
                    && e.Detalles.Any(d => d.idProProducto == producto2.id && d.intCantidad == 1 && d.decPrecioUnitario == 50m)
                    && e.FechaCreacion != default)),
                Times.Once());
        }

        [Fact]
        public async Task GetById_ConRelaciones_DevuelveNombresYDetalles()
        {
            var context = DbContextMock.GetDbContext();
            var (cliente, producto1, producto2) = await SeedDependenciesAsync(context);
            var pedido = TestDataFactory.CreatePedido(Guid.NewGuid(), cliente.id);
            context.VenPedido.Add(pedido);
            await context.SaveChangesAsync();
            context.VenPedidoDetalle.AddRange(
                new VenPedidoDetalle
                {
                    idVenPedido = pedido.id,
                    idProProducto = producto1.id,
                    intCantidad = 2,
                    decPrecioUnitario = 100m,
                    RowVersion = new byte[] { 1, 0, 0, 0 },
                },
                new VenPedidoDetalle
                {
                    idVenPedido = pedido.id,
                    idProProducto = producto2.id,
                    intCantidad = 1,
                    decPrecioUnitario = 50m,
                    RowVersion = new byte[] { 1, 0, 0, 0 },
                });
            await context.SaveChangesAsync();
            var service = CreateService(context, out _);

            var result = await service.GetByIdAsync(pedido.id);

            result.Should().NotBeNull();
            result!.strNombreCliente.Should().Be(cliente.strNombreCliente);
            result.Detalles.Should().HaveCount(2);
            result.Detalles.Should().Contain(d => d.idProProducto == producto1.id && d.strNombreProducto == "Producto Uno" && d.intCantidad == 2 && d.decPrecioUnitario == 100m);
            result.Detalles.Should().Contain(d => d.idProProducto == producto2.id && d.strNombreProducto == "Producto Dos" && d.intCantidad == 1 && d.decPrecioUnitario == 50m);
        }

        [Fact]
        public async Task GetAll_OrdenDescendente_DevuelveMasRecientePrimero()
        {
            var context = DbContextMock.GetDbContext();
            var cliente = TestDataFactory.CreateCliente();
            context.CliCliente.Add(cliente);
            await context.SaveChangesAsync();
            var pedido1 = TestDataFactory.CreatePedido(Guid.NewGuid(), cliente.id);
            pedido1.dteFechaPedido = new DateTime(2026, 1, 1, 10, 0, 0, DateTimeKind.Utc);
            var pedido2 = TestDataFactory.CreatePedido(Guid.NewGuid(), cliente.id);
            pedido2.dteFechaPedido = new DateTime(2026, 2, 1, 10, 0, 0, DateTimeKind.Utc);
            var pedido3 = TestDataFactory.CreatePedido(Guid.NewGuid(), cliente.id);
            pedido3.dteFechaPedido = new DateTime(2026, 3, 1, 10, 0, 0, DateTimeKind.Utc);
            context.VenPedido.AddRange(pedido1, pedido2, pedido3);
            await context.SaveChangesAsync();
            var service = CreateService(context, out _);

            var result = await service.GetAllAsync();

            result.TotalCount.Should().Be(3);
            result.Items.Select(p => p.dteFechaPedido).Should().BeInDescendingOrder();
            result.Items.First().id.Should().Be(pedido3.id);
        }

        [Fact]
        public async Task GetAll_ConDetalles_PopulaDetallesYProductos()
        {
            var context = DbContextMock.GetDbContext();
            var (cliente, producto1, producto2) = await SeedDependenciesAsync(context);
            var pedido = TestDataFactory.CreatePedido(Guid.NewGuid(), cliente.id);
            context.VenPedido.Add(pedido);
            await context.SaveChangesAsync();
            context.VenPedidoDetalle.AddRange(
                new VenPedidoDetalle
                {
                    idVenPedido = pedido.id,
                    idProProducto = producto1.id,
                    intCantidad = 2,
                    decPrecioUnitario = 100m,
                    RowVersion = new byte[] { 1, 0, 0, 0 },
                },
                new VenPedidoDetalle
                {
                    idVenPedido = pedido.id,
                    idProProducto = producto2.id,
                    intCantidad = 1,
                    decPrecioUnitario = 50m,
                    RowVersion = new byte[] { 1, 0, 0, 0 },
                });
            await context.SaveChangesAsync();
            var service = CreateService(context, out _);

            var result = await service.GetAllAsync();

            var item = result.Items.Should().ContainSingle().Subject;
            item.strNombreCliente.Should().Be(cliente.strNombreCliente);
            item.Detalles.Should().HaveCount(2);
            item.Detalles.Should().Contain(d => d.idProProducto == producto1.id && d.strNombreProducto == "Producto Uno");
            item.Detalles.Should().Contain(d => d.idProProducto == producto2.id && d.strNombreProducto == "Producto Dos");
        }

        [Fact]
        public async Task GetAll_ConRelaciones_DevuelveNombresYDetalles()
        {
            var context = DbContextMock.GetDbContext();
            var (cliente, producto1, producto2) = await SeedDependenciesAsync(context);
            var pedido = TestDataFactory.CreatePedido(Guid.NewGuid(), cliente.id);
            context.VenPedido.Add(pedido);
            await context.SaveChangesAsync();
            context.VenPedidoDetalle.AddRange(
                new VenPedidoDetalle
                {
                    idVenPedido = pedido.id,
                    idProProducto = producto1.id,
                    intCantidad = 2,
                    decPrecioUnitario = 100m,
                    RowVersion = new byte[] { 1, 0, 0, 0 },
                },
                new VenPedidoDetalle
                {
                    idVenPedido = pedido.id,
                    idProProducto = producto2.id,
                    intCantidad = 1,
                    decPrecioUnitario = 50m,
                    RowVersion = new byte[] { 1, 0, 0, 0 },
                });
            await context.SaveChangesAsync();
            var service = CreateService(context, out _);

            var result = await service.GetAllAsync();

            var item = result.Items.Should().ContainSingle().Subject;
            item.strNombreCliente.Should().Be(cliente.strNombreCliente);
            item.strEstadoSaga.Should().Be("Pendiente");
            item.decTotal.Should().Be(pedido.decTotal);
            item.Detalles.Should().HaveCount(2);
            item.Detalles.Should().Contain(d => d.idProProducto == producto1.id && d.strNombreProducto == "Producto Uno");
            item.Detalles.Should().Contain(d => d.idProProducto == producto2.id && d.strNombreProducto == "Producto Dos");
        }
    }
}
