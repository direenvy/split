using System.Numerics;

namespace Split.Domain;

/// <summary>
/// Given what everyone is owed or owes, works out the <em>fewest possible</em> payments that
/// leave the whole group square.
///
/// <para>
/// <b>The problem.</b> Four friends end a trip with net positions of +30, -10, -20 and 0.
/// The naive settlement is "everyone who owes pays everyone they owe", which can run to dozens
/// of payments. The obvious improvement is to repeatedly match the biggest debtor with the
/// biggest creditor; that always works and always finishes within n-1 payments. But it is not
/// always the minimum. If Bala owes exactly what Aisyah is owed, and Chong owes exactly what
/// Dinesh is owed, the answer is two payments, not three — because the group falls into two
/// independent pairs that each settle on their own.
/// </para>
///
/// <para>
/// That observation is the whole algorithm:
/// <c>minimum payments = (people with a non-zero balance) - (number of subsets the group can be
/// carved into, where each subset's balances sum to zero)</c>. Every zero-sum subset of size k
/// costs k-1 payments to clear internally, and no payment ever has to cross from one such subset
/// to another. So the more subsets we find, the fewer payments we make.
/// </para>
///
/// <para>
/// <b>Why this is not a one-liner.</b> Finding the maximum number of zero-sum subsets is
/// NP-hard; it contains the partition problem, so there is no polynomial trick waiting to be
/// found. What there is, is the fact that real groups are small: a dinner has four people, a
/// holiday has maybe ten. So this is exhaustive-but-smart up to
/// <see cref="ExactPartitionLimit"/> people, using a dynamic program over subsets that runs in
/// O(2^n * n) rather than the O(3^n) of the obvious submask enumeration. Past that limit it
/// falls back to greedy matching, which never exceeds n-1 payments and in practice is usually
/// optimal anyway. The fallback is a deliberate, documented trade, not a silent approximation.
/// </para>
///
/// <para>
/// <b>The dynamic program.</b> For each subset <c>mask</c> of the participants,
/// <c>best[mask]</c> is the largest number of disjoint zero-sum subsets that can be carved out
/// of it — elements left over need not be used. Then:
/// <code>
/// best[mask] = max over i in mask of best[mask without i]
///              ... plus one, if the balances in mask themselves sum to zero
/// </code>
/// The second line is the interesting one. Suppose <c>mask</c> sums to zero and we can already
/// find <c>m</c> zero-sum subsets inside <c>mask</c> with some element <c>i</c> removed.
/// Whatever remains of <c>mask</c> after taking those <c>m</c> subsets out must itself sum to
/// zero (zero minus a pile of zeroes is zero) and must be non-empty (it still contains
/// <c>i</c>) — so it is an <c>m+1</c>-th subset. And we cannot do better, because deleting a
/// single element from an optimal partition of <c>mask</c> into <c>k</c> subsets leaves the
/// other <c>k-1</c> of them intact, so <c>m</c> is at least <c>k-1</c>. The lower and upper
/// bounds meet, which makes the recurrence exact rather than merely plausible.
/// </para>
/// </summary>
public static class DebtSimplifier
{
    /// <summary>
    /// Groups up to this size get a provably minimal answer. The dynamic program allocates and
    /// walks 2^n subsets, so this is where the cost stops being free: at 18 people it is a few
    /// megabytes and a few milliseconds; at 30 it would be gigabytes and minutes. Larger groups
    /// are settled greedily instead — see the remarks on the class.
    /// </summary>
    public const int ExactPartitionLimit = 18;

    /// <summary>
    /// Returns the shortest list of payments that clears every balance in the group.
    /// Participants who are already square are left out of the result. The output is
    /// deterministic: the same balances always produce the same payments in the same order,
    /// largest first.
    /// </summary>
    /// <exception cref="ArgumentException">
    /// The balances do not sum to zero, or name the same participant twice. Either means the
    /// caller's ledger is broken, and settling it would invent or destroy money.
    /// </exception>
    public static IReadOnlyList<Transfer> Simplify(IReadOnlyCollection<Balance> balances)
    {
        ArgumentNullException.ThrowIfNull(balances);

        // Work in a canonical order, so the output never depends on how the caller happened to
        // order its rows. Participant id is arbitrary but stable, which is all we need.
        Balance[] ordered = [.. balances.OrderBy(b => b.ParticipantId)];

        if (ordered.Select(b => b.ParticipantId).Distinct().Count() != ordered.Length)
        {
            throw new ArgumentException("The same participant appears twice.", nameof(balances));
        }

        long totalCents = 0;
        foreach (Balance balance in ordered)
        {
            totalCents += Money.ToCents(balance.Net);
        }

        if (totalCents != 0)
        {
            throw new ArgumentException(
                $"Balances must sum to zero; these sum to {Money.FromCents(totalCents)}. " +
                "A group whose balances do not cancel cannot be settled by payments between its " +
                "own members, because the difference would have to come from outside the group.",
                nameof(balances));
        }

        // Everyone already square — including the empty group — needs no payments at all.
        Balance[] active = [.. ordered.Where(b => b.Net != 0m)];
        if (active.Length == 0)
        {
            return [];
        }

        long[] netCents = [.. active.Select(b => Money.ToCents(b.Net))];
        Guid[] ids = [.. active.Select(b => b.ParticipantId)];

        var payments = new List<Transfer>();
        foreach (int[] group in PartitionIntoZeroSumGroups(netCents))
        {
            SettleOneGroup(group, netCents, ids, payments);
        }

        // Largest payment first reads best in the UI. The id tiebreaks keep the order stable
        // when two payments happen to be the same size.
        return
        [
            .. payments
                .OrderByDescending(t => t.Amount)
                .ThenBy(t => t.FromParticipantId)
                .ThenBy(t => t.ToParticipantId)
        ];
    }

