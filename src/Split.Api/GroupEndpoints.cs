using Microsoft.EntityFrameworkCore;
using Split.Api.Data;
using Split.Domain;

namespace Split.Api;

/// <summary>
/// The whole API: four endpoints under <c>/api</c>.
///
/// There is no service layer between these handlers and the database, on purpose. The only
/// real logic in this application lives in Split.Domain, where it is unit-tested in isolation;
/// adding a layer here whose job was to forward calls would be ceremony, not structure.
/// </summary>
public static class GroupEndpoints
{
    /// <summary>A group has to have at least this many people before splitting means anything.</summary>
    private const int MinimumMembers = 2;

    public static void MapGroupEndpoints(this IEndpointRouteBuilder app)
    {
        RouteGroupBuilder api = app.MapGroup("/api/groups");

        api.MapGet("/mine", GetMyGroups);
        api.MapPost("/", CreateGroup);
        api.MapGet("/{slug}", GetGroup);
        api.MapPost("/{slug}/members", AddMember);
        api.MapPost("/{slug}/expenses", AddExpense);
        api.MapDelete("/{slug}/expenses/{expenseId:guid}", DeleteExpense);
    }

    // -----------------------------------------------------------------------------------

    private static async Task<IResult> CreateGroup(
        CreateGroupRequest request,
        SplitDbContext db,
        HttpContext context)
    {
        string name = (request.Name ?? string.Empty).Trim();
        if (name.Length == 0)
        {
            return Problem("A group needs a name.");
        }

        string[] members = [.. (request.Members ?? []).Select(m => (m ?? string.Empty).Trim()).Where(m => m.Length > 0)];
        if (members.Length < MinimumMembers)
        {
            return Problem($"A group needs at least {MinimumMembers} people.");
        }

        if (members.Distinct(StringComparer.OrdinalIgnoreCase).Count() != members.Length)
        {
            return Problem("Two people in the group have the same name.");
        }

        string currency = (request.Currency ?? "MYR").Trim().ToUpperInvariant();
        if (currency.Length != 3)
        {
            return Problem("Currency must be a three-letter code, for example MYR.");
        }

        var group = new ExpenseGroup
        {
            Slug = await NextFreeSlug(db),
            Name = name,
            Currency = currency,
            Participants = [.. members.Select((m, i) => new Participant { Name = m, Position = i })]
        };

        db.Groups.Add(group);
        await db.SaveChangesAsync();

        await RememberVisit(context, db, group, isCreator: true);

        return Results.Created($"/api/groups/{group.Slug}", ToView(group));
    }

    private static async Task<IResult> GetGroup(string slug, SplitDbContext db, HttpContext context)
    {
        ExpenseGroup? group = await LoadGroup(db, slug);
        if (group is null)
        {
            return NotFound(slug);
        }

        // Opening a group puts it in your history, if you are signed in. Note the order:
        // the group is loaded and returned regardless, because being signed in is not a
        // condition of reading a group here — the link is.
        await RememberVisit(context, db, group, isCreator: false);

        return Results.Ok(ToView(group));
    }

    /// <summary>
    /// The signed-in user's groups, most recently opened first.
    /// </summary>
    private static async Task<IResult> GetMyGroups(SplitDbContext db, HttpContext context)
    {
        User? user = await context.GetCurrentUser(db);
        if (user is null)
        {
            return Results.Unauthorized();
        }

        return Results.Ok(await LoadHistory(db, user.Id));
    }

    /// <summary>
    /// A user's groups, most recently opened first.
    ///
    /// Public so the tests can exercise the real query rather than a copy of it — this is the
    /// query that used to throw, and a test against a reimplementation would have kept passing
    /// while the endpoint kept failing.
    /// </summary>
    public static async Task<List<GroupSummaryView>> LoadHistory(SplitDbContext db, Guid userId)
    {
        var rows = await db.GroupMemberships
            .Where(m => m.UserId == userId)
            .Join(
                db.Groups,
                membership => membership.GroupId,
                group => group.Id,
                (membership, group) => new
                {
                    group.Slug,
                    group.Name,
                    group.Currency,
                    membership.IsCreator,
                    membership.LastSeenAt,
                    People = group.Participants.Count,
                    TotalCents = group.Expenses.Sum(e => (long?)e.AmountCents) ?? 0L,
                })
            .ToListAsync();

        // Sorted here rather than in SQL, and that is not a stylistic choice: SQLite cannot
        // ORDER BY a DateTimeOffset, so putting this in the query made /api/groups/mine throw
        // for every signed-in user. Doing it in memory is safe because the result set is one
        // person's own groups — tens of rows, not a table scan.
        return
        [
            .. rows
                .OrderByDescending(r => r.LastSeenAt)
                .ThenBy(r => r.Name, StringComparer.OrdinalIgnoreCase)
                .Select(r => new GroupSummaryView(
                    r.Slug,
                    r.Name,
                    r.Currency,
                    r.IsCreator,
                    r.LastSeenAt,
                    r.People,
                    Money.FromCents(r.TotalCents)))
        ];
    }

