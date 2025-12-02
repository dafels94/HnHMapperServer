using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HnHMapperServer.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddOverlayCoveringIndex : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateIndex(
                name: "IX_OverlayData_TenantId_MapId_CoordX_CoordY",
                table: "OverlayData",
                columns: new[] { "TenantId", "MapId", "CoordX", "CoordY" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_OverlayData_TenantId_MapId_CoordX_CoordY",
                table: "OverlayData");
        }
    }
}
