using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace WebAPIDevSecOps.Migrations
{
    /// <inheritdoc />
    public partial class Add2FAFields : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "bln2FAHabilitado",
                table: "SegUsuario",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "str2FASecreto",
                table: "SegUsuario",
                type: "nvarchar(200)",
                maxLength: 200,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "bln2FAHabilitado",
                table: "SegUsuario");

            migrationBuilder.DropColumn(
                name: "str2FASecreto",
                table: "SegUsuario");
        }
    }
}
