using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace WebAPIDevSecOps.Migrations
{
    /// <inheritdoc />
    public partial class SagaVentas : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "VenPedido",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    idCliCliente = table.Column<int>(type: "int", nullable: false),
                    dteFechaPedido = table.Column<DateTime>(type: "datetime2", nullable: false),
                    decTotal = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    strEstadoSaga = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    strMotivoRechazo = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_VenPedido", x => x.id);
                    table.ForeignKey(
                        name: "FK_VenPedido_CliCliente_idCliCliente",
                        column: x => x.idCliCliente,
                        principalTable: "CliCliente",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "VenPedidoDetalle",
                columns: table => new
                {
                    id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    idVenPedido = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    idProProducto = table.Column<int>(type: "int", nullable: false),
                    intCantidad = table.Column<int>(type: "int", nullable: false),
                    decPrecioUnitario = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_VenPedidoDetalle", x => x.id);
                    table.ForeignKey(
                        name: "FK_VenPedidoDetalle_ProProducto_idProProducto",
                        column: x => x.idProProducto,
                        principalTable: "ProProducto",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_VenPedidoDetalle_VenPedido_idVenPedido",
                        column: x => x.idVenPedido,
                        principalTable: "VenPedido",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "VenPedidoFactura",
                columns: table => new
                {
                    id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    idVenPedido = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    strFolioFactura = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    strRFC = table.Column<string>(type: "nvarchar(13)", maxLength: 13, nullable: true),
                    decTotal = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    dteFechaEmision = table.Column<DateTime>(type: "datetime2", nullable: false),
                    strEstado = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_VenPedidoFactura", x => x.id);
                    table.ForeignKey(
                        name: "FK_VenPedidoFactura_VenPedido_idVenPedido",
                        column: x => x.idVenPedido,
                        principalTable: "VenPedido",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "VenPedidoPago",
                columns: table => new
                {
                    id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    idVenPedido = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    decMonto = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    strMetodoPago = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    strIdTransaccion = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    strEstado = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    dteFechaPago = table.Column<DateTime>(type: "datetime2", nullable: false),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_VenPedidoPago", x => x.id);
                    table.ForeignKey(
                        name: "FK_VenPedidoPago_VenPedido_idVenPedido",
                        column: x => x.idVenPedido,
                        principalTable: "VenPedido",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_VenPedido_idCliCliente",
                table: "VenPedido",
                column: "idCliCliente");

            migrationBuilder.CreateIndex(
                name: "IX_VenPedido_strEstadoSaga",
                table: "VenPedido",
                column: "strEstadoSaga");

            migrationBuilder.CreateIndex(
                name: "IX_VenPedidoDetalle_idProProducto",
                table: "VenPedidoDetalle",
                column: "idProProducto");

            migrationBuilder.CreateIndex(
                name: "IX_VenPedidoDetalle_idVenPedido",
                table: "VenPedidoDetalle",
                column: "idVenPedido");

            migrationBuilder.CreateIndex(
                name: "IX_VenPedidoFactura_idVenPedido",
                table: "VenPedidoFactura",
                column: "idVenPedido");

            migrationBuilder.CreateIndex(
                name: "IX_VenPedidoFactura_strFolioFactura",
                table: "VenPedidoFactura",
                column: "strFolioFactura",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_VenPedidoPago_idVenPedido",
                table: "VenPedidoPago",
                column: "idVenPedido");

            migrationBuilder.CreateIndex(
                name: "IX_VenPedidoPago_strIdTransaccion",
                table: "VenPedidoPago",
                column: "strIdTransaccion",
                unique: true,
                filter: "[strIdTransaccion] IS NOT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "VenPedidoDetalle");

            migrationBuilder.DropTable(
                name: "VenPedidoFactura");

            migrationBuilder.DropTable(
                name: "VenPedidoPago");

            migrationBuilder.DropTable(
                name: "VenPedido");
        }
    }
}
