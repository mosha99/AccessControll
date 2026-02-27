using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace AccessControll.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddRolePanelPermissions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "RolePanelPermissions",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    RoleName = table.Column<string>(type: "TEXT", maxLength: 256, nullable: false),
                    Panel = table.Column<string>(type: "TEXT", maxLength: 50, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RolePanelPermissions", x => x.Id);
                });

            migrationBuilder.InsertData(
                table: "RolePanelPermissions",
                columns: new[] { "Id", "Panel", "RoleName" },
                values: new object[,]
                {
                    { 1, "doors", "DoorManager" },
                    { 2, "logs", "DoorManager" }
                });

            migrationBuilder.CreateIndex(
                name: "IX_RolePanelPermissions_RoleName_Panel",
                table: "RolePanelPermissions",
                columns: new[] { "RoleName", "Panel" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "RolePanelPermissions");
        }
    }
}
