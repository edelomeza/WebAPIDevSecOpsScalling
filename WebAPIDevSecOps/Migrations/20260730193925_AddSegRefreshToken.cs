using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace WebAPIDevSecOps.Migrations
{
    /// <inheritdoc />
    public partial class AddSegRefreshToken : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "strCreadoPorUsuario",
                table: "ProProducto",
                type: "nvarchar(50)",
                maxLength: 50,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "strCreadoPorUsuario",
                table: "CliCliente",
                type: "nvarchar(50)",
                maxLength: 50,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "SegRefreshToken",
                columns: table => new
                {
                    id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    idSegUsuario = table.Column<int>(type: "int", nullable: false),
                    strTokenHash = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    dteExpiresAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    dteCreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    dteRevokedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    strReplacedByTokenHash = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: true),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SegRefreshToken", x => x.id);
                    table.ForeignKey(
                        name: "FK_SegRefreshToken_SegUsuario_idSegUsuario",
                        column: x => x.idSegUsuario,
                        principalTable: "SegUsuario",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_SegRefreshToken_idSegUsuario",
                table: "SegRefreshToken",
                column: "idSegUsuario");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "SegRefreshToken");

            migrationBuilder.DropColumn(
                name: "strCreadoPorUsuario",
                table: "ProProducto");

            migrationBuilder.DropColumn(
                name: "strCreadoPorUsuario",
                table: "CliCliente");
        }
    }
}
