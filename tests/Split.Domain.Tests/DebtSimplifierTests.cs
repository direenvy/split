namespace Split.Domain.Tests;

/// <summary>
/// Tests for the settlement algorithm. These are grouped by the thing being checked rather
/// than by method, because there is only one public method and almost everything interesting
/// about it is a property of its output.
/// </summary>
public class DebtSimplifierTests
{
    // -----------------------------------------------------------------------------------
    // The empty and trivial cases
    // -----------------------------------------------------------------------------------

    [Fact]
    public void A_group_with_no_participants_needs_no_payments()
    {
        Assert.Empty(DebtSimplifier.Simplify([]));
    }

    [Fact]
    public void A_group_that_is_already_square_needs_no_payments()
    {
        Balance[] balances = Settlement.Balances(
            (People.Aisyah, 0m),
            (People.Bala, 0m),
            (People.Chong, 0m));

        Assert.Empty(DebtSimplifier.Simplify(balances));
    }

    [Fact]
    public void People_who_are_square_are_left_out_of_the_payments()
    {
        Balance[] balances = Settlement.Balances(
            (People.Aisyah, 25m),
            (People.Bala, -25m),
            (People.Chong, 0m));

        IReadOnlyList<Transfer> payments = DebtSimplifier.Simplify(balances);

        Transfer only = Assert.Single(payments);
        Assert.Equal(People.Bala, only.FromParticipantId);
        Assert.Equal(People.Aisyah, only.ToParticipantId);
        Assert.Equal(25m, only.Amount);
        Settlement.AssertClears(balances, payments);
    }

    [Fact]
    public void Two_people_settle_in_one_payment()
    {
        Balance[] balances = Settlement.Balances(
            (People.Aisyah, -12.50m),
            (People.Bala, 12.50m));

        Transfer only = Assert.Single(DebtSimplifier.Simplify(balances));

        Assert.Equal(People.Aisyah, only.FromParticipantId);
        Assert.Equal(People.Bala, only.ToParticipantId);
        Assert.Equal(12.50m, only.Amount);
    }

    // -----------------------------------------------------------------------------------
    // The shapes the brief called out
    // -----------------------------------------------------------------------------------

    [Fact]
    public void One_person_paying_for_everything_costs_one_payment_per_other_person()
    {
        // Aisyah covered a 400.00 dinner for five; the other four each owe her 80.00.
        Balance[] balances = Settlement.Balances(
            (People.Aisyah, 320m),
            (People.Bala, -80m),
            (People.Chong, -80m),
            (People.Dinesh, -80m),
            (People.Elena, -80m));

        IReadOnlyList<Transfer> payments = DebtSimplifier.Simplify(balances);

        Assert.Equal(4, payments.Count);
        Assert.All(payments, p => Assert.Equal(People.Aisyah, p.ToParticipantId));
        Assert.All(payments, p => Assert.Equal(80m, p.Amount));
        Settlement.AssertClears(balances, payments);
        Assert.Equal(Settlement.MinimumPayments(balances), payments.Count);
    }

    [Fact]
    public void A_single_debtor_against_many_creditors_pays_each_of_them_once()
    {
        Balance[] balances = Settlement.Balances(
            (People.Aisyah, -100m),
            (People.Bala, 50m),
            (People.Chong, 30m),
            (People.Dinesh, 20m));

        IReadOnlyList<Transfer> payments = DebtSimplifier.Simplify(balances);

        Assert.Equal(3, payments.Count);
        Assert.All(payments, p => Assert.Equal(People.Aisyah, p.FromParticipantId));
        Settlement.AssertClears(balances, payments);
        Assert.Equal(Settlement.MinimumPayments(balances), payments.Count);
    }

    [Fact]
    public void Two_independent_pairs_settle_in_two_payments_not_three()
    {
        // This is the case plain greedy matching gets wrong. Sorted by size, greedy would pair
        // Aisyah's +30 with Chong's -30 first, leaving Bala and Dinesh needing two more
        // payments between them: three in total. Spotting that {Aisyah, Chong} and
        // {Bala, Dinesh} are each already zero-sum gets it done in two.
        Balance[] balances = Settlement.Balances(
            (People.Aisyah, 30m),
            (People.Bala, 20m),
            (People.Chong, -30m),
            (People.Dinesh, -20m));

        IReadOnlyList<Transfer> payments = DebtSimplifier.Simplify(balances);

        Assert.Equal(2, payments.Count);
        Settlement.AssertClears(balances, payments);
        Assert.Equal(Settlement.MinimumPayments(balances), payments.Count);
    }

