using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Split.Api.Data;

namespace Split.Api.Tests;

/// <summary>
/// The startup schema update has to cope with databases left by earlier builds, and getting
/// that wrong is invisible: SQLite does not complain, it just stores the wrong thing.
/// </summary>
public class DatabaseSchemaSetupTests : IDisposable
{
    private readonly SqliteConnection connection = new("Data Source=:memory:");

    public DatabaseSchemaSetupTests() => connection.Open();

    public void Dispose() => connection.Dispose();

    private SplitDbContext NewContext() =>
        new(new DbContextOptionsBuilder<SplitDbContext>().UseSqlite(connection).Options);

    [Fact]
    public void A_fresh_database_gets_an_integer_activity_column()
    {
        using SplitDbContext db = NewContext();
        DatabaseSchemaSetup.Initialize(db);

        Assert.Equal("INTEGER", ColumnType("Groups", "LastActivityAt"));
    }

    [Fact]
    public void Running_it_twice_changes_nothing()
    {
        using (SplitDbContext first = NewContext())
        {
            DatabaseSchemaSetup.Initialize(first);
        }

        using SplitDbContext second = NewContext();
        DatabaseSchemaSetup.Initialize(second);

        Assert.Equal("INTEGER", ColumnType("Groups", "LastActivityAt"));
    }

    [Fact]
    public void A_legacy_text_activity_column_is_rebuilt_as_an_integer()
    {
        // Reproduces a database left by the first cut of the retention feature, which
        // declared the column TEXT. Simply backfilling it would not have been enough:
        // a TEXT column coerces every integer written to it back into text, so the
        // retention sweep would have carried on comparing numbers against strings.
        using (SplitDbContext build = NewContext())
        {
            build.Database.EnsureCreated();
            build.Database.ExecuteSqlRaw("ALTER TABLE \"Groups\" DROP COLUMN \"LastActivityAt\";");
            build.Database.ExecuteSqlRaw("ALTER TABLE \"Groups\" ADD COLUMN \"LastActivityAt\" TEXT NULL;");
            build.Database.ExecuteSqlRaw(
                """
                INSERT INTO "Groups" ("Id","Slug","Name","Currency","CreatedAt","LastActivityAt")
                VALUES ('11111111-1111-1111-1111-111111111111','legacy','Legacy','MYR',
                        '2026-01-01 00:00:00+00:00','2020-01-01 00:00:00+00:00');
                """);
        }

        Assert.Equal("TEXT", ColumnType("Groups", "LastActivityAt"));

        using (SplitDbContext upgrade = NewContext())
        {
            DatabaseSchemaSetup.Initialize(upgrade);
        }

        Assert.Equal("INTEGER", ColumnType("Groups", "LastActivityAt"));
        Assert.Equal("integer", StoredTypeOfFirstActivityValue());

        // The row survives the rebuild, with a fresh retention window rather than a date
        // inherited from a build that could not read it back.
        using SplitDbContext check = NewContext();
        ExpenseGroup group = Assert.Single(check.Groups);
        Assert.Equal("Legacy", group.Name);
        Assert.True(group.LastActivityAt > DateTimeOffset.UtcNow.AddMinutes(-5));
    }

    [Fact]
    public async Task After_the_upgrade_the_retention_query_actually_filters()
    {
        using (SplitDbContext build = NewContext())
        {
            build.Database.EnsureCreated();
            build.Database.ExecuteSqlRaw("ALTER TABLE \"Groups\" DROP COLUMN \"LastActivityAt\";");
            build.Database.ExecuteSqlRaw("ALTER TABLE \"Groups\" ADD COLUMN \"LastActivityAt\" TEXT NULL;");
        }

        using (SplitDbContext upgrade = NewContext())
        {
            DatabaseSchemaSetup.Initialize(upgrade);
        }

        using (SplitDbContext seed = NewContext())
        {
            seed.Groups.Add(new ExpenseGroup
            {
                Slug = "stale",
                Name = "Stale",
                Currency = "MYR",
                LastActivityAt = DateTimeOffset.UtcNow.AddDays(-200),
            });

            seed.Groups.Add(new ExpenseGroup
            {
                Slug = "fresh",
                Name = "Fresh",
                Currency = "MYR",
                LastActivityAt = DateTimeOffset.UtcNow,
            });

            await seed.SaveChangesAsync();
        }

        using SplitDbContext db = NewContext();
        DateTimeOffset cutoff = DateTimeOffset.UtcNow.AddDays(-90);

        List<string> expired = await db.Groups
            .Where(g => g.LastActivityAt < cutoff)
            .Select(g => g.Name)
            .ToListAsync();

        Assert.Equal(new[] { "Stale" }, expired.ToArray());
    }

    private string? ColumnType(string table, string column)
    {
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = $"PRAGMA table_info(\"{table}\");";

        using SqliteDataReader reader = command.ExecuteReader();
        while (reader.Read())
        {
            if (string.Equals(reader.GetString(1), column, StringComparison.OrdinalIgnoreCase))
            {
                return reader.GetString(2);
            }
        }

        return null;
    }

    private string? StoredTypeOfFirstActivityValue()
    {
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = "SELECT typeof(\"LastActivityAt\") FROM \"Groups\" LIMIT 1;";
        return command.ExecuteScalar() as string;
    }
}
