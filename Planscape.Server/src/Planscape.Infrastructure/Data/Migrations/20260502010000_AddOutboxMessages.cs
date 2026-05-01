using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable enable

namespace Planscape.Infrastructure.Data.Migrations;

/// <inheritdoc />
/// <remarks>
/// S3.2 — adds the <c>OutboxMessages</c> table behind the transactional
/// outbox pattern. Domain handlers persist a row inside the same DB
/// transaction as the state change; OutboxDispatcher drains the table
/// every minute and dispatches to the right channel.
/// </remarks>
public partial class AddOutboxMessages : Migration
{
    protected override void Up(MigrationBuilder mb)
    {
        mb.CreateTable(
            name: "OutboxMessages",
            columns: t => new
            {
                Id            = t.Column<Guid>("uuid", nullable: false),
                TenantId      = t.Column<Guid>("uuid", nullable: false),
                Channel       = t.Column<string>("character varying(40)", maxLength: 40, nullable: false),
                Topic         = t.Column<string>("character varying(80)", maxLength: 80, nullable: false),
                PayloadJson   = t.Column<string>("jsonb", nullable: false),
                CreatedAt     = t.Column<DateTime>("timestamp with time zone", nullable: false),
                DispatchedAt  = t.Column<DateTime>("timestamp with time zone", nullable: true),
                Attempts      = t.Column<int>("integer", nullable: false, defaultValue: 0),
                LastAttemptAt = t.Column<DateTime>("timestamp with time zone", nullable: true),
                LastError     = t.Column<string>("text", nullable: true),
                NextAttemptAt = t.Column<DateTime>("timestamp with time zone", nullable: true),
                Status        = t.Column<int>("integer", nullable: false, defaultValue: 0),
            },
            constraints: t => t.PrimaryKey("PK_OutboxMessages", x => x.Id));
        mb.CreateIndex("IX_OutboxMessages_Status_NextAttemptAt", "OutboxMessages",
            new[] { "Status", "NextAttemptAt" });
        mb.CreateIndex("IX_OutboxMessages_TenantId", "OutboxMessages", "TenantId");
    }

    protected override void Down(MigrationBuilder mb)
    {
        mb.DropTable("OutboxMessages");
    }
}
