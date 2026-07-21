using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Nieweb.Data.Migrations.Sqlite.Migrations
{
    /// <inheritdoc />
    public partial class MustRotatePassword : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "MustRotatePassword",
                table: "Users",
                type: "INTEGER",
                nullable: false,
                defaultValue: false);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "MustRotatePassword",
                table: "Users");
        }
    }
}
