using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PulpMXFantasy.ReadModel.Migrations
{
    /// <inheritdoc />
    public partial class AddCommandProgressHistory : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<long>(
                name: "duration_ms",
                schema: "read_model",
                table: "command_status",
                type: "bigint",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "command_progress_history",
                schema: "read_model",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    command_id = table.Column<Guid>(type: "uuid", nullable: false),
                    message = table.Column<string>(type: "text", nullable: false),
                    progress_percentage = table.Column<int>(type: "integer", nullable: false),
                    occurred_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    milestone_name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_command_progress_history", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "idx_progress_history_command",
                schema: "read_model",
                table: "command_progress_history",
                column: "command_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "command_progress_history",
                schema: "read_model");

            migrationBuilder.DropColumn(
                name: "duration_ms",
                schema: "read_model",
                table: "command_status");
        }
    }
}
