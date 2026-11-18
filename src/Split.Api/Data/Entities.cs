using Split.Domain;

namespace Split.Api.Data;

/// <summary>
/// A set of people sharing expenses — a trip, a flat, a weekly dinner.
///
/// Reachable by <see cref="Slug"/> alone: there are no accounts in this version, so the URL
/// is the key. See the README for what that does and does not protect.
/// </summary>
public class ExpenseGroup
{
    public Guid Id { get; set; } = Guid.CreateVersion7();

    /// <summary>The shareable part of the URL, e.g. <c>bright-tapir-4821</c>.</summary>
    public required string Slug { get; set; }

    public required string Name { get; set; }

    /// <summary>ISO 4217 code, used for display only — no conversion happens anywhere.</summary>
    public required string Currency { get; set; }

    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;

    public List<Participant> Participants { get; set; } = [];

    public List<Expense> Expenses { get; set; } = [];
}

/// <summary>One person in a group.</summary>
public class Participant
{
    public Guid Id { get; set; } = Guid.CreateVersion7();

    public Guid GroupId { get; set; }

    public required string Name { get; set; }

    /// <summary>Position in the group, used to keep the roster in a stable, obvious order.</summary>
    public int Position { get; set; }
}

/// <summary>
/// Something somebody paid for, and the shares of it that other people owe.
///
/// The shares are worked out once, when the expense is logged, and stored. They are not
/// recalculated on read. That matters: if the splitting rule ever changes, old expenses keep
/// the numbers everybody already agreed on, and a group's history cannot silently shift under
/// its members.
/// </summary>
public class Expense
{
    public Guid Id { get; set; } = Guid.CreateVersion7();

    public Guid GroupId { get; set; }

    public required string Description { get; set; }

    /// <summary>
    /// The total, in whole cents. Stored as an integer rather than a decimal because SQLite has
    /// no decimal type of its own — EF would keep it as text, and any arithmetic that happened
    /// in SQL rather than in C# would go through a float. Cents sidestep all of that.
    /// </summary>
    public long AmountCents { get; set; }

    public decimal Amount => Money.FromCents(AmountCents);

    /// <summary>Who actually handed over the money.</summary>
    public Guid PaidByParticipantId { get; set; }

    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;

    public List<ExpenseShare> Shares { get; set; } = [];
}

/// <summary>One participant's agreed slice of one expense.</summary>
public class ExpenseShare
{
    public Guid Id { get; set; } = Guid.CreateVersion7();

    public Guid ExpenseId { get; set; }

    public Guid ParticipantId { get; set; }

    public long AmountCents { get; set; }

    public decimal Amount => Money.FromCents(AmountCents);
}
