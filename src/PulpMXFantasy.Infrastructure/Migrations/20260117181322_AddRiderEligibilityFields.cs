using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PulpMXFantasy.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddRiderEligibilityFields : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "Ineligible",
                schema: "domain",
                table: "event_riders",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "IneligibleReason",
                schema: "domain",
                table: "event_riders",
                type: "text",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Ineligible",
                schema: "domain",
                table: "event_riders");

            migrationBuilder.DropColumn(
                name: "IneligibleReason",
                schema: "domain",
                table: "event_riders");
        }
    }
}
