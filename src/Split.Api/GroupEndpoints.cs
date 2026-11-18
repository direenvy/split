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

    /*
      Length and size limits.

      These are not paranoia about malicious input so much as about the database quietly
      disagreeing with the code. EF declares these columns with HasMaxLength, but SQLite does
      not enforce a column's declared length at all — it will happily store a 5000-character
      description in a column declared as 200 and give no indication that anything is odd.
      So the limits have to be checked here or they do not exist. They match the model.
    */
    private const int MaxGroupNameLength = 120;
    private const int MaxPersonNameLength = 80;
    private const int MaxDescriptionLength = 200;

    /// <summary>
    /// Past this the UI stops being usable long before the maths does, and settlement drops
    /// off its exact path at 18 people anyway (see DebtSimplifier.ExactPartitionLimit).
    /// </summary>
    private const int MaxMembers = 50;

    public static void MapGroupEndpoints(this IEndpointRouteBuilder app)
    {
        RouteGroupBuilder api = app.MapGroup("/api/groups");

        api.MapGet("/mine", GetMyGroups);
        api.MapPost("/", CreateGroup);
        api.MapGet("/{slug}", GetGroup);
        api.MapPost("/{slug}/members", AddMember);
        api.MapPost("/{slug}/expenses", AddExpense);
        api.MapDelete("/{slug}/expenses/{expenseId:guid}", DeleteExpense);
        api.MapDelete("/{slug}", DeleteGroup);
        api.MapDelete("/{slug}/membership", ForgetGroup);
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

        if (name.Length > MaxGroupNameLength)
        {
            return Problem($"A group name can be at most {MaxGroupNameLength} characters.");
        }

        string[] members = [.. (request.Members ?? []).Select(m => (m ?? string.Empty).Trim()).Where(m => m.Length > 0)];
        if (members.Length < MinimumMembers)
        {
            return Problem($"A group needs at least {MinimumMembers} people.");
        }

        if (members.Length > MaxMembers)
        {
            return Problem($"A group can have at most {MaxMembers} people.");
        }

        if (members.Any(m => m.Length > MaxPersonNameLength))
        {
            return Problem($"A name can be at most {MaxPersonNameLength} characters.");
        }

        if (members.Distinct(StringComparer.OrdinalIgnoreCase).Count() != members.Length)
        {
            return Problem("Two people in the group have the same name.");
        }

        string currency = (request.Currency ?? "MYR").Trim().ToUpperInvariant();

        // Three *letters*, not merely three characters: "123" and "$%^" were both accepted
        // before, and either would end up in front of every amount on the screen.
        if (currency.Length != 3 || !currency.All(char.IsAsciiLetter))
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

        if (name.Length > MaxPersonNameLength)
        {
            return Problem($"A name can be at most {MaxPersonNameLength} characters.");
        }

        if (group.Participants.Count >= MaxMembers)
        {
            return Problem($"A group can have at most {MaxMembers} people.");
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

        if (description.Length > MaxDescriptionLength)
        {
            return Problem($"A description can be at most {MaxDescriptionLength} characters.");
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

    /// <summary>
    /// Deletes a group outright, for everybody, along with its people and expenses.
    ///
    /// <para>
    /// Only whoever created it may do this, and that is the one place in the application
    /// where <see cref="GroupMembership.IsCreator"/> grants anything. Everywhere else a
    /// membership is history and nothing more. The distinction matters: a group is reachable
    /// by its link, so anyone who has ever opened one has a membership row for it, and
    /// without this check opening a link someone shared with you would be enough to destroy
    /// their data.
    /// </para>
    ///
    /// <para>
    /// This is not a soft delete and there is no undo. The alternative — keeping deleted
    /// groups around so they could be restored — means the link keeps working for everybody
    /// else who has it, which is the opposite of what someone deleting a group wants.
    /// </para>
    /// </summary>
    private static async Task<IResult> DeleteGroup(string slug, SplitDbContext db, HttpContext context)
    {
        User? user = await context.GetCurrentUser(db);
        if (user is null)
        {
            return Results.Unauthorized();
        }

        ExpenseGroup? group = await db.Groups.FirstOrDefaultAsync(g => g.Slug == slug);
        if (group is null)
        {
            return NotFound(slug);
        }

        bool isCreator = await db.GroupMemberships
            .AnyAsync(m => m.UserId == user.Id && m.GroupId == group.Id && m.IsCreator);

        if (!isCreator)
        {
            // Deliberately 403 and not 404: the caller can plainly see the group, so
            // pretending it does not exist would only be confusing.
            return Results.Json(
                new { error = "Only whoever created this group can delete it. You can remove it from your list instead." },
                statusCode: StatusCodes.Status403Forbidden);
        }

        // Participants, expenses and shares go with it through the cascade declared on each
        // foreign key. The history rows do not cascade, since nothing links a membership to a
        // group in the model, so they are cleared explicitly — otherwise every member would be
        // left with a row pointing at a group that no longer exists.
        await db.GroupMemberships.Where(m => m.GroupId == group.Id).ExecuteDeleteAsync();
        await db.Groups.Where(g => g.Id == group.Id).ExecuteDeleteAsync();

        return Results.Ok(new { deleted = true });
    }

    /// <summary>
    /// Drops a group out of the caller's own history, leaving the group itself untouched.
    /// This is what you get for a group somebody else created and shared with you.
    /// </summary>
    private static async Task<IResult> ForgetGroup(string slug, SplitDbContext db, HttpContext context)
    {
        User? user = await context.GetCurrentUser(db);
        if (user is null)
        {
            return Results.Unauthorized();
        }

        ExpenseGroup? group = await db.Groups.FirstOrDefaultAsync(g => g.Slug == slug);
        if (group is null)
        {
            return NotFound(slug);
        }

        await db.GroupMemberships
            .Where(m => m.UserId == user.Id && m.GroupId == group.Id)
            .ExecuteDeleteAsync();

        return Results.Ok(new { removed = true });
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
