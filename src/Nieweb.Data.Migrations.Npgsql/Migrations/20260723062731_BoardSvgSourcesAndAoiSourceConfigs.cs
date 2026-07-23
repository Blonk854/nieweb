using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace Nieweb.Data.Migrations.Npgsql.Migrations
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
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    Key = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    DisplayName = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Kind = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    Server = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    Database = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    User = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    EncryptedPassword = table.Column<byte[]>(type: "bytea", nullable: true),
                    ConnectTimeoutSeconds = table.Column<int>(type: "integer", nullable: false),
                    QueryTimeoutSeconds = table.Column<int>(type: "integer", nullable: false),
                    TrustServerCertificate = table.Column<bool>(type: "boolean", nullable: false),
                    Encrypt = table.Column<bool>(type: "boolean", nullable: false),
                    IsEnabled = table.Column<bool>(type: "boolean", nullable: false),
                    LastTestedUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    LastTestSucceeded = table.Column<bool>(type: "boolean", nullable: true),
                    LastTestError = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    CreatedUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    LastModifiedUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AoiSourceConfigs", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "BoardSvgSources",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    MachineName = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    UncPath = table.Column<string>(type: "character varying(1024)", maxLength: 1024, nullable: false),
                    IsEnabled = table.Column<bool>(type: "boolean", nullable: false),
                    LastSyncedUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    LastSyncErrorUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    LastSyncError = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    CreatedUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    LastModifiedUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
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
