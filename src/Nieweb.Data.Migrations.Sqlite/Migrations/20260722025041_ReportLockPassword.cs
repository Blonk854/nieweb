using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Nieweb.Data.Migrations.Sqlite.Migrations
{
    /// <inheritdoc />
    public partial class ReportLockPassword : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "LockPasswordHash",
                table: "Reports",
                type: "TEXT",
                maxLength: 500,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "LockPasswordHash",
                table: "Reports");
        }
    }
}
