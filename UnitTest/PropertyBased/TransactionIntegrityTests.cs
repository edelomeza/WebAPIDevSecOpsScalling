using FsCheck;
using FsCheck.Xunit;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using UnitTest.Common;
using WebAPIDevSecOps.Context;
using WebAPIDevSecOps.Dto;
using WebAPIDevSecOps.Interfaces;
using WebAPIDevSecOps.Models;
using WebAPIDevSecOps.Services;

namespace UnitTest.PropertyBased;

public class TransactionIntegrityTests
{
    private static AppDbContext CreateContext()
    {
        return DbContextMock.GetDbContext();
    }

    private static DbResilienceService CreateDbResilience()
    {
        var options = Options.Create(new ResilienceOptions
        {
            FailureRatio = 1.0,
            MinimumThroughput = 2,
            SamplingDurationSeconds = 60,
            BreakDurationSeconds = 5
        });
        var logger = new Mock<ILogger<DbResilienceService>>();
        return new DbResilienceService(options, logger.Object);
    }

    private static bool IsValidProductoName(string? name)
    {
        return !string.IsNullOrWhiteSpace(name) &&
               name.Length <= 50 &&
               System.Text.RegularExpressions.Regex.IsMatch(name, @"^[a-zA-Z0-9áéíóúÁÉÍÓÚñÑ ]+$");
    }

    private static Mock<ICacheService> CreateCacheMock()
    {
        var mock = new Mock<ICacheService>();

        mock.Setup(c => c.GetOrCreateAsync(
                It.IsAny<string>(),
                It.IsAny<Func<Task<PagedResult<ProProductoDto>>>>(),
                It.IsAny<TimeSpan?>()))
            .Returns<string, Func<Task<PagedResult<ProProductoDto>>>, TimeSpan?>(
                (_, factory, _) => factory());

        mock.Setup(c => c.GetAsync<ProProductoDto>(It.IsAny<string>()))
            .ReturnsAsync((ProProductoDto?)null);

        mock.Setup(c => c.SetAsync(It.IsAny<string>(), It.IsAny<ProProductoDto>(), It.IsAny<TimeSpan?>()))
            .Returns(Task.CompletedTask);

        mock.Setup(c => c.GetOrCreateAsync(
                It.IsAny<string>(),
                It.IsAny<Func<Task<ProProductoDto?>>>(),
                It.IsAny<TimeSpan?>()))
            .Returns<string, Func<Task<ProProductoDto?>>, TimeSpan?>(
                (_, factory, _) => factory());

        mock.Setup(c => c.RemoveAsync(It.IsAny<string>()))
            .Returns(Task.CompletedTask);

        return mock;
    }

    [Property]
    public async Task<bool> Producto_CreateAndGet_ValuesMatch(
        string nombre,
        int stock,
        decimal precio)
    {
        if (!IsValidProductoName(nombre) || stock < 0 || precio < 0.01m || precio > 9999999.99m)
            return true;

        var context = CreateContext();
        try
        {
            var dbResilience = CreateDbResilience();
            var cacheMock = CreateCacheMock();
            var userMock = new Mock<IUserAccessor>();
            userMock.Setup(u => u.GetCurrentUsername()).Returns("pbtester");

            var service = new ProductoService(context, dbResilience, cacheMock.Object, userMock.Object);

            var dto = new ProductoCreateDto
            {
                strNombreProducto = nombre,
                intNumeroExistencia = stock,
                decPrecio = precio,
            };

            var created = await service.CreateAsync(dto);
            var fetched = await service.GetByIdAsync(created.id);

            return fetched != null &&
                   fetched.strNombreProducto == created.strNombreProducto &&
                   fetched.intNumeroExistencia == created.intNumeroExistencia &&
                   fetched.decPrecio == created.decPrecio;
        }
        catch
        {
            return false;
        }
        finally
        {
            await context.DisposeAsync();
        }
    }

    [Property]
    public async Task<bool> Producto_CreateUpdateGet_ValuesMatch(
        string nombreOriginal,
        int stockOriginal,
        decimal precioOriginal,
        string nombreActualizado,
        decimal precioActualizado)
    {
        if (!IsValidProductoName(nombreOriginal) || stockOriginal < 0 ||
            precioOriginal < 0.01m || precioOriginal > 9999999.99m)
            return true;

        if (!IsValidProductoName(nombreActualizado) || precioActualizado < 0.01m || precioActualizado > 9999999.99m)
            return true;

        var context = CreateContext();
        try
        {
            var dbResilience = CreateDbResilience();
            var cacheMock = CreateCacheMock();
            var userMock = new Mock<IUserAccessor>();
            userMock.Setup(u => u.GetCurrentUsername()).Returns("pbtester");

            var service = new ProductoService(context, dbResilience, cacheMock.Object, userMock.Object);

            var createDto = new ProductoCreateDto
            {
                strNombreProducto = nombreOriginal,
                intNumeroExistencia = stockOriginal,
                decPrecio = precioOriginal,
            };

            var created = await service.CreateAsync(createDto);

            var updateDto = new ProductoUpdateDto
            {
                id = created.id,
                strNombreProducto = nombreActualizado,
                intNumeroExistencia = stockOriginal + 1,
                decPrecio = precioActualizado,
                RowVersion = created.RowVersion,
            };

            await service.UpdateAsync(created.id, updateDto);
            var fetched = await service.GetByIdAsync(created.id);

            return fetched != null &&
                   fetched.strNombreProducto == updateDto.strNombreProducto &&
                   fetched.intNumeroExistencia == updateDto.intNumeroExistencia &&
                   fetched.decPrecio == updateDto.decPrecio;
        }
        catch
        {
            return false;
        }
        finally
        {
            await context.DisposeAsync();
        }
    }

