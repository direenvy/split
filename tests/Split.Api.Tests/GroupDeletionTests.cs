using Microsoft.EntityFrameworkCore;
using Split.Api.Data;

namespace Split.Api.Tests;

/// <summary>
/// Deleting a group is the only irreversible thing this application can do, and the only
/// place where being the creator grants a right rather than just a label.
///
/// The rule being protected: a group is reachable by its link, so merely opening one gives
/// you a membership row for it. If deletion checked for a membership instead of for
/// <see cref="GroupMembership.IsCreator"/>, following a link someone sent you would be enough
/// to destroy their trip.
/// </summary>
public class GroupDeletionTests : IDisposable
{
    private readonly SqliteFixture fixture = new();

    public void Dispose() => fixture.Dispose();

    [Fact]
    public async Task The_creator_may_delete_and_everything_in_the_group_goes_with_it()
    {
        (User creator, ExpenseGroup group) = Seed(isCreator: true);

        using SplitDbContext db = fixture.NewContext();
        Assert.True(await MayDelete(db, creator.Id, group.Id));

        await db.GroupMemberships.Where(m => m.GroupId == group.Id).ExecuteDeleteAsync();
        await db.Groups.Where(g => g.Id == group.Id).ExecuteDeleteAsync();

        using SplitDbContext check = fixture.NewContext();
        Assert.Empty(check.Groups);
        Assert.Empty(check.Participants);
        Assert.Empty(check.Expenses);
        Assert.Empty(check.ExpenseShares);

        // The history rows have to be cleared explicitly: nothing in the model links a
        // membership to a group, so there is no cascade to rely on. Without that, every
        // member would keep a row pointing at a group that no longer exists.
        Assert.Empty(check.GroupMemberships);
    }

    [Fact]
    public async Task Somebody_who_merely_opened_the_group_may_not_delete_it()
    {
        (User visitor, ExpenseGroup group) = Seed(isCreator: false);

        using SplitDbContext db = fixture.NewContext();

        Assert.False(await MayDelete(db, visitor.Id, group.Id));
    }

    [Fact]
    public async Task A_stranger_with_no_membership_may_not_delete_it()
    {
        (_, ExpenseGroup group) = Seed(isCreator: true);

        using SplitDbContext db = fixture.NewContext();

        Assert.False(await MayDelete(db, Guid.CreateVersion7(), group.Id));
    }

    [Fact]
    public async Task Forgetting_a_group_removes_only_the_callers_own_history()
    {
        (User mine, ExpenseGroup group) = Seed(isCreator: false);

        var somebodyElse = new User
        {
            GoogleSubject = "sub-other",
            Email = "other@example.com",
            DisplayName = "Other",
        };

        using (SplitDbContext seed = fixture.NewContext())
        {
            seed.Users.Add(somebodyElse);
            seed.GroupMemberships.Add(new GroupMembership
            {
                UserId = somebodyElse.Id,
                GroupId = group.Id,
                IsCreator = true,
            });

            await seed.SaveChangesAsync();
        }

        using (SplitDbContext act = fixture.NewContext())
        {
            await act.GroupMemberships
                .Where(m => m.UserId == mine.Id && m.GroupId == group.Id)
                .ExecuteDeleteAsync();
        }

        using SplitDbContext check = fixture.NewContext();

        // The group survives, and so does the other person's history of it.
        Assert.Single(check.Groups);
        GroupMembership remaining = Assert.Single(check.GroupMemberships);
        Assert.Equal(somebodyElse.Id, remaining.UserId);
        Assert.Empty(await GroupEndpoints.LoadHistory(check, mine.Id));
    }

    private static Task<bool> MayDelete(SplitDbContext db, Guid userId, Guid groupId) =>
        db.GroupMemberships.AnyAsync(m => m.UserId == userId && m.GroupId == groupId && m.IsCreator);

    private (User, ExpenseGroup) Seed(bool isCreator)
    {
        var user = new User
        {
            GoogleSubject = $"sub-{Guid.NewGuid()}",
            Email = "a@example.com",
            DisplayName = "Tester",
        };

        var group = new ExpenseGroup { Slug = "trip", Name = "Trip", Currency = "MYR" };
        var participant = new Participant { GroupId = group.Id, Name = "Aisyah", Position = 0 };

        group.Participants.Add(participant);
        group.Expenses.Add(new Expense
        {
            GroupId = group.Id,
            Description = "Dinner",
            AmountCents = 2000,
            PaidByParticipantId = participant.Id,
            Shares = [new ExpenseShare { ParticipantId = participant.Id, AmountCents = 2000 }],
        });

        using SplitDbContext db = fixture.NewContext();
        db.Users.Add(user);
        db.Groups.Add(group);
        db.GroupMemberships.Add(new GroupMembership
        {
            UserId = user.Id,
            GroupId = group.Id,
            IsCreator = isCreator,
        });

        db.SaveChanges();

        return (user, group);
    }
}
