using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AccessControll.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddDoorI2cRelayFields : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "DurationMs",
                table: "Doors",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<byte>(
                name: "I2cAddress",
                table: "Doors",
                type: "INTEGER",
                nullable: false,
                defaultValue: (byte)0);

            migrationBuilder.AddColumn<byte>(
                name: "I2cPin",
                table: "Doors",
                type: "INTEGER",
                nullable: false,
                defaultValue: (byte)0);

            migrationBuilder.AddColumn<string>(
                name: "StationMacAddress",
                table: "Doors",
                type: "TEXT",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "DurationMs",
                table: "Doors");

            migrationBuilder.DropColumn(
                name: "I2cAddress",
                table: "Doors");

            migrationBuilder.DropColumn(
                name: "I2cPin",
                table: "Doors");

            migrationBuilder.DropColumn(
                name: "StationMacAddress",
                table: "Doors");
        }
    }
}
