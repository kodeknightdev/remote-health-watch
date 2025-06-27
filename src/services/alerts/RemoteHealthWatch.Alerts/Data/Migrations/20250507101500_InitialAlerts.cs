using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace RemoteHealthWatch.Alerts.Data.Migrations;

/// <inheritdoc />
public partial class InitialAlerts : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.EnsureSchema(
            name: "alerts");

        migrationBuilder.CreateTable(
            name: "Alerts",
            schema: "alerts",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                PatientId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                Type = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                Severity = table.Column<string>(type: "nvarchar(12)", maxLength: 12, nullable: false),
                Vital = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: true),
                Metric = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: true),
                Value = table.Column<double>(type: "float", nullable: true),
                Message = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: false),
                DedupKey = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                Status = table.Column<string>(type: "nvarchar(24)", maxLength: 24, nullable: false),
                EscalationLevel = table.Column<int>(type: "int", nullable: false),
                AssignedRole = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                NextEscalationAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                RaisedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                LastOccurredAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                OccurrenceCount = table.Column<int>(type: "int", nullable: false),
                AcknowledgedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                AcknowledgedBy = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                ResolvedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                ResolvedBy = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                ResolutionReason = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                Version = table.Column<Guid>(type: "uniqueidentifier", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_Alerts", x => x.Id);
            });

        migrationBuilder.CreateTable(
            name: "Outbox",
            schema: "alerts",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                Topic = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                Key = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                Type = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                Payload = table.Column<string>(type: "nvarchar(max)", nullable: false),
                TraceParent = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                OccurredAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                ProcessedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                Attempts = table.Column<int>(type: "int", nullable: false),
                LastError = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_Outbox", x => x.Id);
            });

        migrationBuilder.CreateTable(
            name: "ProcessedEvents",
            schema: "alerts",
            columns: table => new
            {
                EventId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                Consumer = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                ProcessedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_ProcessedEvents", x => new { x.EventId, x.Consumer });
            });

        migrationBuilder.CreateTable(
            name: "AlertNotes",
            schema: "alerts",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                AlertId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                AuthorId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                AuthorName = table.Column<string>(type: "nvarchar(120)", maxLength: 120, nullable: false),
                Text = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: false),
                At = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_AlertNotes", x => x.Id);
                table.ForeignKey(
                    name: "FK_AlertNotes_Alerts_AlertId",
                    column: x => x.AlertId,
                    principalSchema: "alerts",
                    principalTable: "Alerts",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateTable(
            name: "AlertTimeline",
            schema: "alerts",
            columns: table => new
            {
                Id = table.Column<long>(type: "bigint", nullable: false)
                    .Annotation("SqlServer:Identity", "1, 1"),
                AlertId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                At = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                Kind = table.Column<string>(type: "nvarchar(24)", maxLength: 24, nullable: false),
                ActorUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                Detail = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_AlertTimeline", x => x.Id);
                table.ForeignKey(
                    name: "FK_AlertTimeline_Alerts_AlertId",
                    column: x => x.AlertId,
                    principalSchema: "alerts",
                    principalTable: "Alerts",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateIndex(
            name: "IX_AlertNotes_AlertId",
            schema: "alerts",
            table: "AlertNotes",
            column: "AlertId");

        migrationBuilder.CreateIndex(
            name: "IX_Alerts_DedupKey",
            schema: "alerts",
            table: "Alerts",
            column: "DedupKey",
            unique: true,
            filter: "[Status] <> 'Resolved'");

        migrationBuilder.CreateIndex(
            name: "IX_Alerts_PatientId_Status",
            schema: "alerts",
            table: "Alerts",
            columns: new[] { "PatientId", "Status" });

        migrationBuilder.CreateIndex(
            name: "IX_Alerts_RaisedAt",
            schema: "alerts",
            table: "Alerts",
            column: "RaisedAt");

        migrationBuilder.CreateIndex(
            name: "IX_Alerts_Status_NextEscalationAt",
            schema: "alerts",
            table: "Alerts",
            columns: new[] { "Status", "NextEscalationAt" });

        migrationBuilder.CreateIndex(
            name: "IX_AlertTimeline_AlertId",
            schema: "alerts",
            table: "AlertTimeline",
            column: "AlertId");

        migrationBuilder.CreateIndex(
            name: "IX_Outbox_ProcessedAt_OccurredAt",
            schema: "alerts",
            table: "Outbox",
            columns: new[] { "ProcessedAt", "OccurredAt" });
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(
            name: "AlertNotes",
            schema: "alerts");

        migrationBuilder.DropTable(
            name: "AlertTimeline",
            schema: "alerts");

        migrationBuilder.DropTable(
            name: "Outbox",
            schema: "alerts");

        migrationBuilder.DropTable(
            name: "ProcessedEvents",
            schema: "alerts");

        migrationBuilder.DropTable(
            name: "Alerts",
            schema: "alerts");
    }
}