    /// <summary>
    /// Notes that this user has seen this group, so it shows up in their history.
    ///
    /// Does nothing at all for an anonymous caller, which is the common case — the app is
    /// fully usable without an account, and this is the only thing an account adds.
    /// </summary>
    private static async Task RememberVisit(
        HttpContext context,
        SplitDbContext db,
        ExpenseGroup group,
        bool isCreator)
    {
        group.LastActivityAt = DateTimeOffset.UtcNow;
        User? user = await context.GetCurrentUser(db);
        if (user is not null)
        {
            GroupMembership? existing = await db.GroupMemberships
                .FirstOrDefaultAsync(m => m.UserId == user.Id && m.GroupId == group.Id);

            if (existing is null)
            {
                db.GroupMemberships.Add(new GroupMembership
                {
                    UserId = user.Id,
                    GroupId = group.Id,
                    IsCreator = isCreator,
                });
            }
            else
            {
                existing.LastSeenAt = DateTimeOffset.UtcNow;

                // Creating a group you had merely opened before should still mark you creator;
                // opening one you created must never clear the flag.
                existing.IsCreator = existing.IsCreator || isCreator;
            }
        }

        await db.SaveChangesAsync();
    }

    private static async Task<IResult> AddMember(string slug, AddMemberRequest request, SplitDbContext db)
    {
        ExpenseGroup? group = await LoadGroup(db, slug);
        if (group is null)
        {
            return NotFound(slug);
        }

        string name = (request.Name ?? string.Empty).Trim();
        if (name.Length == 0)
        {
            return Problem("A person needs a name.");
        }

        if (group.Participants.Any(p => string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase)))
        {
            return Problem($"{name} is already in this group.");
        }

        var participant = new Participant
        {
            GroupId = group.Id,
            Name = name,
            Position = group.Participants.Count == 0 ? 0 : group.Participants.Max(p => p.Position) + 1
        };

        group.Participants.Add(participant);
        group.LastActivityAt = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync();

