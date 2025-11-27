using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HnHMapperServer.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddFoodPerformanceIndexes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateIndex(
                name: "IX_Foods_TenantId_CreatedAt",
                table: "Foods",
                columns: new[] { "TenantId", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_FoodFeps_AttributeName_FoodId_BaseValue",
                table: "FoodFeps",
                columns: new[] { "AttributeName", "FoodId", "BaseValue" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Foods_TenantId_CreatedAt",
                table: "Foods");

            migrationBuilder.DropIndex(
                name: "IX_FoodFeps_AttributeName_FoodId_BaseValue",
                table: "FoodFeps");
        }
    }
}
