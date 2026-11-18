using System.Data.Common;
using Microsoft.EntityFrameworkCore;

namespace Split.Api.Data;

/// <summary>
/// Keeps databases created by earlier versions usable while this small app still uses
/// EnsureCreated instead of EF migrations.
/// </summary>
public static class DatabaseSchemaSetup
{
    public static void Initialize(SplitDbContext db)
    {
        db.Database.EnsureCreated();
        db.Database.OpenConnection();

        try
        {
            // EnsureCreated does not add newly introduced tables to an existing database.
            // Create the optional sign-in history tables idempotently so upgrades do not
            // break /api/groups/mine for people who already have a split.db.
            db.Database.ExecuteSqlRaw(
                """
                CREATE TABLE IF NOT EXISTS "Users" (
                    "Id" TEXT NOT NULL CONSTRAINT "PK_Users" PRIMARY KEY,
                    "GoogleSubject" TEXT NOT NULL,
                    "Email" TEXT NOT NULL,
                    "DisplayName" TEXT NOT NULL,
                    "AvatarUrl" TEXT NULL,
                    "CreatedAt" TEXT NOT NULL,
                    "LastSeenAt" TEXT NOT NULL
                );
                CREATE UNIQUE INDEX IF NOT EXISTS "IX_Users_GoogleSubject"
                    ON "Users" ("GoogleSubject");

                CREATE TABLE IF NOT EXISTS "GroupMemberships" (
                    "Id" TEXT NOT NULL CONSTRAINT "PK_GroupMemberships" PRIMARY KEY,
                    "UserId" TEXT NOT NULL,
                    "GroupId" TEXT NOT NULL,
                    "IsCreator" INTEGER NOT NULL,
                    "FirstSeenAt" TEXT NOT NULL,
                    "LastSeenAt" TEXT NOT NULL
                );
                CREATE UNIQUE INDEX IF NOT EXISTS "IX_GroupMemberships_UserId_GroupId"
                    ON "GroupMemberships" ("UserId", "GroupId");
                CREATE INDEX IF NOT EXISTS "IX_GroupMemberships_UserId"
                    ON "GroupMemberships" ("UserId");
                """);

            if (!HasGroupActivityColumn(db.Database.GetDbConnection()))
            {
                // INTEGER, not TEXT: LastActivityAt holds Unix seconds, because EF's SQLite
                // provider cannot translate a DateTimeOffset comparison and the retention
                // sweep is exactly one. See the converter in SplitDbContext.
                db.Database.ExecuteSqlRaw("ALTER TABLE \"Groups\" ADD COLUMN \"LastActivityAt\" INTEGER NULL;");
            }

            // Existing groups get a full retention period when this feature is first
            // installed, rather than being judged on an activity time that older versions
            // never recorded. The typeof() test also repairs rows left by the first cut of
            // this feature, which wrote an ISO timestamp here before the column became an
            // integer: anything that is not a number counts as missing.
            db.Database.ExecuteSqlInterpolated(
                $"""
                UPDATE "Groups"
                SET "LastActivityAt" = {DateTimeOffset.UtcNow.ToUnixTimeSeconds()}
                WHERE "LastActivityAt" IS NULL OR typeof("LastActivityAt") <> 'integer';
                """);
        }
        finally
        {
            db.Database.CloseConnection();
        }
    }

    private static bool HasGroupActivityColumn(DbConnection connection)
    {
        using DbCommand command = connection.CreateCommand();
        command.CommandText = "PRAGMA table_info(\"Groups\");";

        using DbDataReader reader = command.ExecuteReader();
        while (reader.Read())
        {
            if (string.Equals(reader.GetString(1), "LastActivityAt", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }
}
