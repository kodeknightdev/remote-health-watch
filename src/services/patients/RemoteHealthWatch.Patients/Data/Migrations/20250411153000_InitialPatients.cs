using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace RemoteHealthWatch.Patients.Data.Migrations
{
    /// <inheritdoc />
    public partial class InitialPatients : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "patients");

            migrationBuilder.CreateTable(
                name: "AuditLog",
                schema: "patients",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    At = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    UserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Role = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false),
                    PatientId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    Action = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false),
                    Resource = table.Column<string>(type: "nvarchar(80)", maxLength: 80, nullable: false),
                    Service = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false),
                    Allowed = table.Column<bool>(type: "bit", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AuditLog", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Outbox",
                schema: "patients",
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
                name: "Patients",
                schema: "patients",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Mrn = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    FirstName = table.Column<string>(type: "nvarchar(80)", maxLength: 80, nullable: false),
                    LastName = table.Column<string>(type: "nvarchar(80)", maxLength: 80, nullable: false),
                    DateOfBirth = table.Column<DateOnly>(type: "date", nullable: false),
                    Sex = table.Column<string>(type: "nvarchar(10)", maxLength: 10, nullable: false),
                    Conditions = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    Status = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    PlanVitals = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    PlanIntervalSeconds = table.Column<int>(type: "int", nullable: false),
                    SpO2Scale = table.Column<int>(type: "int", nullable: false),
                    OnSupplementalOxygen = table.Column<bool>(type: "bit", nullable: false),
                    RequestedDeviceSerial = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: true),
                    PairedDeviceCount = table.Column<int>(type: "int", nullable: false),
                    LastPairingError = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    EnrolledAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    BaselineStartedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    MonitoringActiveAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    DischargedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    Version = table.Column<Guid>(type: "uniqueidentifier", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Patients", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ProcessedEvents",
                schema: "patients",
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
                name: "CareTeamMembers",
                schema: "patients",
                columns: table => new
                {
                    PatientId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    UserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Role = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false),
                    DisplayName = table.Column<string>(type: "nvarchar(120)", maxLength: 120, nullable: false),
                    Email = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    Phone = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    IsPrimary = table.Column<bool>(type: "bit", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CareTeamMembers", x => new { x.PatientId, x.UserId });
                    table.ForeignKey(
                        name: "FK_CareTeamMembers_Patients_PatientId",
                        column: x => x.PatientId,
                        principalSchema: "patients",
                        principalTable: "Patients",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "Thresholds",
                schema: "patients",
                columns: table => new
                {
                    PatientId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Metric = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    LowCritical = table.Column<double>(type: "float", nullable: true),
                    LowWarning = table.Column<double>(type: "float", nullable: true),
                    HighWarning = table.Column<double>(type: "float", nullable: true),
                    HighCritical = table.Column<double>(type: "float", nullable: true),
                    Source = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    UpdatedBy = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Thresholds", x => new { x.PatientId, x.Metric });
                    table.ForeignKey(
                        name: "FK_Thresholds_Patients_PatientId",
                        column: x => x.PatientId,
                        principalSchema: "patients",
                        principalTable: "Patients",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_AuditLog_PatientId_At",
                schema: "patients",
                table: "AuditLog",
                columns: new[] { "PatientId", "At" });

            migrationBuilder.CreateIndex(
                name: "IX_AuditLog_UserId_At",
                schema: "patients",
                table: "AuditLog",
                columns: new[] { "UserId", "At" });

            migrationBuilder.CreateIndex(
                name: "IX_CareTeamMembers_UserId",
                schema: "patients",
                table: "CareTeamMembers",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_Outbox_ProcessedAt_OccurredAt",
                schema: "patients",
                table: "Outbox",
                columns: new[] { "ProcessedAt", "OccurredAt" });

            migrationBuilder.CreateIndex(
                name: "IX_Patients_Mrn",
                schema: "patients",
                table: "Patients",
                column: "Mrn",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Patients_Status",
                schema: "patients",
                table: "Patients",
                column: "Status");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AuditLog",
                schema: "patients");

            migrationBuilder.DropTable(
                name: "CareTeamMembers",
                schema: "patients");

            migrationBuilder.DropTable(
                name: "Outbox",
                schema: "patients");

            migrationBuilder.DropTable(
                name: "ProcessedEvents",
                schema: "patients");

            migrationBuilder.DropTable(
                name: "Thresholds",
                schema: "patients");

            migrationBuilder.DropTable(
                name: "Patients",
                schema: "patients");
        }
    }
}