        return Results.Ok(ToView(group));
    }

    private static async Task<IResult> AddExpense(string slug, AddExpenseRequest request, SplitDbContext db)
    {
        ExpenseGroup? group = await LoadGroup(db, slug);
        if (group is null)
        {
            return NotFound(slug);
        }

        string description = (request.Description ?? string.Empty).Trim();
        if (description.Length == 0)
        {
            return Problem("An expense needs a description.");
        }

        if (request.Amount <= 0m)
        {
            return Problem("An expense has to be more than zero.");
        }

        if (request.Amount > Money.MaxAmount)
        {
            return Problem("That amount is implausibly large.");
        }

        if (decimal.Round(request.Amount, 2) != request.Amount)
        {
            return Problem("An amount can have at most two decimal places.");
        }

        var roster = group.Participants.ToDictionary(p => p.Id);

        if (!roster.ContainsKey(request.PaidBy))
        {
            return Problem("Whoever paid has to be a member of this group.");
        }

        Guid[] splitAmong = request.SplitAmong ?? [];
        if (splitAmong.Length == 0)
        {
            return Problem("Choose at least one person to split this between.");
        }

        if (splitAmong.Distinct().Count() != splitAmong.Length)
        {
            return Problem("Somebody is listed twice in the split.");
        }

        if (splitAmong.Any(id => !roster.ContainsKey(id)))
        {
            return Problem("The split includes somebody who is not in this group.");
        }

        // Allocate the shares against the roster's own order, not the order the client happened
        // to send. The first people in the group absorb the leftover cents, which makes the
        // split reproducible and stops the outcome depending on how the UI built its list.
        Guid[] inRosterOrder =
        [
            .. splitAmong.OrderBy(id => roster[id].Position)
        ];

        IReadOnlyList<Posting> shares = MoneySplitter.AllocateEvenly(request.Amount, inRosterOrder);

        var expense = new Expense
        {
            GroupId = group.Id,
            Description = description,
            AmountCents = Money.ToCents(request.Amount),
            PaidByParticipantId = request.PaidBy,
            Shares =
            [
                .. shares.Select(s => new ExpenseShare
                {
                    ParticipantId = s.ParticipantId,
                    AmountCents = Money.ToCents(s.Amount)
                })
            ]
        };

        group.Expenses.Add(expense);
        group.LastActivityAt = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync();

        return Results.Ok(ToView(group));
    }

    private static async Task<IResult> DeleteExpense(string slug, Guid expenseId, SplitDbContext db)
    {
        ExpenseGroup? group = await LoadGroup(db, slug);
        if (group is null)
        {
            return NotFound(slug);
        }

        Expense? expense = group.Expenses.FirstOrDefault(e => e.Id == expenseId);
        if (expense is null)
        {
            return Results.NotFound(new { error = "No such expense in this group." });
        }

        db.Expenses.Remove(expense);
        group.Expenses.Remove(expense);
        group.LastActivityAt = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync();

        return Results.Ok(ToView(group));
    }

    // -----------------------------------------------------------------------------------
    // Shared plumbing
    // -----------------------------------------------------------------------------------

    private static Task<ExpenseGroup?> LoadGroup(SplitDbContext db, string slug) =>
        db.Groups
          .Include(g => g.Participants)
          .Include(g => g.Expenses)
          .ThenInclude(e => e.Shares)
          .FirstOrDefaultAsync(g => g.Slug == slug);

    private static async Task<string> NextFreeSlug(SplitDbContext db)
    {
        // Collisions are rare rather than impossible, so try a few times before giving up and
        // letting the unique index be the final word.
        for (int attempt = 0; attempt < 8; attempt++)
        {
            string candidate = SlugGenerator.Next();
            if (!await db.Groups.AnyAsync(g => g.Slug == candidate))
            {
                return candidate;
            }
        }

        return $"{SlugGenerator.Next()}-{Guid.CreateVersion7():N}"[..40];
    }

    /// <summary>
    /// Projects a loaded group into the shape the client reads, working out the live balances
    /// and the settlement plan on the way.
    /// </summary>
    private static GroupView ToView(ExpenseGroup group)
    {
        Participant[] roster = [.. group.Participants.OrderBy(p => p.Position)];
        Guid[] rosterIds = [.. roster.Select(p => p.Id)];

        Expense[] expenses = [.. group.Expenses.OrderByDescending(e => e.CreatedAt).ThenByDescending(e => e.Id)];

        // The ledger has two sides: what each person handed over, and what each person's share
        // of the bills came to. The difference is their position.
        var paid = expenses.Select(e => new Posting(e.PaidByParticipantId, e.Amount));
        var owed = expenses.SelectMany(e => e.Shares).Select(s => new Posting(s.ParticipantId, s.Amount));

        IReadOnlyList<Balance> balances = BalanceCalculator.Compute(rosterIds, paid, owed);
        IReadOnlyList<Transfer> settlement = DebtSimplifier.Simplify([.. balances]);

        return new GroupView(
            group.Slug,
            group.Name,
            group.Currency,
            group.CreatedAt,
            [.. roster.Select(p => new MemberView(p.Id, p.Name))],
            [
                .. expenses.Select(e => new ExpenseView(
                    e.Id,
                    e.Description,
                    e.Amount,
                    e.PaidByParticipantId,
                    e.CreatedAt,
                    [.. e.Shares.Select(s => new ShareView(s.ParticipantId, s.Amount))]))
            ],
            expenses.Sum(e => e.Amount),
            [.. balances.Select(b => new BalanceView(b.ParticipantId, b.Net))],
            [.. settlement.Select(t => new PaymentView(t.FromParticipantId, t.ToParticipantId, t.Amount))]);
    }

    private static IResult Problem(string message) => Results.BadRequest(new { error = message });

    private static IResult NotFound(string slug) =>
        Results.NotFound(new { error = $"No group with the code '{slug}'." });
}