    /// <summary>
    /// Splits the participants into as many zero-sum subsets as possible. Each returned array
    /// holds indices into <paramref name="netCents"/>.
    /// </summary>
    private static List<int[]> PartitionIntoZeroSumGroups(long[] netCents)
    {
        int n = netCents.Length;

        // Too big for the exact method: treat the whole group as a single lump and let the
        // greedy matcher in SettleOneGroup clear it in at most n-1 payments.
        if (n > ExactPartitionLimit)
        {
            return [[.. Enumerable.Range(0, n)]];
        }

        int maskCount = 1 << n;
        var subsetSum = new long[maskCount];
        var best = new int[maskCount];          // most zero-sum subsets carvable out of this mask
        var dropped = new int[maskCount];       // which participant we set aside to reach `best`
        var closesAGroup = new bool[maskCount]; // did this mask contribute a subset of its own?

        for (int mask = 1; mask < maskCount; mask++)
        {
            // Clearing the lowest set bit with `mask & (mask - 1)` gives the subset one element
            // smaller, whose sum is already known — so each subset's sum costs one addition.
            int lowest = BitOperations.TrailingZeroCount(mask);
            subsetSum[mask] = subsetSum[mask & (mask - 1)] + netCents[lowest];

            int bestWithoutOne = -1;
            int droppedChoice = -1;
            for (int i = 0; i < n; i++)
            {
                if ((mask & (1 << i)) == 0)
                {
                    continue;
                }

                int candidate = best[mask ^ (1 << i)];
                if (candidate > bestWithoutOne)
                {
                    bestWithoutOne = candidate;
                    droppedChoice = i;
                }
            }

            dropped[mask] = droppedChoice;

            if (subsetSum[mask] == 0)
            {
                best[mask] = bestWithoutOne + 1;
                closesAGroup[mask] = true;
            }
            else
            {
                best[mask] = bestWithoutOne;
            }
        }

        // Rebuild the actual subsets by retracing the choices. Walking down from the full set
        // gives the chain of masks the dynamic program came through; replaying that chain from
        // the smallest mask upwards, every mask that closed a group claims whatever members are
        // not yet spoken for.
        int fullMask = maskCount - 1;
        var chain = new List<int>();
        for (int mask = fullMask; mask != 0; mask ^= 1 << dropped[mask])
        {
            chain.Add(mask);
        }

        var groups = new List<int[]>(best[fullMask]);
        int claimed = 0;
        for (int step = chain.Count - 1; step >= 0; step--)
        {
            int mask = chain[step];
            if (!closesAGroup[mask])
            {
                continue;
            }

            int group = mask & ~claimed;
            if (group == 0)
            {
                continue;
            }

            claimed |= group;
            groups.Add([.. IndicesIn(group, n)]);
        }

        return groups;
    }

    private static IEnumerable<int> IndicesIn(int mask, int n)
    {
        for (int i = 0; i < n; i++)
        {
            if ((mask & (1 << i)) != 0)
            {
                yield return i;
            }
        }
    }

    /// <summary>
    /// Clears one zero-sum subset by repeatedly pointing the largest remaining debtor at the
    /// largest remaining creditor. Each payment zeroes at least one of the two, so a subset of
    /// k people costs at most k-1 payments — which, summed over a maximal partition, is the
    /// minimum established in the remarks on the class.
    /// </summary>
    private static void SettleOneGroup(int[] group, long[] netCents, Guid[] ids, List<Transfer> output)
    {
        // Sorting by size keeps the payment list front-loaded with the ones that matter, and the
        // id tiebreak means two equal balances always resolve the same way.
        Side[] debtors =
        [
            .. group.Where(i => netCents[i] < 0)
                    .OrderByDescending(i => -netCents[i])
                    .ThenBy(i => ids[i])
                    .Select(i => new Side(i, -netCents[i]))
        ];

        Side[] creditors =
        [
            .. group.Where(i => netCents[i] > 0)
                    .OrderByDescending(i => netCents[i])
                    .ThenBy(i => ids[i])
                    .Select(i => new Side(i, netCents[i]))
        ];

        int d = 0;
        int c = 0;
        while (d < debtors.Length && c < creditors.Length)
        {
            long payment = Math.Min(debtors[d].Outstanding, creditors[c].Outstanding);

            output.Add(new Transfer(
                ids[debtors[d].Index],
                ids[creditors[c].Index],
                Money.FromCents(payment)));

            debtors[d].Outstanding -= payment;
            creditors[c].Outstanding -= payment;

            if (debtors[d].Outstanding == 0)
            {
                d++;
            }

            if (creditors[c].Outstanding == 0)
            {
                c++;
            }
        }
    }

    /// <summary>One side of the greedy match, with the amount it still has to move.</summary>
    private struct Side(int index, long outstanding)
    {
        public readonly int Index = index;
        public long Outstanding = outstanding;
    }
}
