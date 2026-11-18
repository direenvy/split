namespace Split.Api;

// The shapes the API speaks. These are deliberately separate from the database entities:
// the client never sees cents, EF navigation properties or internal ids it has no use for,
// and changing a column does not silently change the public API.

public record CreateGroupRequest(string Name, string? Currency, string[] Members);

public record AddMemberRequest(string Name);

public record AddExpenseRequest(
    string Description,
    decimal Amount,
    Guid PaidBy,
    Guid[] SplitAmong);

public record MemberView(Guid Id, string Name);

public record ShareView(Guid ParticipantId, decimal Amount);

public record ExpenseView(
    Guid Id,
    string Description,
    decimal Amount,
    Guid PaidBy,
    DateTimeOffset CreatedAt,
    ShareView[] Shares);

public record BalanceView(Guid ParticipantId, decimal Net);

/// <summary>One line in a signed-in user's history list — enough to render a card, no more.</summary>
public record GroupSummaryView(
    string Slug,
    string Name,
    string Currency,
    bool IsCreator,
    DateTimeOffset LastSeenAt,
    int People,
    decimal TotalSpend);

/// <summary>
/// A settlement worked out from balances alone, with no group behind it. Used by the demo on
/// the landing page, so that what visitors see is the real algorithm rather than a mock-up or
/// a second implementation written in TypeScript.
/// </summary>
public record SettlementPreviewRequest(PreviewBalance[] Balances);

public record PreviewBalance(string Name, decimal Net);

public record PreviewPayment(string From, string To, decimal Amount);

public record SettlementPreviewResponse(
    PreviewPayment[] Payments,
    int NaivePaymentCount);

public record PaymentView(Guid From, Guid To, decimal Amount);

/// <summary>
/// Everything a group's screens need, in one response. Balances and the settlement plan are
/// derived on read rather than stored — they are a pure function of the expenses, and keeping
/// a second copy in the database would only create something to go stale.
/// </summary>
public record GroupView(
    string Slug,
    string Name,
    string Currency,
    DateTimeOffset CreatedAt,
    MemberView[] Members,
    ExpenseView[] Expenses,
    decimal TotalSpend,
    BalanceView[] Balances,
    PaymentView[] Settlement);
