namespace Split.Domain;

/// <summary>
/// Divides an amount between people so that the parts add up to <em>exactly</em> the amount.
///
/// The awkward case this exists for: 10.00 split three ways. 10.00 / 3 = 3.3333...
/// Rounding each share to 3.33 gives 9.99 and loses a cent; rounding up gives 10.02 and
/// invents two. Neither is acceptable, because the settlement algorithm requires that every
/// balance in the group sums to zero — a stray cent makes the group permanently unsettleable.
///
/// So: hand everyone the floor of the division, then give the leftover cents out one each,
/// to the people at the front of the list. 10.00 three ways becomes 3.34 / 3.33 / 3.33.
/// The person who gets the extra cent is decided by their position in <c>among</c>, which the
/// caller controls, so the result is deterministic and reproducible rather than arbitrary.
/// </summary>
public static class MoneySplitter
{
    /// <summary>
    /// Splits <paramref name="amount"/> into <paramref name="ways"/> parts summing exactly to it.
    /// Negative amounts split symmetrically (the leftover cents go to the front in the same way).
    /// </summary>
    public static IReadOnlyList<decimal> SplitEvenly(decimal amount, int ways)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(ways, 1);

        long totalCents = Money.ToCents(amount);
        long sign = totalCents < 0 ? -1 : 1;
        long magnitude = Math.Abs(totalCents);

        long baseShare = magnitude / ways;
        int leftover = (int)(magnitude % ways);   // always in [0, ways), so the cast is safe

        var shares = new decimal[ways];
        for (int i = 0; i < ways; i++)
        {
            long cents = baseShare + (i < leftover ? 1 : 0);
            shares[i] = Money.FromCents(cents * sign);
        }

        return shares;
    }

    /// <summary>
    /// Splits <paramref name="amount"/> evenly between the given participants and pairs each
    /// share with its owner. This is what the API calls when an expense is logged; the resulting
    /// shares are stored, so a group's history never changes shape if this code later does.
    /// </summary>
    /// <exception cref="ArgumentException">
    /// <paramref name="among"/> is empty or names the same participant twice.
    /// </exception>
    public static IReadOnlyList<Posting> AllocateEvenly(decimal amount, IReadOnlyList<Guid> among)
    {
        ArgumentNullException.ThrowIfNull(among);

        if (among.Count == 0)
        {
            throw new ArgumentException("An expense must be split between at least one person.", nameof(among));
        }

        if (among.Distinct().Count() != among.Count)
        {
            throw new ArgumentException("The same participant appears twice in the split.", nameof(among));
        }

        IReadOnlyList<decimal> shares = SplitEvenly(amount, among.Count);

        var postings = new Posting[among.Count];
        for (int i = 0; i < among.Count; i++)
        {
            postings[i] = new Posting(among[i], shares[i]);
        }

        return postings;
    }
}