    [Fact]
    public void Three_independent_pairs_settle_in_three_payments()
    {
        Balance[] balances = Settlement.Balances(
            (People.Aisyah, 15m),
            (People.Bala, -15m),
            (People.Chong, 42.75m),
            (People.Dinesh, -42.75m),
            (People.Elena, 8.10m),
            (People.Farid, -8.10m));

        IReadOnlyList<Transfer> payments = DebtSimplifier.Simplify(balances);

        Assert.Equal(3, payments.Count);
        Settlement.AssertClears(balances, payments);
        Assert.Equal(Settlement.MinimumPayments(balances), payments.Count);
    }

    [Fact]
    public void A_zero_sum_subgroup_hiding_inside_a_larger_group_is_still_found()
    {
        // {Chong, Dinesh} cancel exactly at 60; the other four have to be settled among
        // themselves. Minimum is therefore (6 non-zero people) - (2 subsets) = 4 payments.
        Balance[] balances = Settlement.Balances(
            (People.Aisyah, 100m),
            (People.Bala, -40m),
            (People.Chong, 60m),
            (People.Dinesh, -60m),
            (People.Elena, -35m),
            (People.Farid, -25m));

        IReadOnlyList<Transfer> payments = DebtSimplifier.Simplify(balances);

        Settlement.AssertClears(balances, payments);
        Assert.Equal(Settlement.MinimumPayments(balances), payments.Count);
        Assert.Equal(4, payments.Count);
    }

    // -----------------------------------------------------------------------------------
    // Rounding: the cents have to land exactly
    // -----------------------------------------------------------------------------------

    [Fact]
    public void An_amount_that_does_not_divide_evenly_still_settles_to_the_last_cent()
    {
        // 10.00 split three ways. Aisyah paid; the shares are 3.34 / 3.33 / 3.33, so her net
        // is 10.00 - 3.34 = 6.66 and the other two are -3.33 each. Nothing may be rounded here
        // or the group will never reach zero.
        Balance[] balances = Settlement.Balances(
            (People.Aisyah, 6.66m),
            (People.Bala, -3.33m),
            (People.Chong, -3.33m));

        IReadOnlyList<Transfer> payments = DebtSimplifier.Simplify(balances);

        Assert.Equal(2, payments.Count);
        Assert.Equal(6.66m, payments.Sum(p => p.Amount));
        Settlement.AssertClears(balances, payments);
    }

    [Fact]
    public void Payments_sum_to_exactly_the_total_debt()
    {
        Balance[] balances = Settlement.Balances(
            (People.Aisyah, 33.34m),
            (People.Bala, 33.33m),
            (People.Chong, -22.22m),
            (People.Dinesh, -44.45m));

        IReadOnlyList<Transfer> payments = DebtSimplifier.Simplify(balances);

        decimal owed = balances.Where(b => b.Net < 0).Sum(b => -b.Net);
        Assert.Equal(owed, payments.Sum(p => p.Amount));
        Settlement.AssertClears(balances, payments);
    }

    [Fact]
    public void A_single_stray_cent_is_rejected_rather_than_quietly_absorbed()
    {
        // The kind of ledger a float-based splitter produces. Settling it would mean inventing
        // a cent from nowhere, so it is refused.
        Balance[] balances = Settlement.Balances(
            (People.Aisyah, 3.33m),
            (People.Bala, -3.34m));

        ArgumentException error = Assert.Throws<ArgumentException>(() => DebtSimplifier.Simplify(balances));
        Assert.Contains("sum to zero", error.Message);
    }

    [Fact]
    public void Sub_cent_balances_are_rejected()
    {
        Balance[] balances = Settlement.Balances(
            (People.Aisyah, 0.005m),
            (People.Bala, -0.005m));

        Assert.Throws<ArgumentOutOfRangeException>(() => DebtSimplifier.Simplify(balances));
    }

    // -----------------------------------------------------------------------------------
    // Guards
    // -----------------------------------------------------------------------------------

    [Fact]
    public void Balances_that_do_not_cancel_are_rejected()
    {
        Balance[] balances = Settlement.Balances(
            (People.Aisyah, 10m),
            (People.Bala, -5m));

        ArgumentException error = Assert.Throws<ArgumentException>(() => DebtSimplifier.Simplify(balances));
        Assert.Contains("5", error.Message);
    }

    [Fact]
    public void The_same_participant_twice_is_rejected()
    {
        Balance[] balances = Settlement.Balances(
            (People.Aisyah, 10m),
            (People.Aisyah, -10m));

        ArgumentException error = Assert.Throws<ArgumentException>(() => DebtSimplifier.Simplify(balances));
        Assert.Contains("twice", error.Message);
    }

