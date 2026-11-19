using System.Data.Common;
using Microsoft.EntityFrameworkCore;

namespace Split.Api.Data;

/// <summary>
/// Keeps databases created by earlier versions usable while this small app still uses
/// EnsureCreated instead of EF migrations.
///
/// <para>
/// EnsureCreated does exactly one thing: if there is no database, it creates one matching the
/// current model. It will not add a table to an existing database, will not add a column, and
/// will certainly not change a column's type. Everything below exists to cover that gap, and
/// every step is written to be safe to run on every startup.
/// </para>
/// </summary>
public static class DatabaseSchemaSetup
{
    /// <summary>
    /// Every date column in the model, which must all have INTEGER affinity because dates are
    /// stored as Unix seconds — see the convention in <see cref="SplitDbContext"/>.
    ///
    /// The type matters, not just the presence of the column. SQLite applies a column's
    /// declared affinity to everything written to it, so an integer going into a column
    /// declared TEXT is silently stored as text, and comparing it against a number then
    /// quietly means nothing. That is not hypothetical: it is how the retention sweep went on
    /// doing nothing after the value had supposedly been fixed.
    /// </summary>
    private static readonly (string Table, string Column)[] DateColumns =
    [
        ("Groups", "CreatedAt"),
        ("Groups", "LastActivityAt"),
        ("Expenses", "CreatedAt"),
        ("Users", "CreatedAt"),
        ("Users", "LastSeenAt"),
        ("GroupMemberships", "FirstSeenAt"),
        ("GroupMemberships", "LastSeenAt"),
    ];

    public static void Initialize(SplitDbContext db)
    {
        db.Database.EnsureCreated();
        db.Database.OpenConnection();

        try
        {
            // The sign-in history tables, for databases created before accounts existed.
            // Declared with INTEGER dates to match the model; a database that already has
            // them with TEXT dates is repaired by the column pass below.
            db.Database.ExecuteSqlRaw(
                """
                CREATE TABLE IF NOT EXISTS "Users" (
                    "Id" TEXT NOT NULL CONSTRAINT "PK_Users" PRIMARY KEY,
                    "GoogleSubject" TEXT NOT NULL,
                    "Email" TEXT NOT NULL,
                    "DisplayName" TEXT NOT NULL,
                    "AvatarUrl" TEXT NULL,
                    "CreatedAt" INTEGER NOT NULL,
                    "LastSeenAt" INTEGER NOT NULL
                );
                CREATE UNIQUE INDEX IF NOT EXISTS "IX_Users_GoogleSubject"
                    ON "Users" ("GoogleSubject");

                CREATE TABLE IF NOT EXISTS "GroupMemberships" (
                    "Id" TEXT NOT NULL CONSTRAINT "PK_GroupMemberships" PRIMARY KEY,
                    "UserId" TEXT NOT NULL,
                    "GroupId" TEXT NOT NULL,
                    "IsCreator" INTEGER NOT NULL,
                    "FirstSeenAt" INTEGER NOT NULL,
                    "LastSeenAt" INTEGER NOT NULL
                );
                CREATE UNIQUE INDEX IF NOT EXISTS "IX_GroupMemberships_UserId_GroupId"
                    ON "GroupMemberships" ("UserId", "GroupId");
                CREATE INDEX IF NOT EXISTS "IX_GroupMemberships_UserId"
                    ON "GroupMemberships" ("UserId");
                """);

            // LastActivityAt arrived after the Groups table, so it may be missing entirely.
            if (ColumnType(db.Database.GetDbConnection(), "Groups", "LastActivityAt") is null)
            {
                db.Database.ExecuteSqlRaw("ALTER TABLE \"Groups\" ADD COLUMN \"LastActivityAt\" INTEGER NULL;");
            }

            foreach ((string table, string column) in DateColumns)
            {
                ConvertToUnixSeconds(db, table, column);
            }

            // Groups that predate the retention feature have no activity time. They get a full
            // retention period from now rather than being judged on something never recorded.
            db.Database.ExecuteSqlInterpolated(
                $"""
                UPDATE "Groups"
                SET "LastActivityAt" = {DateTimeOffset.UtcNow.ToUnixTimeSeconds()}
                WHERE "LastActivityAt" IS NULL;
                """);
        }
        finally
        {
            db.Database.CloseConnection();
        }
    }

