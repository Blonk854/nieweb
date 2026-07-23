using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Nieweb.Data.Migrations.Sqlite.Migrations
{
    /// <inheritdoc />
    public partial class ProductionLines : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ProductionLines",
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
                    table.PrimaryKey("PK_ProductionLines", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ShiftBreakpoints",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    Hour = table.Column<int>(type: "INTEGER", nullable: false),
                    Minute = table.Column<int>(type: "INTEGER", nullable: false),
                    Label = table.Column<string>(type: "TEXT", maxLength: 100, nullable: true),
                    DisplayOrder = table.Column<int>(type: "INTEGER", nullable: false),
                    CreatedUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    LastModifiedUtc = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ShiftBreakpoints", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ProductionLineMachines",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    ProductionLineId = table.Column<int>(type: "INTEGER", nullable: false),
                    SourceId = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    MachineId = table.Column<int>(type: "INTEGER", nullable: false),
                    MachineName = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    Category = table.Column<string>(type: "TEXT", maxLength: 100, nullable: true),
                    DisplayOrder = table.Column<int>(type: "INTEGER", nullable: false),
                    CreatedUtc = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ProductionLineMachines", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ProductionLineMachines_ProductionLines_ProductionLineId",
                        column: x => x.ProductionLineId,
                        principalTable: "ProductionLines",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ProductionLineMachines_ProductionLineId",
                table: "ProductionLineMachines",
                column: "ProductionLineId");

            migrationBuilder.CreateIndex(
                name: "IX_ProductionLineMachines_SourceId_MachineId",
                table: "ProductionLineMachines",
                columns: new[] { "SourceId", "MachineId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ProductionLines_DisplayOrder",
                table: "ProductionLines",
                column: "DisplayOrder");

            migrationBuilder.CreateIndex(
                name: "IX_ProductionLines_Name",
                table: "ProductionLines",
                column: "Name",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ShiftBreakpoints_Hour_Minute",
                table: "ShiftBreakpoints",
                columns: new[] { "Hour", "Minute" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ProductionLineMachines");

            migrationBuilder.DropTable(
                name: "ShiftBreakpoints");

            migrationBuilder.DropTable(
                name: "ProductionLines");
        }
    }
}
