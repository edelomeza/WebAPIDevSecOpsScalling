using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using UnitTest.Common;
using WebAPIDevSecOps.Context;
using WebAPIDevSecOps.Models;

namespace UnitTest.Controllers;

public class RaceConditionTests
{
    [Fact]
    public async Task VentaDetalleParalelo_StockUno_SoloUnExito()
    {
        using var factory = new Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactory<Program>()
            .WithWebHostBuilder(builder =>
            {
                builder.UseSetting("ConnectionStrings:DefaultConnection", "Server=.;Database=Test;Trusted_Connection=True;");
                builder.UseSetting("Jwt:Key", JwtTestConfig.Key);
                builder.UseSetting("Jwt:Issuer", JwtTestConfig.Issuer);
                builder.UseSetting("Jwt:Audience", JwtTestConfig.Audience);
                builder.UseSetting("UseInMemoryDatabase", "true");
                builder.UseSetting("InMemoryDatabaseName", $"RaceTest_{Guid.NewGuid():N}");
            });
        var client = factory.CreateClient();

        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        db.VenCatEstado.Add(new VenCatEstado
        {
            id = 1,
            strValor = "Activa",
        });
        db.ProProducto.Add(new ProProducto
        {
            strNombreProducto = "productorace",
            intNumeroExistencia = 1,
            decPrecio = 50m,
        });
        db.CliCliente.Add(new CliCliente
        {
            strNombreCliente = "clienteunico",
            strCorreoElectronico = "clienteunico@test.com",
            strNumeroTelefono = "5512345678",
        });
        db.SegUsuario.Add(new SegUsuario
        {
            strNombre = "admin",
            strCorreoElectronico = "usuariorace@test.com",
            strPWD = "hash",
        });
        db.SaveChanges();

        var producto = db.ProProducto.First(p => p.strNombreProducto == "productorace");
        var cliente = db.CliCliente.First(c => c.strNombreCliente == "clienteunico");
        var usuario = db.SegUsuario.First(u => u.strNombre == "admin");

        var venta = new VenVenta
        {
            idCliCliente = cliente.id,
            idSegUsuario = usuario.id,
            idVenCatEstado = 1,
            dteFechaHoraCompra = DateTime.UtcNow,
            strClaveVenta = $"RC{Guid.NewGuid():N}"[..20],
        };
        db.VenVenta.Add(venta);
        db.SaveChanges();

        var token = JwtTestConfig.AdminToken;

        var tasks = Enumerable.Range(0, 5).Select(_ =>
        {
            var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/ventadetalle")
            {
                Content = JsonContent.Create(new
                {
                    idVenVenta = venta.id,
                    idProProducto = producto.id,
                    intPiezaVenta = 1,
                })
            };
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            return client.SendAsync(request);
        }).ToArray();

        await Task.WhenAll(tasks);

        var statuses = tasks.Select(t => t.Result.StatusCode).ToList();
        var successCount = statuses.Count(s => s == HttpStatusCode.Created);
        var failCount = statuses.Count(s => s == HttpStatusCode.BadRequest);

        successCount.Should().Be(1);
        failCount.Should().Be(4);

        var productoFinal = db.ProProducto.AsNoTracking().First(p => p.id == producto.id);
        productoFinal.intNumeroExistencia.Should().BeLessThanOrEqualTo(0);
    }
}
