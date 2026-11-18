namespace Split.Domain;

/// <summary>
/// Turns a group's ledger into one net figure per person.
///
/// The ledger has exactly two kinds of line:
///   paid — money a participant actually handed over (the full amount of an expense they covered)
///   owed — a participant's allocated share of an expense (produced by <see cref="MoneySplitter"/>)
///
/// Net = paid − owed. Because every expense contributes the same total to both sides, the nets
/// of a whole group always sum to zero. That property is what makes settlement possible, so it
/// is checked rather than assumed.
/// </summary>
public static class BalanceCalculator
{
    /// <summary>
    /// Computes one <see cref="Balance"/> per participant, in the order the participants are given.
    /// Participants with no activity are included with a net of zero, so the caller can render a
    /// complete roster without reconciling two lists.
    /// </summary>
    /// <exception cref="ArgumentException">
    /// A posting names someone who is not in <paramref name="participantIds"/>, or the nets do not
    /// sum to zero (which would mean the ledger itself is broken).
    /// </exception>
    public static IReadOnlyList<Balance> Compute(
        IReadOnlyList<Guid> participantIds,
        IEnumerable<Posting> paid,
        IEnumerable<Posting> owed)
    {
        ArgumentNullException.ThrowIfNull(participantIds);
        ArgumentNullException.ThrowIfNull(paid);
        ArgumentNullException.ThrowIfNull(owed);

        if (participantIds.Distinct().Count() != participantIds.Count)
        {
            throw new ArgumentException("The same participant appears twice.", nameof(participantIds));
        }

        var netCents = new Dictionary<Guid, long>(participantIds.Count);
        foreach (Guid id in participantIds)
        {
            netCents[id] = 0;
        }

        Apply(paid, +1, netCents, nameof(paid));
        Apply(owed, -1, netCents, nameof(owed));

        long total = 0;
        var balances = new Balance[participantIds.Count];
        for (int i = 0; i < participantIds.Count; i++)
        {
            long cents = netCents[participantIds[i]];
            total += cents;
            balances[i] = new Balance(participantIds[i], Money.FromCents(cents));
        }

        if (total != 0)
        {
            throw new ArgumentException(
                $"The ledger does not balance: the net positions sum to {Money.FromCents(total)} " +
                "rather than zero. Every expense must post the same total to 'paid' and to 'owed'.");
        }

        return balances;
    }

    private static void Apply(
        IEnumerable<Posting> postings,
        int sign,
        Dictionary<Guid, long> netCents,
        string parameterName)
    {
        foreach (Posting posting in postings)
        {
            if (!netCents.ContainsKey(posting.ParticipantId))
            {
                throw new ArgumentException(
                    $"Posting refers to participant {posting.ParticipantId}, who is not in the group.",
                    parameterName);
            }

            netCents[posting.ParticipantId] += sign * Money.ToCents(posting.Amount);
        }
    }
}