    [Fact]
    public void A_null_balance_set_is_rejected()
    {
        Assert.Throws<ArgumentNullException>(() => DebtSimplifier.Simplify(null!));
    }

    // -----------------------------------------------------------------------------------
    // Determinism
    // -----------------------------------------------------------------------------------

    [Fact]
    public void The_order_balances_arrive_in_does_not_change_the_answer()
    {
        Balance[] balances = Settlement.Balances(
            (People.Aisyah, 40m),
            (People.Bala, -15m),
            (People.Chong, 5m),
            (People.Dinesh, -30m));

        IReadOnlyList<Transfer> fromOneOrder = DebtSimplifier.Simplify(balances);
        IReadOnlyList<Transfer> fromAnother = DebtSimplifier.Simplify([.. balances.Reverse()]);

        Assert.Equal(fromOneOrder, fromAnother);
    }

    [Fact]
    public void Payments_come_back_largest_first()
    {
        Balance[] balances = Settlement.Balances(
            (People.Aisyah, 90m),
            (People.Bala, -10m),
            (People.Chong, -30m),
            (People.Dinesh, -50m));

        IReadOnlyList<Transfer> payments = DebtSimplifier.Simplify(balances);

        Assert.Equal(new[] { 50m, 30m, 10m }, payments.Select(p => p.Amount).ToArray());
    }

    // -----------------------------------------------------------------------------------
    // Properties that must hold for every input
    // -----------------------------------------------------------------------------------

    [Theory]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    [InlineData(5)]
    [InlineData(6)]
    [InlineData(7)]
    [InlineData(8)]
    public void Random_groups_are_settled_exactly_and_in_the_fewest_payments(int size)
    {
        // A fixed seed per size: these are randomised cases, but a failure is always
        // reproducible, which a test that re-rolls on every run would not be.
        var random = new Random(9_000 + size);

        for (int round = 0; round < 200; round++)
        {
            Balance[] balances = RandomZeroSumGroup(random, size);

            IReadOnlyList<Transfer> payments = DebtSimplifier.Simplify(balances);

            Settlement.AssertClears(balances, payments);
            Assert.Equal(Settlement.MinimumPayments(balances), payments.Count);
            Assert.True(
                payments.Count <= balances.Count(b => b.Net != 0m) - (balances.Any(b => b.Net != 0m) ? 1 : 0),
                "A settlement should never need more than one payment fewer than it has active people.");
        }
    }

    [Fact]
    public void A_group_larger_than_the_exact_limit_still_settles_exactly()
    {
        // Past DebtSimplifier.ExactPartitionLimit the algorithm stops trying to prove
        // minimality and matches greedily. It must still clear the group to the cent, and
        // still stay within the n-1 bound.
        const int size = 25;
        Assert.True(size > DebtSimplifier.ExactPartitionLimit);

        var random = new Random(4242);
        Balance[] balances = RandomZeroSumGroup(random, size);

        IReadOnlyList<Transfer> payments = DebtSimplifier.Simplify(balances);

        Settlement.AssertClears(balances, payments);
        Assert.True(payments.Count <= size - 1, $"Expected at most {size - 1} payments, got {payments.Count}.");
    }

    [Fact]
    public void A_group_at_the_exact_limit_completes_quickly()
    {
        var random = new Random(777);
        Balance[] balances = RandomZeroSumGroup(random, DebtSimplifier.ExactPartitionLimit);

        var clock = System.Diagnostics.Stopwatch.StartNew();
        IReadOnlyList<Transfer> payments = DebtSimplifier.Simplify(balances);
        clock.Stop();

        Settlement.AssertClears(balances, payments);
        Assert.True(
            clock.Elapsed < TimeSpan.FromSeconds(5),
            $"Settling {DebtSimplifier.ExactPartitionLimit} people took {clock.Elapsed}, which is too slow to do in a request.");
    }

    /// <summary>
    /// Builds a group whose balances sum to exactly zero, by handing out random cents and
    /// giving the last person whatever is left over.
    /// </summary>
    private static Balance[] RandomZeroSumGroup(Random random, int size)
    {
        var cents = new long[size];
        long running = 0;

        for (int i = 0; i < size - 1; i++)
        {
            cents[i] = random.Next(-20_000, 20_001);
            running += cents[i];
        }

        cents[size - 1] = -running;

        return [.. cents.Select((c, i) => new Balance(People.Numbered(i), Money.FromCents(c)))];
    }
}
