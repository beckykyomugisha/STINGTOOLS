using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable enable

namespace Planscape.Infrastructure.Data.Migrations;

/// <inheritdoc />
public partial class AddSceneNodes : Migration
{
    protected override void Up(MigrationBuilder mb)
    {
        mb.CreateTable(
            name: "SceneNodes",
            columns: t => new
            {
                Id            = t.Column<Guid>("uuid", nullable: false),
                TenantId      = t.Column<Guid>("uuid", nullable: false),
                ProjectId     = t.Column<Guid>("uuid", nullable: false),
                SourceModelId = t.Column<Guid>("uuid", nullable: false),
                Discipline    = t.Column<string>("character varying(8)", maxLength: 8, nullable: false),
                LevelCode     = t.Column<string>("character varying(20)", maxLength: 20, nullable: true),
                SystemCode    = t.Column<string>("character varying(20)", maxLength: 20, nullable: true),
                StoragePath   = t.Column<string>("character varying(500)", maxLength: 500, nullable: false),
                ContentHash   = t.Column<string>("character varying(64)", maxLength: 64, nullable: false),
                FileSizeBytes = t.Column<long>("bigint", nullable: false),
                VertexCount   = t.Column<int>("integer", nullable: false),
                MinX = t.Column<double>("double precision", nullable: false),
                MinY = t.Column<double>("double precision", nullable: false),
                MinZ = t.Column<double>("double precision", nullable: false),
                MaxX = t.Column<double>("double precision", nullable: false),
                MaxY = t.Column<double>("double precision", nullable: false),
                MaxZ = t.Column<double>("double precision", nullable: false),
                Compression   = t.Column<string>("character varying(20)", maxLength: 20, nullable: false, defaultValue: "none"),
                CreatedAt     = t.Column<DateTime>("timestamp with time zone", nullable: false),
                DeletedAt     = t.Column<DateTime>("timestamp with time zone", nullable: true),
            },
            constraints: t => t.PrimaryKey("PK_SceneNodes", x => x.Id));
        mb.CreateIndex("IX_SceneNodes_ProjectId_Discipline", "SceneNodes", new[] { "ProjectId", "Discipline" });
        mb.CreateIndex("IX_SceneNodes_SourceModelId", "SceneNodes", "SourceModelId");
        mb.CreateIndex("IX_SceneNodes_ContentHash", "SceneNodes", "ContentHash");
    }

    protected override void Down(MigrationBuilder mb) => mb.DropTable("SceneNodes");
}
