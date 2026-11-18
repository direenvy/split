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

            // LastActivityAt holds Unix seconds, so the column must have INTEGER affinity.
            //
            // The type matters, not just the presence of the column. SQLite applies the
            // declared affinity to everything written: put an integer into a TEXT column and
            // it is silently stored as text, after which comparing it to a number no longer
            // means what it looks like. An early build of this feature declared the column
            // TEXT, so a database created by that build has to have it rebuilt rather than
            // merely backfilled — otherwise the retention sweep goes on quietly doing nothing.
            string? declaredType = GroupActivityColumnType(db.Database.GetDbConnection());

            if (declaredType is not null && !declaredType.Equals("INTEGER", StringComparison.OrdinalIgnoreCase))
            {
                // Nothing of value is lost: the only values this column ever held were written
                // by the build that could not read them back, and every row is backfilled below.
                db.Database.ExecuteSqlRaw("ALTER TABLE \"Groups\" DROP COLUMN \"LastActivityAt\";");
                declaredType = null;
            }

            if (declaredType is null)
            {
                db.Database.ExecuteSqlRaw("ALTER TABLE \"Groups\" ADD COLUMN \"LastActivityAt\" INTEGER NULL;");
            }

            // Existing groups get a full retention period when this feature is first
            // installed, rather than being judged on an activity time that older versions
            // never recorded.
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

    /// <summary>
    /// The declared type of Groups.LastActivityAt, or null when the column is absent.
    /// Returns the type rather than a yes/no, because a column of the wrong type is a
    /// different problem from a missing one and needs a different repair.
    /// </summary>
    private static string? GroupActivityColumnType(DbConnection connection)
    {
        using DbCommand command = connection.CreateCommand();
        command.CommandText = "PRAGMA table_info(\"Groups\");";

        using DbDataReader reader = command.ExecuteReader();
        while (reader.Read())
        {
            // PRAGMA table_info returns: cid, name, type, notnull, dflt_value, pk
            if (string.Equals(reader.GetString(1), "LastActivityAt", StringComparison.OrdinalIgnoreCase))
            {
                return reader.IsDBNull(2) ? string.Empty : reader.GetString(2);
            }
        }

        return null;
    }
}
