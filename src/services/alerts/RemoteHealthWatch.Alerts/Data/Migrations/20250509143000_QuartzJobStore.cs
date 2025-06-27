using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace RemoteHealthWatch.Alerts.Data.Migrations;

/// <inheritdoc />
public partial class QuartzJobStore : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        foreach (var batch in QuartzSchema.Batches())
            migrationBuilder.Sql(batch);
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        foreach (var table in new[] { "QRTZ_SIMPLE_TRIGGERS", "QRTZ_SIMPROP_TRIGGERS", "QRTZ_CRON_TRIGGERS", "QRTZ_BLOB_TRIGGERS",
                     "QRTZ_TRIGGERS", "QRTZ_JOB_DETAILS", "QRTZ_CALENDARS", "QRTZ_PAUSED_TRIGGER_GRPS", "QRTZ_FIRED_TRIGGERS",
                     "QRTZ_SCHEDULER_STATE", "QRTZ_LOCKS" })
            migrationBuilder.Sql($"DROP TABLE IF EXISTS [dbo].[{table}];");
    }
}
