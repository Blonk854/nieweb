using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Nieweb.Data.Migrations.Sqlite.Migrations
{
    /// <inheritdoc />
    public partial class BoardSvgSourcesAndAoiSourceConfigs : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "AoiSourceConfigs",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    Key = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    DisplayName = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    Kind = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                    Server = table.Column<string>(type: "TEXT", maxLength: 200, nullable: true),
                    Database = table.Column<string>(type: "TEXT", maxLength: 200, nullable: true),
                    User = table.Column<string>(type: "TEXT", maxLength: 200, nullable: true),
                    EncryptedPassword = table.Column<byte[]>(type: "BLOB", nullable: true),
                    ConnectTimeoutSeconds = table.Column<int>(type: "INTEGER", nullable: false),
                    QueryTimeoutSeconds = table.Column<int>(type: "INTEGER", nullable: false),
                    TrustServerCertificate = table.Column<bool>(type: "INTEGER", nullable: false),
                    Encrypt = table.Column<bool>(type: "INTEGER", nullable: false),
                    IsEnabled = table.Column<bool>(type: "INTEGER", nullable: false),
                    LastTestedUtc = table.Column<DateTime>(type: "TEXT", nullable: true),
                    LastTestSucceeded = table.Column<bool>(type: "INTEGER", nullable: true),
                    LastTestError = table.Column<string>(type: "TEXT", maxLength: 500, nullable: true),
                    CreatedUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    LastModifiedUtc = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AoiSourceConfigs", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "BoardSvgSources",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    MachineName = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    UncPath = table.Column<string>(type: "TEXT", maxLength: 1024, nullable: false),
                    IsEnabled = table.Column<bool>(type: "INTEGER", nullable: false),
                    LastSyncedUtc = table.Column<DateTime>(type: "TEXT", nullable: true),
                    LastSyncErrorUtc = table.Column<DateTime>(type: "TEXT", nullable: true),
                    LastSyncError = table.Column<string>(type: "TEXT", maxLength: 500, nullable: true),
                    CreatedUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    LastModifiedUtc = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BoardSvgSources", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_AoiSourceConfigs_IsEnabled",
                table: "AoiSourceConfigs",
                column: "IsEnabled");

            migrationBuilder.CreateIndex(
                name: "IX_AoiSourceConfigs_Key",
                table: "AoiSourceConfigs",
                column: "Key",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_BoardSvgSources_MachineName",
                table: "BoardSvgSources",
                column: "MachineName",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AoiSourceConfigs");

            migrationBuilder.DropTable(
                name: "BoardSvgSources");
        }
    }
}