    /// <summary>
    /// Rebuilds one date column as INTEGER Unix seconds, converting whatever text is in it.
    /// Does nothing if the column is absent or already INTEGER, so it is free to run on every
    /// startup.
    /// </summary>
    private static void ConvertToUnixSeconds(SplitDbContext db, string table, string column)
    {
        DbConnection connection = db.Database.GetDbConnection();

        string? declared = ColumnType(connection, table, column);
        if (declared is null || declared.Equals("INTEGER", StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        // SQLite cannot change a column's type in place, so the column is replaced: add the
        // new one, convert the values across, drop the old, rename. None of these columns is
        // indexed or part of a constraint, which is what makes DROP COLUMN legal here.
        string temporary = $"{column}__unix";

        // Built as a string first and then passed. EF Core treats an interpolated string
        // handed straight to ExecuteSqlRaw as parameterised, which turns the table name into
        // a bound parameter and produces "no such table: @p0". Identifiers cannot be
        // parameters in SQL. These names come from the constant array above, never from user
        // input, so composing them is safe.
        string addColumn = $"ALTER TABLE \"{table}\" ADD COLUMN \"{temporary}\" INTEGER NULL;";
        db.Database.ExecuteSqlRaw(addColumn);

        // The text EF wrote looks like '2026-11-18 06:48:26.2652607+00:00'. SQLite's strftime
        // wants 'YYYY-MM-DD HH:MM:SS', so the fractional seconds and offset are trimmed — the
        // offset is always +00:00 because every value this app writes is UtcNow. A value that
        // will not parse yields NULL, which the backfill below turns into the current time
        // rather than leaving a non-nullable column unreadable.
        string convert =
            $"""
            UPDATE "{table}" SET "{temporary}" = CASE
                WHEN "{column}" IS NULL THEN NULL
                WHEN typeof("{column}") = 'integer' THEN CAST("{column}" AS INTEGER)
                ELSE CAST(strftime('%s', substr(replace("{column}", 'T', ' '), 1, 19)) AS INTEGER)
            END;
            """;
        db.Database.ExecuteSqlRaw(convert);

        string backfill =
            $"""
            UPDATE "{table}" SET "{temporary}" = {DateTimeOffset.UtcNow.ToUnixTimeSeconds()}
            WHERE "{temporary}" IS NULL AND "{column}" IS NOT NULL;
            """;
        db.Database.ExecuteSqlRaw(backfill);

        string dropOld = $"ALTER TABLE \"{table}\" DROP COLUMN \"{column}\";";
        string renameNew = $"ALTER TABLE \"{table}\" RENAME COLUMN \"{temporary}\" TO \"{column}\";";
        db.Database.ExecuteSqlRaw(dropOld);
        db.Database.ExecuteSqlRaw(renameNew);
    }

    /// <summary>
    /// The declared type of a column, or null when the table or column does not exist.
    /// Returns the type rather than a yes/no, because a column of the wrong type is a
    /// different problem from a missing one and needs a different repair.
    /// </summary>
    private static string? ColumnType(DbConnection connection, string table, string column)
    {
        using DbCommand command = connection.CreateCommand();
        command.CommandText = $"PRAGMA table_info(\"{table}\");";

        using DbDataReader reader = command.ExecuteReader();
        while (reader.Read())
        {
            // PRAGMA table_info returns: cid, name, type, notnull, dflt_value, pk
            if (string.Equals(reader.GetString(1), column, StringComparison.OrdinalIgnoreCase))
            {
                return reader.IsDBNull(2) ? string.Empty : reader.GetString(2);
            }
        }

        return null;
    }
}
