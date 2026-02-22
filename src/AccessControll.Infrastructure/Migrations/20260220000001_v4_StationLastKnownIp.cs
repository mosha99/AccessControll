using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AccessControll.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class v4_StationLastKnownIp : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "LastKnownIp",
                table: "Stations",
                type: "TEXT",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "LastKnownIp",
                table: "Stations");
        }
    }
}
