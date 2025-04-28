using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace RemoteHealthWatch.Vitals.Data.Migrations
{
    /// <inheritdoc />
    public partial class InitialVitals : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "vitals");

            migrationBuilder.CreateTable(
                name: "DeviceCursors",
                schema: "vitals",
                columns: table => new
                {
                    DeviceId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    PatientId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    HighestSeq = table.Column<long>(type: "bigint", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DeviceCursors", x => x.DeviceId);
                });

            migrationBuilder.CreateTable(
                name: "DeviceStates",
                schema: "vitals",
                columns: table => new
                {
                    DeviceId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    PatientId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    DeviceType = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    Paired = table.Column<bool>(type: "bit", nullable: false),
                    ExpectedIntervalSeconds = table.Column<int>(type: "int", nullable: false),
                    SilentSince = table.Column<DateTime>(type: "datetime2", nullable: true),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DeviceStates", x => x.DeviceId);
                });

            migrationBuilder.CreateTable(
                name: "Outbox",
                schema: "vitals",
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
                schema: "vitals",
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
                name: "ReadingKeys",
                schema: "vitals",
                columns: table => new
                {
                    DeviceId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Seq = table.Column<long>(type: "bigint", nullable: false),
                    StoredAt = table.Column<DateTime>(type: "datetime2(0)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ReadingKeys", x => new { x.DeviceId, x.Seq });
                });

            migrationBuilder.CreateTable(
                name: "Readings",
                schema: "vitals",
                columns: table => new
                {
                    DeviceId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Seq = table.Column<long>(type: "bigint", nullable: false),
                    MeasuredAt = table.Column<DateTime>(type: "datetime2(3)", nullable: false),
                    PatientId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Vital = table.Column<byte>(type: "tinyint", nullable: false),
                    Value = table.Column<double>(type: "float", nullable: false),
                    Value2 = table.Column<double>(type: "float", nullable: true),
                    ReceivedAt = table.Column<DateTime>(type: "datetime2(3)", nullable: false),
                    Late = table.Column<bool>(type: "bit", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Readings", x => new { x.DeviceId, x.Seq, x.MeasuredAt })
                        .Annotation("SqlServer:Clustered", false);
                });

            migrationBuilder.CreateTable(
                name: "Rollup1h",
                schema: "vitals",
                columns: table => new
                {
                    PatientId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Vital = table.Column<byte>(type: "tinyint", nullable: false),
                    BucketStart = table.Column<DateTime>(type: "datetime2(0)", nullable: false),
                    Count = table.Column<int>(type: "int", nullable: false),
                    MinValue = table.Column<double>(type: "float", nullable: false),
                    MaxValue = table.Column<double>(type: "float", nullable: false),
                    AvgValue = table.Column<double>(type: "float", nullable: false),
                    AvgValue2 = table.Column<double>(type: "float", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Rollup1h", x => new { x.PatientId, x.Vital, x.BucketStart });
                });

            migrationBuilder.CreateTable(
                name: "Rollup1m",
                schema: "vitals",
                columns: table => new
                {
                    PatientId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Vital = table.Column<byte>(type: "tinyint", nullable: false),
                    BucketStart = table.Column<DateTime>(type: "datetime2(0)", nullable: false),
                    Count = table.Column<int>(type: "int", nullable: false),
                    MinValue = table.Column<double>(type: "float", nullable: false),
                    MaxValue = table.Column<double>(type: "float", nullable: false),
                    AvgValue = table.Column<double>(type: "float", nullable: false),
                    AvgValue2 = table.Column<double>(type: "float", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Rollup1m", x => new { x.PatientId, x.Vital, x.BucketStart });
                });

            migrationBuilder.CreateIndex(
                name: "IX_DeviceStates_PatientId",
                schema: "vitals",
                table: "DeviceStates",
                column: "PatientId");

            migrationBuilder.CreateIndex(
                name: "IX_Outbox_ProcessedAt_OccurredAt",
                schema: "vitals",
                table: "Outbox",
                columns: new[] { "ProcessedAt", "OccurredAt" });

            // monthly partitions; the columnstore sits on the scheme so old months can be switched out cheaply
            migrationBuilder.Sql("CREATE PARTITION FUNCTION pf_vitals_month (datetime2(3)) AS RANGE RIGHT FOR VALUES ('2025-01-01', '2025-02-01', '2025-03-01', '2025-04-01', '2025-05-01', '2025-06-01', '2025-07-01', '2025-08-01', '2025-09-01', '2025-10-01', '2025-11-01', '2025-12-01', '2026-01-01', '2026-02-01', '2026-03-01', '2026-04-01', '2026-05-01', '2026-06-01', '2026-07-01', '2026-08-01', '2026-09-01', '2026-10-01', '2026-11-01', '2026-12-01', '2027-01-01', '2027-02-01', '2027-03-01', '2027-04-01', '2027-05-01', '2027-06-01', '2027-07-01', '2027-08-01', '2027-09-01', '2027-10-01', '2027-11-01', '2027-12-01');");
            migrationBuilder.Sql("CREATE PARTITION SCHEME ps_vitals_month AS PARTITION pf_vitals_month ALL TO ([PRIMARY]);");
            // a heap can't go straight to a partitioned columnstore, and every index on it must be aligned,
            // so drop the PK, partition with a rowstore clustered index, convert, then re-add the PK on the scheme
            migrationBuilder.Sql("ALTER TABLE vitals.Readings DROP CONSTRAINT PK_Readings;");
            migrationBuilder.Sql("CREATE CLUSTERED INDEX CCI_Readings ON vitals.Readings (MeasuredAt) ON ps_vitals_month(MeasuredAt);");
            migrationBuilder.Sql("CREATE CLUSTERED COLUMNSTORE INDEX CCI_Readings ON vitals.Readings WITH (DROP_EXISTING = ON) ON ps_vitals_month(MeasuredAt);");
            migrationBuilder.Sql("ALTER TABLE vitals.Readings ADD CONSTRAINT PK_Readings PRIMARY KEY NONCLUSTERED (DeviceId, Seq, MeasuredAt) ON ps_vitals_month(MeasuredAt);");
            migrationBuilder.Sql("CREATE NONCLUSTERED INDEX IX_Readings_Patient_Time ON vitals.Readings (PatientId, Vital, MeasuredAt DESC) INCLUDE (Value, Value2, DeviceId) ON ps_vitals_month(MeasuredAt);");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "DeviceCursors",
                schema: "vitals");

            migrationBuilder.DropTable(
                name: "DeviceStates",
                schema: "vitals");

            migrationBuilder.DropTable(
                name: "Outbox",
                schema: "vitals");

            migrationBuilder.DropTable(
                name: "ProcessedEvents",
                schema: "vitals");

            migrationBuilder.DropTable(
                name: "ReadingKeys",
                schema: "vitals");

            migrationBuilder.DropTable(
                name: "Readings",
                schema: "vitals");

            migrationBuilder.DropTable(
                name: "Rollup1h",
                schema: "vitals");

            migrationBuilder.DropTable(
                name: "Rollup1m",
                schema: "vitals");

            migrationBuilder.Sql("IF EXISTS (SELECT 1 FROM sys.partition_schemes WHERE name = 'ps_vitals_month') DROP PARTITION SCHEME ps_vitals_month;");
            migrationBuilder.Sql("IF EXISTS (SELECT 1 FROM sys.partition_functions WHERE name = 'pf_vitals_month') DROP PARTITION FUNCTION pf_vitals_month;");
        }
    }
}
