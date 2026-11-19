using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Split.Api.Data;

namespace Split.Api.Tests;

/// <summary>
/// The startup schema update has to cope with databases left by earlier versions, and getting
/// it wrong is invisible: SQLite does not complain about a type mismatch, it silently stores
/// the wrong thing and every later comparison quietly means nothing.
/// </summary>
public class DatabaseSchemaSetupTests : IDisposable
{
    private readonly SqliteConnection connection = new("Data Source=:memory:");

    public DatabaseSchemaSetupTests() => connection.Open();

    public void Dispose() => connection.Dispose();

    private SplitDbContext NewContext() =>
        new(new DbContextOptionsBuilder<SplitDbContext>().UseSqlite(connection).Options);

    private static readonly (string Table, string Column)[] EveryDateColumn =
    [
        ("Groups", "CreatedAt"),
        ("Groups", "LastActivityAt"),
        ("Expenses", "CreatedAt"),
        ("Users", "CreatedAt"),
        ("Users", "LastSeenAt"),
        ("GroupMemberships", "FirstSeenAt"),
        ("GroupMemberships", "LastSeenAt"),
    ];

    [Fact]
    public void A_fresh_database_stores_every_date_as_an_integer()
    {
        using SplitDbContext db = NewContext();
        DatabaseSchemaSetup.Initialize(db);

        foreach ((string table, string column) in EveryDateColumn)
        {
            Assert.Equal("INTEGER", ColumnType(table, column));
        }
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

        foreach ((string table, string column) in EveryDateColumn)
        {
            Assert.Equal("INTEGER", ColumnType(table, column));
        }
    }

    [Fact]
    public void Legacy_text_date_columns_are_rebuilt_as_integers_without_losing_the_time()
    {
        // A database from before the convention: dates held as the ISO text EF used to write.
        var created = new DateTimeOffset(2026, 3, 4, 5, 6, 7, TimeSpan.Zero);

        BuildLegacyDatabase(created);

        foreach ((string table, string column) in EveryDateColumn.Where(c => c.Column != "LastActivityAt"))
        {
            Assert.Equal("TEXT", ColumnType(table, column));
        }

        using (SplitDbContext upgrade = NewContext())
        {
            DatabaseSchemaSetup.Initialize(upgrade);
        }

        foreach ((string table, string column) in EveryDateColumn)
        {
            Assert.Equal("INTEGER", ColumnType(table, column));
        }

        // The point of the exercise: the instant survives, to the second.
        using SplitDbContext check = NewContext();
        ExpenseGroup group = Assert.Single(check.Groups);
        Assert.Equal(created, group.CreatedAt);

        User user = Assert.Single(check.Users);
        Assert.Equal(created, user.CreatedAt);

        GroupMembership membership = Assert.Single(check.GroupMemberships);
        Assert.Equal(created, membership.FirstSeenAt);
    }

    [Fact]
    public async Task After_the_upgrade_queries_that_touch_dates_actually_work()
    {
        // Both of the failures this convention exists to prevent, against a database that
        // started life with text dates.
        BuildLegacyDatabase(new DateTimeOffset(2026, 3, 4, 5, 6, 7, TimeSpan.Zero));

        using (SplitDbContext upgrade = NewContext())
        {
            DatabaseSchemaSetup.Initialize(upgrade);
        }

        using SplitDbContext db = NewContext();

        // A comparison — the retention sweep. The seeded group is months old, so it must come
        // back; asserting that it does proves the filter is really comparing, where asserting
        // an empty result would also have passed if the comparison matched nothing at all.
        DateTimeOffset cutoff = DateTimeOffset.UtcNow.AddDays(-90);

        List<string> stale = await db.Groups
            .Where(g => g.LastActivityAt < cutoff)
            .Select(g => g.Name)
            .ToListAsync();
        Assert.Equal(new[] { "Legacy" }, stale.ToArray());

        // And the other side of the comparison: nothing is stale against a cutoff before it.
        DateTimeOffset longAgo = new DateTimeOffset(2020, 1, 1, 0, 0, 0, TimeSpan.Zero);

        List<string> none = await db.Groups
            .Where(g => g.LastActivityAt < longAgo)
            .Select(g => g.Name)
            .ToListAsync();
        Assert.Empty(none);

        // An ordering — the thing SQLite refuses outright for a DateTimeOffset.
        List<string> byDate = await db.Groups
            .OrderByDescending(g => g.CreatedAt)
            .Select(g => g.Name)
            .ToListAsync();
        Assert.Single(byDate);
    }

    [Fact]
    public void A_date_that_cannot_be_parsed_becomes_now_rather_than_breaking_the_row()
    {
        BuildLegacyDatabase(new DateTimeOffset(2026, 3, 4, 5, 6, 7, TimeSpan.Zero));

        using (var command = connection.CreateCommand())
        {
            command.CommandText = "UPDATE \"Groups\" SET \"CreatedAt\" = 'not a date';";
            command.ExecuteNonQuery();
        }

        using (SplitDbContext upgrade = NewContext())
        {
            DatabaseSchemaSetup.Initialize(upgrade);
        }

        // CreatedAt is not nullable, so leaving a NULL behind would make the row unreadable.
        using SplitDbContext check = NewContext();
        ExpenseGroup group = Assert.Single(check.Groups);
        Assert.True(group.CreatedAt > DateTimeOffset.UtcNow.AddMinutes(-5));
    }

    /// <summary>
    /// Creates the schema as an earlier build left it: every date column TEXT, holding the
    /// ISO strings EF wrote before dates became integers.
    /// </summary>
    private void BuildLegacyDatabase(DateTimeOffset when)
    {
        string iso = when.UtcDateTime.ToString("yyyy-MM-dd HH:mm:ss.fffffff") + "+00:00";

        using SplitDbContext build = NewContext();
        build.Database.EnsureCreated();

        foreach ((string table, string column) in EveryDateColumn)
        {
            string drop = $"ALTER TABLE \"{table}\" DROP COLUMN \"{column}\";";
            string add = $"ALTER TABLE \"{table}\" ADD COLUMN \"{column}\" TEXT NULL;";
            build.Database.ExecuteSqlRaw(drop);
            build.Database.ExecuteSqlRaw(add);
        }

        string seed =
            $"""
            INSERT INTO "Groups" ("Id","Slug","Name","Currency","CreatedAt","LastActivityAt")
            VALUES ('11111111-1111-1111-1111-111111111111','legacy','Legacy','MYR','{iso}','{iso}');

            INSERT INTO "Users" ("Id","GoogleSubject","Email","DisplayName","AvatarUrl","CreatedAt","LastSeenAt")
            VALUES ('22222222-2222-2222-2222-222222222222','sub','a@example.com','Tester',NULL,'{iso}','{iso}');

            INSERT INTO "GroupMemberships" ("Id","UserId","GroupId","IsCreator","FirstSeenAt","LastSeenAt")
            VALUES ('33333333-3333-3333-3333-333333333333',
                    '22222222-2222-2222-2222-222222222222',
                    '11111111-1111-1111-1111-111111111111', 1, '{iso}','{iso}');
            """;
        build.Database.ExecuteSqlRaw(seed);
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
}
