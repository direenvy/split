namespace Split.Domain.Tests;

public class BalanceCalculatorTests
{
    [Fact]
    public void One_expense_paid_by_one_person_leaves_them_owed_everyone_elses_share()
    {
        // Aisyah pays 30.00 for dinner; the three of them split it evenly.
        Guid[] group = [People.Aisyah, People.Bala, People.Chong];
        IReadOnlyList<Posting> owed = MoneySplitter.AllocateEvenly(30.00m, group);

        IReadOnlyList<Balance> balances = BalanceCalculator.Compute(
            group,
            paid: [new Posting(People.Aisyah, 30.00m)],
            owed: owed);

        Assert.Equal(20.00m, balances.Single(b => b.ParticipantId == People.Aisyah).Net);
        Assert.Equal(-10.00m, balances.Single(b => b.ParticipantId == People.Bala).Net);
        Assert.Equal(-10.00m, balances.Single(b => b.ParticipantId == People.Chong).Net);
    }

    [Fact]
    public void Balances_come_back_in_the_order_the_participants_were_given()
    {
        Guid[] group = [People.Chong, People.Aisyah, People.Bala];

        IReadOnlyList<Balance> balances = BalanceCalculator.Compute(group, [], []);

        Assert.Equal(group, balances.Select(b => b.ParticipantId).ToArray());
    }

    [Fact]
    public void Someone_who_has_neither_paid_nor_consumed_anything_shows_as_zero()
    {
        Guid[] group = [People.Aisyah, People.Bala, People.Dinesh];

        IReadOnlyList<Balance> balances = BalanceCalculator.Compute(
            group,
            paid: [new Posting(People.Aisyah, 10.00m)],
            owed:
            [
                new Posting(People.Aisyah, 5.00m),
                new Posting(People.Bala, 5.00m)
            ]);

        Assert.Equal(0m, balances.Single(b => b.ParticipantId == People.Dinesh).Net);
    }

    [Fact]
    public void Several_expenses_with_different_payers_net_off_against_each_other()
    {
        Guid[] group = [People.Aisyah, People.Bala];

        // Aisyah pays 20 for a meal split evenly; Bala pays 6 for coffee split evenly.
        IReadOnlyList<Balance> balances = BalanceCalculator.Compute(
            group,
            paid:
            [
                new Posting(People.Aisyah, 20.00m),
                new Posting(People.Bala, 6.00m)
            ],
            owed:
            [
                new Posting(People.Aisyah, 10.00m),
                new Posting(People.Bala, 10.00m),
                new Posting(People.Aisyah, 3.00m),
                new Posting(People.Bala, 3.00m)
            ]);

        Assert.Equal(7.00m, balances.Single(b => b.ParticipantId == People.Aisyah).Net);
        Assert.Equal(-7.00m, balances.Single(b => b.ParticipantId == People.Bala).Net);
    }

    [Fact]
    public void Balances_always_sum_to_zero_for_a_real_ledger()
    {
        Guid[] group = [People.Aisyah, People.Bala, People.Chong, People.Dinesh, People.Elena];
        var random = new Random(31337);

        var paid = new List<Posting>();
        var owed = new List<Posting>();

        for (int expense = 0; expense < 50; expense++)
        {
            decimal amount = Money.FromCents(random.Next(1, 50_000));
            Guid payer = group[random.Next(group.Length)];

            // A random, non-empty subset of the group shares this expense.
            Guid[] among = [.. group.Where(_ => random.Next(2) == 0)];
            if (among.Length == 0)
            {
                among = [payer];
            }

            paid.Add(new Posting(payer, amount));
            owed.AddRange(MoneySplitter.AllocateEvenly(amount, among));
        }

        IReadOnlyList<Balance> balances = BalanceCalculator.Compute(group, paid, owed);

        Assert.Equal(0m, balances.Sum(b => b.Net));

        // And the whole point: whatever came out, the settlement algorithm can clear it.
        Settlement.AssertClears(balances, DebtSimplifier.Simplify([.. balances]));
    }

    // -----------------------------------------------------------------------------------
    // Guards
    // -----------------------------------------------------------------------------------

    [Fact]
    public void A_posting_for_somebody_outside_the_group_is_rejected()
    {
        ArgumentException error = Assert.Throws<ArgumentException>(() => BalanceCalculator.Compute(
            [People.Aisyah],
            paid: [new Posting(People.Farid, 10m)],
            owed: []));

        Assert.Contains("not in the group", error.Message);
    }

    [Fact]
    public void A_ledger_whose_sides_do_not_match_is_rejected()
    {
        // 10.00 paid but only 9.00 allocated: a cent-losing bug upstream, caught here rather
        // than surfacing later as an unsettleable group.
        ArgumentException error = Assert.Throws<ArgumentException>(() => BalanceCalculator.Compute(
            [People.Aisyah, People.Bala],
            paid: [new Posting(People.Aisyah, 10.00m)],
            owed:
            [
                new Posting(People.Aisyah, 4.50m),
                new Posting(People.Bala, 4.50m)
            ]));

        Assert.Contains("does not balance", error.Message);
    }

    [Fact]
    public void The_same_participant_listed_twice_is_rejected()
    {
        Assert.Throws<ArgumentException>(() => BalanceCalculator.Compute(
            [People.Aisyah, People.Aisyah],
            [],
            []));
    }

    [Fact]
    public void Null_arguments_are_rejected()
    {
        Assert.Throws<ArgumentNullException>(() => BalanceCalculator.Compute(null!, [], []));
        Assert.Throws<ArgumentNullException>(() => BalanceCalculator.Compute([People.Aisyah], null!, []));
        Assert.Throws<ArgumentNullException>(() => BalanceCalculator.Compute([People.Aisyah], [], null!));
    }
}
