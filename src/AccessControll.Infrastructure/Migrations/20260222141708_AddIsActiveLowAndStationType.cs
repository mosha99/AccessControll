using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AccessControll.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddIsActiveLowAndStationType : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "Type",
                table: "Stations",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<bool>(
                name: "IsActiveLow",
                table: "Doors",
                type: "INTEGER",
                nullable: false,
                defaultValue: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Type",
                table: "Stations");

            migrationBuilder.DropColumn(
                name: "IsActiveLow",
                table: "Doors");
        }
    }
}
