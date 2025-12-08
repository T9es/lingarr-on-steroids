using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Lingarr.Migrations.MySQL.Migrations
{
    /// <inheritdoc />
    public partial class AddPriorityColumns : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "is_priority",
                table: "shows",
                type: "tinyint(1)",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<DateTime>(
                name: "priority_date",
                table: "shows",
                type: "datetime(6)",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "is_priority",
                table: "movies",
                type: "tinyint(1)",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<DateTime>(
                name: "priority_date",
                table: "movies",
                type: "datetime(6)",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "date_added",
                table: "episodes",
                type: "datetime(6)",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "is_priority",
                table: "shows");

            migrationBuilder.DropColumn(
                name: "priority_date",
                table: "shows");

            migrationBuilder.DropColumn(
                name: "is_priority",
                table: "movies");

            migrationBuilder.DropColumn(
                name: "priority_date",
                table: "movies");

            migrationBuilder.DropColumn(
                name: "date_added",
                table: "episodes");
        }
    }
}
