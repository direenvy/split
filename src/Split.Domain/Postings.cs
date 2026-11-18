namespace Split.Domain;

/// <summary>
/// One participant's share of one amount: "Aisyah's slice of this dinner is 23.34".
/// </summary>
public readonly record struct Posting(Guid ParticipantId, decimal Amount);

/// <summary>
/// What the group owes one participant, net of everything they have paid and everything
/// they have consumed.
///
/// Positive: the group owes them money (they paid more than their share).
/// Negative: they owe the group money.
/// Zero: square.
/// </summary>
public readonly record struct Balance(Guid ParticipantId, decimal Net);

/// <summary>
/// A single payment that moves the group towards settled:
/// <see cref="FromParticipantId"/> hands <see cref="Amount"/> to <see cref="ToParticipantId"/>.
/// <see cref="Amount"/> is always strictly positive.
/// </summary>
public readonly record struct Transfer(Guid FromParticipantId, Guid ToParticipantId, decimal Amount);
