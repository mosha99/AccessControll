using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AccessControll.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class I2cFieldsAsStringAndMomentary : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<string>(
                name: "I2cPin",
                table: "Doors",
                type: "TEXT",
                nullable: false,
                oldClrType: typeof(byte),
                oldType: "INTEGER");

            migrationBuilder.AlterColumn<string>(
                name: "I2cAddress",
                table: "Doors",
                type: "TEXT",
                nullable: false,
                oldClrType: typeof(byte),
                oldType: "INTEGER");

            migrationBuilder.AddColumn<bool>(
                name: "IsMomentary",
                table: "Doors",
                type: "INTEGER",
                nullable: false,
                defaultValue: false);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "IsMomentary",
                table: "Doors");

            migrationBuilder.AlterColumn<byte>(
                name: "I2cPin",
                table: "Doors",
                type: "INTEGER",
                nullable: false,
                oldClrType: typeof(string),
                oldType: "TEXT");

            migrationBuilder.AlterColumn<byte>(
                name: "I2cAddress",
                table: "Doors",
                type: "INTEGER",
                nullable: false,
                oldClrType: typeof(string),
                oldType: "TEXT");
        }
    }
}
