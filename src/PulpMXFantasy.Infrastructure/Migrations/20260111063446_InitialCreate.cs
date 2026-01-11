using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PulpMXFantasy.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "domain");

            migrationBuilder.CreateTable(
                name: "riders",
                schema: "domain",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    pulp_mx_id = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    number = table.Column<int>(type: "integer", nullable: false),
                    photo_url = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_riders", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "series",
                schema: "domain",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    series_type = table.Column<string>(type: "text", nullable: false),
                    year = table.Column<int>(type: "integer", nullable: false),
                    start_date = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    end_date = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    is_active = table.Column<bool>(type: "boolean", nullable: false, defaultValue: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_series", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "events",
                schema: "domain",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    series_id = table.Column<Guid>(type: "uuid", nullable: false),
                    slug = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    venue = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    location = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    event_date = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    round_number = table.Column<int>(type: "integer", nullable: false),
                    series_type = table.Column<string>(type: "text", nullable: false),
                    event_format = table.Column<string>(type: "text", nullable: false),
                    division = table.Column<string>(type: "text", nullable: false),
                    LockoutTime = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    is_completed = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_events", x => x.id);
                    table.ForeignKey(
                        name: "FK_events_series_series_id",
                        column: x => x.series_id,
                        principalSchema: "domain",
                        principalTable: "series",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "event_riders",
                schema: "domain",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    event_id = table.Column<Guid>(type: "uuid", nullable: false),
                    rider_id = table.Column<Guid>(type: "uuid", nullable: false),
                    bike_class = table.Column<string>(type: "text", nullable: false),
                    handicap = table.Column<int>(type: "integer", nullable: false),
                    is_all_star = table.Column<bool>(type: "boolean", nullable: false),
                    is_injured = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false),
                    pick_trend = table.Column<decimal>(type: "numeric(5,2)", precision: 5, scale: 2, nullable: true),
                    combined_qualy_position = table.Column<int>(type: "integer", nullable: true),
                    best_qualy_lap_seconds = table.Column<decimal>(type: "numeric(10,3)", precision: 10, scale: 3, nullable: true),
                    qualy_gap_to_leader = table.Column<decimal>(type: "numeric(10,3)", precision: 10, scale: 3, nullable: true),
                    finish_position = table.Column<int>(type: "integer", nullable: true),
                    handicap_adjusted_position = table.Column<int>(type: "integer", nullable: true),
                    fantasy_points = table.Column<int>(type: "integer", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_event_riders", x => x.id);
                    table.ForeignKey(
                        name: "FK_event_riders_events_event_id",
                        column: x => x.event_id,
                        principalSchema: "domain",
                        principalTable: "events",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_event_riders_riders_rider_id",
                        column: x => x.rider_id,
                        principalSchema: "domain",
                        principalTable: "riders",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "teams",
                schema: "domain",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    event_id = table.Column<Guid>(type: "uuid", nullable: false),
                    user_id = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    team_name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    total_points = table.Column<int>(type: "integer", nullable: true),
                    is_optimized = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_teams", x => x.id);
                    table.ForeignKey(
                        name: "FK_teams_events_event_id",
                        column: x => x.event_id,
                        principalSchema: "domain",
                        principalTable: "events",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "team_selections",
                schema: "domain",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    team_id = table.Column<Guid>(type: "uuid", nullable: false),
                    event_rider_id = table.Column<Guid>(type: "uuid", nullable: false),
                    selection_order = table.Column<int>(type: "integer", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_team_selections", x => x.id);
                    table.ForeignKey(
                        name: "FK_team_selections_event_riders_event_rider_id",
                        column: x => x.event_rider_id,
                        principalSchema: "domain",
                        principalTable: "event_riders",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_team_selections_teams_team_id",
                        column: x => x.team_id,
                        principalSchema: "domain",
                        principalTable: "teams",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_event_riders_bike_class",
                schema: "domain",
                table: "event_riders",
                column: "bike_class");

            migrationBuilder.CreateIndex(
                name: "ix_event_riders_event_class",
                schema: "domain",
                table: "event_riders",
                columns: new[] { "event_id", "bike_class" });

            migrationBuilder.CreateIndex(
                name: "ix_event_riders_event_class_allstar",
                schema: "domain",
                table: "event_riders",
                columns: new[] { "event_id", "bike_class", "is_all_star" });

            migrationBuilder.CreateIndex(
                name: "ix_event_riders_event_finish",
                schema: "domain",
                table: "event_riders",
                columns: new[] { "event_id", "finish_position" });

            migrationBuilder.CreateIndex(
                name: "ix_event_riders_event_rider_unique",
                schema: "domain",
                table: "event_riders",
                columns: new[] { "event_id", "rider_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_event_riders_finish_position",
                schema: "domain",
                table: "event_riders",
                column: "finish_position");

            migrationBuilder.CreateIndex(
                name: "ix_event_riders_is_all_star",
                schema: "domain",
                table: "event_riders",
                column: "is_all_star");

            migrationBuilder.CreateIndex(
                name: "IX_event_riders_rider_id",
                schema: "domain",
                table: "event_riders",
                column: "rider_id");

            migrationBuilder.CreateIndex(
                name: "ix_events_date_completed",
                schema: "domain",
                table: "events",
                columns: new[] { "event_date", "is_completed" });

            migrationBuilder.CreateIndex(
                name: "ix_events_event_date",
                schema: "domain",
                table: "events",
                column: "event_date");

            migrationBuilder.CreateIndex(
                name: "ix_events_is_completed",
                schema: "domain",
                table: "events",
                column: "is_completed");

            migrationBuilder.CreateIndex(
                name: "ix_events_series_round",
                schema: "domain",
                table: "events",
                columns: new[] { "series_id", "round_number" });

            migrationBuilder.CreateIndex(
                name: "ix_events_slug",
                schema: "domain",
                table: "events",
                column: "slug",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_riders_name",
                schema: "domain",
                table: "riders",
                column: "name");

            migrationBuilder.CreateIndex(
                name: "ix_riders_pulp_mx_id",
                schema: "domain",
                table: "riders",
                column: "pulp_mx_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_series_is_active",
                schema: "domain",
                table: "series",
                column: "is_active");

            migrationBuilder.CreateIndex(
                name: "ix_series_year_type",
                schema: "domain",
                table: "series",
                columns: new[] { "year", "series_type" });

            migrationBuilder.CreateIndex(
                name: "ix_team_selections_event_rider_id",
                schema: "domain",
                table: "team_selections",
                column: "event_rider_id");

            migrationBuilder.CreateIndex(
                name: "ix_team_selections_team_id",
                schema: "domain",
                table: "team_selections",
                column: "team_id");

            migrationBuilder.CreateIndex(
                name: "ix_team_selections_team_rider_unique",
                schema: "domain",
                table: "team_selections",
                columns: new[] { "team_id", "event_rider_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_teams_event_id",
                schema: "domain",
                table: "teams",
                column: "event_id");

            migrationBuilder.CreateIndex(
                name: "ix_teams_event_points",
                schema: "domain",
                table: "teams",
                columns: new[] { "event_id", "total_points" });

            migrationBuilder.CreateIndex(
                name: "ix_teams_event_user",
                schema: "domain",
                table: "teams",
                columns: new[] { "event_id", "user_id" });

            migrationBuilder.CreateIndex(
                name: "ix_teams_is_optimized",
                schema: "domain",
                table: "teams",
                column: "is_optimized");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "team_selections",
                schema: "domain");

            migrationBuilder.DropTable(
                name: "event_riders",
                schema: "domain");

            migrationBuilder.DropTable(
                name: "teams",
                schema: "domain");

            migrationBuilder.DropTable(
                name: "riders",
                schema: "domain");

            migrationBuilder.DropTable(
                name: "events",
                schema: "domain");

            migrationBuilder.DropTable(
                name: "series",
                schema: "domain");
        }
    }
}
