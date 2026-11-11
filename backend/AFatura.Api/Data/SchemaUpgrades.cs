using Microsoft.EntityFrameworkCore;

namespace AFatura.Api.Data;

/// <summary>
/// Idempotent SQL upgrades for databases created with EnsureCreated (no EF migrations yet).
/// Each step must be safe to run on every start: fresh databases already have everything, so steps are no-ops there.
/// When the project switches to EF migrations, create the initial migration from the current model and drop this class.
/// </summary>
public static class SchemaUpgrades
{
    private static readonly (string Name, string Sql)[] Steps =
    [
        ("2026-11 projects", """
            CREATE TABLE IF NOT EXISTS "Projects" (
                "Id" uuid NOT NULL PRIMARY KEY,
                "OrganizationId" uuid NOT NULL,
                "Name" text NOT NULL,
                "Code" text NULL,
                "ClientName" text NULL,
                "Description" text NULL,
                "Color" text NOT NULL,
                "Budget" numeric(14,2) NULL,
                "StartDate" date NULL,
                "EndDate" date NULL,
                "IsActive" boolean NOT NULL,
                "CreatedAt" timestamp with time zone NOT NULL
            );
            CREATE INDEX IF NOT EXISTS "IX_Projects_OrganizationId_Name" ON "Projects" ("OrganizationId", "Name");
            ALTER TABLE "Invoices" ADD COLUMN IF NOT EXISTS "ProjectId" uuid NULL;
            CREATE INDEX IF NOT EXISTS "IX_Invoices_ProjectId" ON "Invoices" ("ProjectId");
            DO $$ BEGIN
                IF NOT EXISTS (SELECT 1 FROM pg_constraint WHERE conname = 'FK_Invoices_Projects_ProjectId') THEN
                    ALTER TABLE "Invoices" ADD CONSTRAINT "FK_Invoices_Projects_ProjectId"
                        FOREIGN KEY ("ProjectId") REFERENCES "Projects" ("Id") ON DELETE SET NULL;
                END IF;
            END $$;
            """),
        ("2026-11 e-fatura ignore/comment + multi-invoice documents", """
            ALTER TABLE "EFaturaRecords" ADD COLUMN IF NOT EXISTS "Ignored" boolean NOT NULL DEFAULT FALSE;
            ALTER TABLE "EFaturaRecords" ADD COLUMN IF NOT EXISTS "Comment" text NULL;
            ALTER TABLE "EFaturaRecords" ADD COLUMN IF NOT EXISTS "UpdatedAt" timestamp with time zone NULL;
            ALTER TABLE "EFaturaRecords" ADD COLUMN IF NOT EXISTS "UpdatedByUserId" uuid NULL;
            ALTER TABLE "Invoices" ADD COLUMN IF NOT EXISTS "SubDocuments" jsonb NOT NULL DEFAULT '[]'::jsonb;
            """),
    ];

    public static async Task ApplyAsync(AppDbContext db, ILogger log)
    {
        foreach (var (name, sql) in Steps)
        {
            await db.Database.ExecuteSqlRawAsync(sql);
            log.LogInformation("Schema upgrade checked: {Name}", name);
        }
    }
}