    [Property]
    public async Task<bool> Producto_CreateDeleteGet_ReturnsNull(
        string nombre,
        int stock,
        decimal precio)
    {
        if (!IsValidProductoName(nombre) || stock < 0 || precio < 0.01m || precio > 9999999.99m)
            return true;

        var context = CreateContext();
        try
        {
            var dbResilience = CreateDbResilience();
            var cacheMock = CreateCacheMock();
            var userMock = new Mock<IUserAccessor>();
            userMock.Setup(u => u.GetCurrentUsername()).Returns("pbtester");

            var service = new ProductoService(context, dbResilience, cacheMock.Object, userMock.Object);

            var dto = new ProductoCreateDto
            {
                strNombreProducto = nombre,
                intNumeroExistencia = stock,
                decPrecio = precio,
            };

            var created = await service.CreateAsync(dto);

            var deleteDto = new ProductoDeleteDto
            {
                id = created.id,
                RowVersion = created.RowVersion,
            };

            await service.DeleteAsync(created.id, deleteDto);
            var fetched = await service.GetByIdAsync(created.id);

            return fetched == null;
        }
        catch
        {
            return false;
        }
        finally
        {
            await context.DisposeAsync();
        }
    }

    [Property]
    public async Task<bool> VentaDetalle_StockInvariant_NeverNegative(
        byte initialStock,
        byte[] quantities)
    {
        if (quantities.Length == 0)
            return true;

        if (initialStock == 0)
            return true;

        var context = CreateContext();
        try
        {
            var dbResilience = CreateDbResilience();

            context.VenCatEstado.Add(new VenCatEstado { id = 1, strValor = "Activa" });
            context.CliCliente.Add(new CliCliente
            {
                strNombreCliente = "pbtest",
                strCorreoElectronico = "pb@test.com",
                strNumeroTelefono = "5512345678",
            });
            context.SegUsuario.Add(new SegUsuario
            {
                strNombre = "pbuser",
                strCorreoElectronico = "pbuser@test.com",
                strPWD = "hash",
            });
            var producto = new ProProducto
            {
                strNombreProducto = "pbproducto",
                intNumeroExistencia = initialStock,
                decPrecio = 10m,
            };
            context.ProProducto.Add(producto);
            context.SaveChanges();

            var ventaService = new VentaService(context, dbResilience);
            var detalleService = new VentaDetalleService(context, dbResilience);

            var ventaDto = new VenVentaCreateDto
            {
                idCliCliente = 1,
                idSegUsuario = 1,
            };
            var venta = await ventaService.CreateAsync(ventaDto);

            var expectedStock = (int)initialStock;

            foreach (var qty in quantities)
            {
                if (qty == 0) continue;

                var detalleDto = new VenVentaDetalleCreateDto
                {
                    idVenVenta = venta.id,
                    idProProducto = producto.id,
                    intPiezaVenta = qty,
                };

                if (qty <= expectedStock)
                {
                    try
                    {
                        await detalleService.CreateAsync(detalleDto);
                        expectedStock -= qty;
                    }
                    catch
                    {
                        return false;
                    }
                }
                else
                {
                    try
                    {
                        await detalleService.CreateAsync(detalleDto);
                        return false;
                    }
                    catch (ArgumentException)
                    {
                    }
                }

                var current = await context.ProProducto.FindAsync(producto.id);
                if (current!.intNumeroExistencia < 0)
                    return false;
            }

            var final = await context.ProProducto.FindAsync(producto.id);
            return final!.intNumeroExistencia == expectedStock;
        }
        catch
        {
            return false;
        }
        finally
        {
            await context.DisposeAsync();
        }
    }

    [Property]
    public async Task<bool> Producto_GetAll_IncludesCreated(
        string nombre,
        int stock,
        decimal precio)
    {
        if (!IsValidProductoName(nombre) || stock < 0 || precio < 0.01m || precio > 9999999.99m)
            return true;

        var context = CreateContext();
        try
        {
            var dbResilience = CreateDbResilience();
            var cacheMock = CreateCacheMock();
            var userMock = new Mock<IUserAccessor>();
            userMock.Setup(u => u.GetCurrentUsername()).Returns("pbtester");

            var service = new ProductoService(context, dbResilience, cacheMock.Object, userMock.Object);

            var dto = new ProductoCreateDto
            {
                strNombreProducto = nombre,
                intNumeroExistencia = stock,
                decPrecio = precio,
            };

            await service.CreateAsync(dto);
            var all = await service.GetAllAsync(new QueryParams { PageSize = 100 });

            return all.Items.Any(p => p.strNombreProducto == nombre);
        }
        catch
        {
            return false;
        }
        finally
        {
            await context.DisposeAsync();
        }
    }
}
