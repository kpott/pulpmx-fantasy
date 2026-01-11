using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PulpMXFantasy.ReadModel.Migrations
{
    /// <inheritdoc />
    public partial class InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "read_model");

            migrationBuilder.CreateTable(
                name: "command_status",
                schema: "read_model",
                columns: table => new
                {
                    command_id = table.Column<Guid>(type: "uuid", nullable: false),
                    correlation_id = table.Column<Guid>(type: "uuid", nullable: false),
                    command_type = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    status = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    progress_message = table.Column<string>(type: "text", nullable: true),
                    progress_percentage = table.Column<int>(type: "integer", nullable: true),
                    started_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    completed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    error_message = table.Column<string>(type: "text", nullable: true),
                    result_data = table.Column<string>(type: "jsonb", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_command_status", x => x.command_id);
                });

            migrationBuilder.CreateTable(
                name: "event_predictions",
                schema: "read_model",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    event_id = table.Column<Guid>(type: "uuid", nullable: false),
                    rider_id = table.Column<Guid>(type: "uuid", nullable: false),
                    rider_name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    rider_number = table.Column<int>(type: "integer", nullable: false),
                    bike_class = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    is_all_star = table.Column<bool>(type: "boolean", nullable: false),
                    handicap = table.Column<int>(type: "integer", nullable: false),
                    expected_points = table.Column<float>(type: "real", nullable: false),
                    PointsIfQualifies = table.Column<float>(type: "real", nullable: false),
                    predicted_finish = table.Column<int>(type: "integer", nullable: true),
                    lower_bound = table.Column<float>(type: "real", nullable: false),
                    upper_bound = table.Column<float>(type: "real", nullable: false),
                    confidence = table.Column<float>(type: "real", nullable: false),
                    model_version = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    generated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_event_predictions", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "events",
                schema: "read_model",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    slug = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    venue = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    location = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    event_date = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    series_name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    season_year = table.Column<int>(type: "integer", nullable: false),
                    is_completed = table.Column<bool>(type: "boolean", nullable: false),
                    LockoutTime = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    rider_count = table.Column<int>(type: "integer", nullable: false),
                    synced_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_events", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "model_metadata",
                schema: "read_model",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    bike_class = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    model_type = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    version = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    trained_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    training_samples = table.Column<int>(type: "integer", nullable: false),
                    validation_accuracy = table.Column<float>(type: "real", nullable: true),
                    r_squared = table.Column<float>(type: "real", nullable: true),
                    mean_absolute_error = table.Column<float>(type: "real", nullable: true),
                    model_path = table.Column<string>(type: "text", nullable: false),
                    is_active = table.Column<bool>(type: "boolean", nullable: false, defaultValue: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_model_metadata", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "idx_command_status_correlation",
                schema: "read_model",
                table: "command_status",
                column: "correlation_id");

            migrationBuilder.CreateIndex(
                name: "idx_command_status_started",
                schema: "read_model",
                table: "command_status",
                column: "started_at");

            migrationBuilder.CreateIndex(
                name: "idx_command_status_type",
                schema: "read_model",
                table: "command_status",
                columns: new[] { "command_type", "status" });

            migrationBuilder.CreateIndex(
                name: "idx_event_predictions_class",
                schema: "read_model",
                table: "event_predictions",
                columns: new[] { "event_id", "bike_class" });

            migrationBuilder.CreateIndex(
                name: "idx_event_predictions_event",
                schema: "read_model",
                table: "event_predictions",
                column: "event_id");

            migrationBuilder.CreateIndex(
                name: "idx_event_predictions_points",
                schema: "read_model",
                table: "event_predictions",
                columns: new[] { "event_id", "expected_points" });

            migrationBuilder.CreateIndex(
                name: "uq_event_predictions_event_rider",
                schema: "read_model",
                table: "event_predictions",
                columns: new[] { "event_id", "rider_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "idx_events_date",
                schema: "read_model",
                table: "events",
                column: "event_date");

            migrationBuilder.CreateIndex(
                name: "idx_events_upcoming",
                schema: "read_model",
                table: "events",
                columns: new[] { "is_completed", "event_date" });

            migrationBuilder.CreateIndex(
                name: "uq_events_slug",
                schema: "read_model",
                table: "events",
                column: "slug",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "idx_model_metadata_active",
                schema: "read_model",
                table: "model_metadata",
                columns: new[] { "bike_class", "model_type", "is_active" });

            migrationBuilder.CreateIndex(
                name: "idx_model_metadata_trained",
                schema: "read_model",
                table: "model_metadata",
                column: "trained_at");

            migrationBuilder.CreateIndex(
                name: "uq_model_metadata_version",
                schema: "read_model",
                table: "model_metadata",
                columns: new[] { "bike_class", "model_type", "version" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "command_status",
                schema: "read_model");

            migrationBuilder.DropTable(
                name: "event_predictions",
                schema: "read_model");

            migrationBuilder.DropTable(
                name: "events",
                schema: "read_model");

            migrationBuilder.DropTable(
                name: "model_metadata",
                schema: "read_model");
        }
    }
}
