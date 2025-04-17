using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace RemoteHealthWatch.Devices.Data.Migrations
{
    /// <inheritdoc />
    public partial class InitialDevices : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "devices");

            migrationBuilder.CreateTable(
                name: "Devices",
                schema: "devices",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Serial = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false),
                    Type = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    Firmware = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    Status = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    PatientId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    PairedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    ExpectedIntervalSeconds = table.Column<int>(type: "int", nullable: false),
                    ApiKeyPrefix = table.Column<string>(type: "nchar(8)", fixedLength: true, maxLength: 8, nullable: false),
                    ApiKeyHash = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    KeyIssuedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    PreviousKeyHash = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: true),
                    PreviousKeyValidUntil = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    BatteryPercent = table.Column<int>(type: "int", nullable: true),
                    LastSeenAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    LastSeq = table.Column<long>(type: "bigint", nullable: true),
                    SilentSince = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    RegisteredAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    Version = table.Column<Guid>(type: "uniqueidentifier", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Devices", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Outbox",
                schema: "devices",
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
                schema: "devices",
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

            migrationBuilder.CreateIndex(
                name: "IX_Devices_ApiKeyPrefix",
                schema: "devices",
                table: "Devices",
                column: "ApiKeyPrefix",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Devices_PatientId",
                schema: "devices",
                table: "Devices",
                column: "PatientId");

            migrationBuilder.CreateIndex(
                name: "IX_Devices_Serial",
                schema: "devices",
                table: "Devices",
                column: "Serial",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Devices_Status_LastSeenAt",
                schema: "devices",
                table: "Devices",
                columns: new[] { "Status", "LastSeenAt" });

            migrationBuilder.CreateIndex(
                name: "IX_Outbox_ProcessedAt_OccurredAt",
                schema: "devices",
                table: "Outbox",
                columns: new[] { "ProcessedAt", "OccurredAt" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "Devices",
                schema: "devices");

            migrationBuilder.DropTable(
                name: "Outbox",
                schema: "devices");

            migrationBuilder.DropTable(
                name: "ProcessedEvents",
                schema: "devices");
        }
    }
}
