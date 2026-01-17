using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PulpMXFantasy.ReadModel.Migrations
{
    /// <inheritdoc />
    public partial class AddPredictionEligibilityFields : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "Ineligible",
                schema: "read_model",
                table: "event_predictions",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "IneligibleReason",
                schema: "read_model",
                table: "event_predictions",
                type: "text",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Ineligible",
                schema: "read_model",
                table: "event_predictions");

            migrationBuilder.DropColumn(
                name: "IneligibleReason",
                schema: "read_model",
                table: "event_predictions");
        }
    }
}
