using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Nieweb.Data.Migrations.Sqlite.Migrations
{
    /// <inheritdoc />
    public partial class ReportComposition : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ReportGroups",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    Name = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    DisplayOrder = table.Column<int>(type: "INTEGER", nullable: false),
                    CreatedUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    LastModifiedUtc = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ReportGroups", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Reports",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    Title = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    Description = table.Column<string>(type: "TEXT", maxLength: 1000, nullable: true),
                    ReportGroupId = table.Column<int>(type: "INTEGER", nullable: true),
                    OwnerUserId = table.Column<int>(type: "INTEGER", nullable: true),
                    OwnerDisplayName = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    IsLocked = table.Column<bool>(type: "INTEGER", nullable: false),
                    IsPinnedHome = table.Column<bool>(type: "INTEGER", nullable: false),
                    RefreshFrequencySeconds = table.Column<int>(type: "INTEGER", nullable: true),
                    ChromeJson = table.Column<string>(type: "TEXT", nullable: true),
                    DisplayOrder = table.Column<int>(type: "INTEGER", nullable: false),
                    CreatedUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    LastModifiedUtc = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Reports", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Reports_ReportGroups_ReportGroupId",
                        column: x => x.ReportGroupId,
                        principalTable: "ReportGroups",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "ReportEntities",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    ReportId = table.Column<int>(type: "INTEGER", nullable: false),
                    TileType = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                    Title = table.Column<string>(type: "TEXT", maxLength: 200, nullable: true),
                    DisplayOrder = table.Column<int>(type: "INTEGER", nullable: false),
                    ConfigJson = table.Column<string>(type: "TEXT", nullable: false),
                    CreatedUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    LastModifiedUtc = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ReportEntities", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ReportEntities_Reports_ReportId",
                        column: x => x.ReportId,
                        principalTable: "Reports",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ReportEntities_ReportId_DisplayOrder",
                table: "ReportEntities",
                columns: new[] { "ReportId", "DisplayOrder" });

            migrationBuilder.CreateIndex(
                name: "IX_ReportGroups_DisplayOrder",
                table: "ReportGroups",
                column: "DisplayOrder");

            migrationBuilder.CreateIndex(
                name: "IX_ReportGroups_Name",
                table: "ReportGroups",
                column: "Name",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Reports_DisplayOrder",
                table: "Reports",
                column: "DisplayOrder");

            migrationBuilder.CreateIndex(
                name: "IX_Reports_IsPinnedHome",
                table: "Reports",
                column: "IsPinnedHome");

            migrationBuilder.CreateIndex(
                name: "IX_Reports_OwnerUserId",
                table: "Reports",
                column: "OwnerUserId");

            migrationBuilder.CreateIndex(
                name: "IX_Reports_ReportGroupId",
                table: "Reports",
                column: "ReportGroupId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ReportEntities");

            migrationBuilder.DropTable(
                name: "Reports");

            migrationBuilder.DropTable(
                name: "ReportGroups");
        }
    }
}
