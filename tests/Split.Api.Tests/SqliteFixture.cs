using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Split.Api.Data;

namespace Split.Api.Tests;

/// <summary>
/// A real SQLite database for one test, held open in memory.
///
/// Deliberately SQLite and not the in-memory provider. Every bug these tests exist to catch
/// is a bug in what SQLite can and cannot do — translating a date comparison, honouring a
/// cascade, coercing an integer into a text column. The in-memory provider has none of those
/// limits, so it would pass happily while production broke.
/// </summary>
public sealed class SqliteFixture : IDisposable
{
    private readonly SqliteConnection connection;

    public SqliteFixture()
    {
        // A named shared cache keeps the database alive as long as this connection is open,
        // and throws it away afterwards, so tests cannot see each other's rows.
        connection = new SqliteConnection("Data Source=:memory:");
        connection.Open();

        Options = new DbContextOptionsBuilder<SplitDbContext>()
            .UseSqlite(connection)
            .Options;

        using SplitDbContext db = NewContext();
        db.Database.EnsureCreated();
    }

    public DbContextOptions<SplitDbContext> Options { get; }

    public SplitDbContext NewContext() => new(Options);

    public void Dispose() => connection.Dispose();
}
