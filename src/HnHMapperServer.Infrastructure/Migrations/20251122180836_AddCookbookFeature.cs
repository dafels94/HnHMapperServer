using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HnHMapperServer.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddCookbookFeature : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Foods",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    TenantId = table.Column<string>(type: "TEXT", nullable: true),
                    Name = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    ResourceType = table.Column<string>(type: "TEXT", maxLength: 256, nullable: true),
                    Energy = table.Column<int>(type: "INTEGER", nullable: false),
                    Hunger = table.Column<int>(type: "INTEGER", nullable: false),
                    Description = table.Column<string>(type: "TEXT", maxLength: 2000, nullable: true),
                    IsSmoked = table.Column<bool>(type: "INTEGER", nullable: false, defaultValue: false),
                    SubmittedBy = table.Column<string>(type: "TEXT", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    ApprovedBy = table.Column<string>(type: "TEXT", nullable: true),
                    ApprovedAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                    IsVerified = table.Column<bool>(type: "INTEGER", nullable: false, defaultValue: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Foods", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Foods_AspNetUsers_SubmittedBy",
                        column: x => x.SubmittedBy,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Foods_Tenants_TenantId",
                        column: x => x.TenantId,
                        principalTable: "Tenants",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "FoodSubmissions",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    TenantId = table.Column<string>(type: "TEXT", nullable: true),
                    SubmittedBy = table.Column<string>(type: "TEXT", nullable: false),
                    SubmittedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    Status = table.Column<string>(type: "TEXT", maxLength: 50, nullable: false, defaultValue: "Pending"),
                    DataJson = table.Column<string>(type: "TEXT", nullable: false),
                    ReviewedBy = table.Column<string>(type: "TEXT", nullable: true),
                    ReviewedAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                    ReviewNotes = table.Column<string>(type: "TEXT", maxLength: 1000, nullable: true),
                    ApprovedFoodId = table.Column<int>(type: "INTEGER", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_FoodSubmissions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_FoodSubmissions_AspNetUsers_SubmittedBy",
                        column: x => x.SubmittedBy,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_FoodSubmissions_Tenants_TenantId",
                        column: x => x.TenantId,
                        principalTable: "Tenants",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "VerifiedContributors",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    TenantId = table.Column<string>(type: "TEXT", nullable: false),
                    UserId = table.Column<string>(type: "TEXT", nullable: false),
                    VerifiedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    VerifiedBy = table.Column<string>(type: "TEXT", nullable: false),
                    Notes = table.Column<string>(type: "TEXT", maxLength: 500, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_VerifiedContributors", x => x.Id);
                    table.ForeignKey(
                        name: "FK_VerifiedContributors_AspNetUsers_UserId",
                        column: x => x.UserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_VerifiedContributors_AspNetUsers_VerifiedBy",
                        column: x => x.VerifiedBy,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_VerifiedContributors_Tenants_TenantId",
                        column: x => x.TenantId,
                        principalTable: "Tenants",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "FoodFeps",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    FoodId = table.Column<int>(type: "INTEGER", nullable: false),
                    AttributeName = table.Column<string>(type: "TEXT", maxLength: 50, nullable: false),
                    BaseValue = table.Column<decimal>(type: "TEXT", precision: 18, scale: 4, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_FoodFeps", x => x.Id);
                    table.ForeignKey(
                        name: "FK_FoodFeps_Foods_FoodId",
                        column: x => x.FoodId,
                        principalTable: "Foods",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "FoodIngredients",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    FoodId = table.Column<int>(type: "INTEGER", nullable: false),
                    Name = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    Quantity = table.Column<decimal>(type: "TEXT", precision: 18, scale: 4, nullable: false),
                    Quality = table.Column<int>(type: "INTEGER", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_FoodIngredients", x => x.Id);
                    table.ForeignKey(
                        name: "FK_FoodIngredients_Foods_FoodId",
                        column: x => x.FoodId,
                        principalTable: "Foods",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_FoodFeps_AttributeName",
                table: "FoodFeps",
                column: "AttributeName");

            migrationBuilder.CreateIndex(
                name: "IX_FoodFeps_FoodId",
                table: "FoodFeps",
                column: "FoodId");

            migrationBuilder.CreateIndex(
                name: "IX_FoodIngredients_FoodId",
                table: "FoodIngredients",
                column: "FoodId");

            migrationBuilder.CreateIndex(
                name: "IX_FoodIngredients_Name",
                table: "FoodIngredients",
                column: "Name");

            migrationBuilder.CreateIndex(
                name: "IX_Foods_CreatedAt",
                table: "Foods",
                column: "CreatedAt");

            migrationBuilder.CreateIndex(
                name: "IX_Foods_IsVerified",
                table: "Foods",
                column: "IsVerified");

            migrationBuilder.CreateIndex(
                name: "IX_Foods_Name",
                table: "Foods",
                column: "Name");

            migrationBuilder.CreateIndex(
                name: "IX_Foods_ResourceType",
                table: "Foods",
                column: "ResourceType");

            migrationBuilder.CreateIndex(
                name: "IX_Foods_SubmittedBy",
                table: "Foods",
                column: "SubmittedBy");

            migrationBuilder.CreateIndex(
                name: "IX_Foods_TenantId",
                table: "Foods",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_Foods_TenantId_Name",
                table: "Foods",
                columns: new[] { "TenantId", "Name" });

            migrationBuilder.CreateIndex(
                name: "IX_FoodSubmissions_Status",
                table: "FoodSubmissions",
                column: "Status");

            migrationBuilder.CreateIndex(
                name: "IX_FoodSubmissions_SubmittedAt",
                table: "FoodSubmissions",
                column: "SubmittedAt");

            migrationBuilder.CreateIndex(
                name: "IX_FoodSubmissions_SubmittedBy",
                table: "FoodSubmissions",
                column: "SubmittedBy");

            migrationBuilder.CreateIndex(
                name: "IX_FoodSubmissions_TenantId",
                table: "FoodSubmissions",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_FoodSubmissions_TenantId_Status",
                table: "FoodSubmissions",
                columns: new[] { "TenantId", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_VerifiedContributors_TenantId",
                table: "VerifiedContributors",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_VerifiedContributors_TenantId_UserId",
                table: "VerifiedContributors",
                columns: new[] { "TenantId", "UserId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_VerifiedContributors_UserId",
                table: "VerifiedContributors",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_VerifiedContributors_VerifiedBy",
                table: "VerifiedContributors",
                column: "VerifiedBy");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "FoodFeps");

            migrationBuilder.DropTable(
                name: "FoodIngredients");

            migrationBuilder.DropTable(
                name: "FoodSubmissions");

            migrationBuilder.DropTable(
                name: "VerifiedContributors");

            migrationBuilder.DropTable(
                name: "Foods");
        }
    }
}
