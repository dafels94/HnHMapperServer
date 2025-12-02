using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HnHMapperServer.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddOverlayData : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "OverlayData",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    MapId = table.Column<int>(type: "INTEGER", nullable: false),
                    CoordX = table.Column<int>(type: "INTEGER", nullable: false),
                    CoordY = table.Column<int>(type: "INTEGER", nullable: false),
                    OverlayType = table.Column<string>(type: "TEXT", maxLength: 50, nullable: false),
                    Data = table.Column<byte[]>(type: "BLOB", nullable: false),
                    TenantId = table.Column<string>(type: "TEXT", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_OverlayData", x => x.Id);
                    table.ForeignKey(
                        name: "FK_OverlayData_Maps_MapId",
                        column: x => x.MapId,
                        principalTable: "Maps",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_OverlayData_Tenants_TenantId",
                        column: x => x.TenantId,
                        principalTable: "Tenants",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_OverlayData_MapId",
                table: "OverlayData",
                column: "MapId");

            migrationBuilder.CreateIndex(
                name: "IX_OverlayData_MapId_CoordX_CoordY_OverlayType_TenantId",
                table: "OverlayData",
                columns: new[] { "MapId", "CoordX", "CoordY", "OverlayType", "TenantId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_OverlayData_TenantId",
                table: "OverlayData",
                column: "TenantId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "OverlayData");
        }
    }
}
