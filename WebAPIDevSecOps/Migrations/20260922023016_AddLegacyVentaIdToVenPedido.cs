using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace WebAPIDevSecOps.Migrations
{
    /// <inheritdoc />
    public partial class AddLegacyVentaIdToVenPedido : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "LegacyVentaId",
                table: "VenPedido",
                type: "int",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_VenPedido_LegacyVentaId",
                table: "VenPedido",
                column: "LegacyVentaId",
                unique: true,
                filter: "[LegacyVentaId] IS NOT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_VenPedido_LegacyVentaId",
                table: "VenPedido");

            migrationBuilder.DropColumn(
                name: "LegacyVentaId",
                table: "VenPedido");
        }
    }
}
