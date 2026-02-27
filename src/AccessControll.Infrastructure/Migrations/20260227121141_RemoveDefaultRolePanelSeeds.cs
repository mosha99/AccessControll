using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace AccessControll.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class RemoveDefaultRolePanelSeeds : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DeleteData(
                table: "RolePanelPermissions",
                keyColumn: "Id",
                keyValue: 1);

            migrationBuilder.DeleteData(
                table: "RolePanelPermissions",
                keyColumn: "Id",
                keyValue: 2);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.InsertData(
                table: "RolePanelPermissions",
                columns: new[] { "Id", "Panel", "RoleName" },
                values: new object[,]
                {
                    { 1, "doors", "DoorManager" },
                    { 2, "logs", "DoorManager" }
                });
        }
    }
}
