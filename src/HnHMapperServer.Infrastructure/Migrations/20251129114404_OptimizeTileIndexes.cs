using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HnHMapperServer.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class OptimizeTileIndexes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateIndex(
                name: "IX_Tiles_TenantId_MapId_Zoom",
                table: "Tiles",
                columns: new[] { "TenantId", "MapId", "Zoom" });

            migrationBuilder.CreateIndex(
                name: "IX_Tiles_TenantId_Zoom",
                table: "Tiles",
                columns: new[] { "TenantId", "Zoom" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Tiles_TenantId_MapId_Zoom",
                table: "Tiles");

            migrationBuilder.DropIndex(
                name: "IX_Tiles_TenantId_Zoom",
                table: "Tiles");
        }
    }
}
