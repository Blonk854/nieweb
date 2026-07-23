using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace Nieweb.Data.Migrations.Npgsql.Migrations
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
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    Name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    DisplayOrder = table.Column<int>(type: "integer", nullable: false),
                    CreatedUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    LastModifiedUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ReportGroups", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Reports",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    Title = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Description = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    ReportGroupId = table.Column<int>(type: "integer", nullable: true),
                    OwnerUserId = table.Column<int>(type: "integer", nullable: true),
                    OwnerDisplayName = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    IsLocked = table.Column<bool>(type: "boolean", nullable: false),
                    IsPinnedHome = table.Column<bool>(type: "boolean", nullable: false),
                    RefreshFrequencySeconds = table.Column<int>(type: "integer", nullable: true),
                    ChromeJson = table.Column<string>(type: "text", nullable: true),
                    DisplayOrder = table.Column<int>(type: "integer", nullable: false),
                    CreatedUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    LastModifiedUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
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
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    ReportId = table.Column<int>(type: "integer", nullable: false),
                    TileType = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    Title = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    DisplayOrder = table.Column<int>(type: "integer", nullable: false),
                    ConfigJson = table.Column<string>(type: "text", nullable: false),
                    CreatedUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    LastModifiedUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
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
