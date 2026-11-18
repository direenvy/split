namespace Split.Domain.Tests;

/// <summary>
/// Shared checking for settlement tests.
///
/// <para>
/// <see cref="AssertClears"/> is the property that actually matters: whatever payments the
/// algorithm produced, applying them must leave everybody on exactly zero. A test that only
/// counted payments could pass while quietly moving the wrong money.
/// </para>
///
/// <para>
/// <see cref="MinimumPayments"/> is a second, independent implementation of the hard part,
/// written the slow and obvious way — enumerate every subset, recurse, memoise. It is far too
/// slow for production (O(3^n)) but it is easy to believe, and comparing it against the real
/// algorithm's output turns "I think this is minimal" into something the test suite checks.
/// Two implementations that disagree mean at least one is wrong; agreeing on every case we can
/// enumerate is about as good as it gets without a proof assistant.
/// </para>
/// </summary>
public static class Settlement
{
    /// <summary>Builds a balance list from name/amount pairs.</summary>
    public static Balance[] Balances(params (Guid Who, decimal Net)[] entries) =>
        [.. entries.Select(e => new Balance(e.Who, e.Net))];

    /// <summary>
    /// Asserts that the payments are well-formed and that applying them settles every balance
    /// exactly — no stray cent left anywhere.
    /// </summary>
    public static void AssertClears(IReadOnlyCollection<Balance> balances, IReadOnlyList<Transfer> payments)
    {
        var remaining = balances.ToDictionary(b => b.ParticipantId, b => Money.ToCents(b.Net));

        foreach (Transfer payment in payments)
        {
            Assert.True(payment.Amount > 0m, $"Payment amount must be positive but was {payment.Amount}.");
            Assert.True(
                decimal.Round(payment.Amount, 2) == payment.Amount,
                $"Payment amount {payment.Amount} has sub-cent precision.");
            Assert.NotEqual(payment.FromParticipantId, payment.ToParticipantId);
            Assert.True(remaining.ContainsKey(payment.FromParticipantId), "Payment from someone outside the group.");
            Assert.True(remaining.ContainsKey(payment.ToParticipantId), "Payment to someone outside the group.");

            long cents = Money.ToCents(payment.Amount);
            remaining[payment.FromParticipantId] += cents;  // the payer's debt shrinks towards zero
            remaining[payment.ToParticipantId] -= cents;    // the payee is owed less
        }

        foreach ((Guid who, long left) in remaining)
        {
            Assert.True(left == 0, $"Participant {who} is left {Money.FromCents(left)} out of square.");
        }
    }

    /// <summary>
    /// The slow, obviously-correct answer for how few payments are possible, for comparison
    /// against the real algorithm. Only usable for small groups.
    /// </summary>
    public static int MinimumPayments(IReadOnlyCollection<Balance> balances)
    {
        long[] net = [.. balances.Select(b => Money.ToCents(b.Net)).Where(c => c != 0)];
        int n = net.Length;
        if (n == 0)
        {
            return 0;
        }

        Assert.True(n <= 13, "The brute-force oracle is only usable for small groups.");

        var memo = new Dictionary<int, int>();
        int full = (1 << n) - 1;
        return n - MaxZeroSumParts(full);

        long SumOf(int mask)
        {
            long total = 0;
            for (int i = 0; i < n; i++)
            {
                if ((mask & (1 << i)) != 0)
                {
                    total += net[i];
                }
            }

            return total;
        }

        // Largest number of zero-sum subsets this (zero-sum) mask can be partitioned into.
        int MaxZeroSumParts(int mask)
        {
            if (mask == 0)
            {
                return 0;
            }

            if (memo.TryGetValue(mask, out int cached))
            {
                return cached;
            }

            // Fix the lowest-numbered member and try every zero-sum subset that contains them.
            // Anchoring on one member is what stops us counting the same partition many times.
            int anchor = 1 << System.Numerics.BitOperations.TrailingZeroCount(mask);

            int best = 0;
            for (int subset = mask; subset > 0; subset = (subset - 1) & mask)
            {
                if ((subset & anchor) == 0 || SumOf(subset) != 0)
                {
                    continue;
                }

                best = Math.Max(best, MaxZeroSumParts(mask ^ subset) + 1);
            }

            memo[mask] = best;
            return best;
        }
    }
}
