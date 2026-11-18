using Microsoft.EntityFrameworkCore;
using Split.Api.Data;

namespace Split.Api.Tests;

/// <summary>
/// These tests exist because of a specific class of bug this project has already shipped
/// twice: a LINQ query that compiles, reads correctly, and throws at runtime because SQLite
/// cannot translate it.
///
/// SQLite has no date type. EF stores a <see cref="DateTimeOffset"/> as text and cannot
/// translate comparisons or ordering on one, so any query that filters or sorts by a date
/// column blows up the first time it runs — and if it runs inside a background service with
/// a try/catch, it does so silently, forever.
///
/// Every query in the application that touches a date in SQL gets a test here.
/// </summary>
public class QueryTranslationTests : IDisposable
{
    private readonly SqliteFixture fixture = new();

    public void Dispose() => fixture.Dispose();

    [Fact]
    public async Task The_retention_sweep_query_translates()
    {
        using SplitDbContext db = fixture.NewContext();
        DateTimeOffset cutoff = DateTimeOffset.UtcNow.AddDays(-90);

        // Exactly the query AnonymousGroupCleanupService runs. It threw for weeks because
        // LastActivityAt used to be stored as a DateTimeOffset.
        List<Guid> expired = await db.Groups
            .Where(group => group.LastActivityAt < cutoff
                && !db.GroupMemberships.Any(membership => membership.GroupId == group.Id))
            .Select(group => group.Id)
            .ToListAsync();

        Assert.Empty(expired);
    }

    [Fact]
    public async Task The_my_groups_query_translates_and_orders_by_most_recent()
    {
        var user = new User
        {
            GoogleSubject = "sub-1",
            Email = "a@example.com",
            DisplayName = "Tester",
        };

        var older = NewGroup("older");
        var newer = NewGroup("newer");

        using (SplitDbContext seed = fixture.NewContext())
        {
            seed.Users.Add(user);
            seed.Groups.AddRange(older, newer);

            seed.GroupMemberships.Add(new GroupMembership
            {
                UserId = user.Id,
                GroupId = older.Id,
                LastSeenAt = DateTimeOffset.UtcNow.AddDays(-10),
            });

            seed.GroupMemberships.Add(new GroupMembership
            {
                UserId = user.Id,
                GroupId = newer.Id,
                LastSeenAt = DateTimeOffset.UtcNow,
            });

            await seed.SaveChangesAsync();
        }

        using SplitDbContext db = fixture.NewContext();

        // The real query the endpoint runs, not a copy of it. Sorting this by LastSeenAt in
        // SQL made /api/groups/mine throw for every signed-in user, because SQLite cannot
        // ORDER BY a DateTimeOffset.
        List<GroupSummaryView> history = await GroupEndpoints.LoadHistory(db, user.Id);

        Assert.Equal(new[] { "newer", "older" }, history.Select(r => r.Name).ToArray());
    }

    [Fact]
    public async Task History_reports_the_group_size_and_total_spend()
    {
        var user = new User { GoogleSubject = "sub-2", Email = "b@example.com", DisplayName = "B" };
        ExpenseGroup group = NewGroup("trip");
        var aisyah = new Participant { GroupId = group.Id, Name = "Aisyah", Position = 0 };
        var bala = new Participant { GroupId = group.Id, Name = "Bala", Position = 1 };

        using (SplitDbContext seed = fixture.NewContext())
        {
            group.Participants.AddRange([aisyah, bala]);
            group.Expenses.Add(new Expense
            {
                GroupId = group.Id,
                Description = "Dinner",
                AmountCents = 2050,
                PaidByParticipantId = aisyah.Id,
            });

            seed.Users.Add(user);
            seed.Groups.Add(group);
            seed.GroupMemberships.Add(new GroupMembership
            {
                UserId = user.Id,
                GroupId = group.Id,
                IsCreator = true,
            });

            await seed.SaveChangesAsync();
        }

        using SplitDbContext db = fixture.NewContext();
        GroupSummaryView row = Assert.Single(await GroupEndpoints.LoadHistory(db, user.Id));

        Assert.Equal("trip", row.Name);
        Assert.Equal(2, row.People);
        Assert.Equal(20.50m, row.TotalSpend);
        Assert.True(row.IsCreator);
    }

    [Fact]
    public async Task A_group_with_no_expenses_reports_zero_rather_than_failing()
    {
        // The sum is over an empty set, which SQL returns as NULL rather than 0.
        var user = new User { GoogleSubject = "sub-3", Email = "c@example.com", DisplayName = "C" };
        ExpenseGroup group = NewGroup("empty");

        using (SplitDbContext seed = fixture.NewContext())
        {
            seed.Users.Add(user);
            seed.Groups.Add(group);
            seed.GroupMemberships.Add(new GroupMembership { UserId = user.Id, GroupId = group.Id });
            await seed.SaveChangesAsync();
        }

        using SplitDbContext db = fixture.NewContext();
        GroupSummaryView row = Assert.Single(await GroupEndpoints.LoadHistory(db, user.Id));

        Assert.Equal(0m, row.TotalSpend);
    }

    [Fact]
    public async Task Deleting_a_group_takes_its_participants_expenses_and_shares_with_it()
    {
        // The retention sweep uses ExecuteDelete, which issues raw SQL and never loads the
        // children to cascade them in memory. So the cascade has to come from the database,
        // which means SQLite must have foreign keys switched on. If it ever stops being on
        // by default, this is the test that notices instead of the orphan rows.
        ExpenseGroup group = NewGroup("doomed");
        var participant = new Participant { GroupId = group.Id, Name = "Aisyah", Position = 0 };
        var expense = new Expense
        {
            GroupId = group.Id,
            Description = "Dinner",
            AmountCents = 2000,
            PaidByParticipantId = participant.Id,
            Shares = [new ExpenseShare { ParticipantId = participant.Id, AmountCents = 2000 }],
        };

        using (SplitDbContext seed = fixture.NewContext())
        {
            group.Participants.Add(participant);
            group.Expenses.Add(expense);
            seed.Groups.Add(group);
            await seed.SaveChangesAsync();
        }

        using (SplitDbContext act = fixture.NewContext())
        {
            await act.Groups.Where(g => g.Id == group.Id).ExecuteDeleteAsync();
        }

        using SplitDbContext check = fixture.NewContext();
        Assert.Empty(check.Groups);
        Assert.Empty(check.Participants);
        Assert.Empty(check.Expenses);
        Assert.Empty(check.ExpenseShares);
    }

    private static ExpenseGroup NewGroup(string name) => new()
    {
        Slug = $"slug-{name}",
        Name = name,
        Currency = "MYR",
    };
}
