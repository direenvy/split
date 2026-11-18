namespace Split.Domain.Tests;

public class MoneySplitterTests
{
    // -----------------------------------------------------------------------------------
    // The case the class exists for
    // -----------------------------------------------------------------------------------

    [Fact]
    public void Ten_split_three_ways_gives_back_exactly_ten()
    {
        IReadOnlyList<decimal> shares = MoneySplitter.SplitEvenly(10.00m, 3);

        Assert.Equal(new[] { 3.34m, 3.33m, 3.33m }, shares.ToArray());
        Assert.Equal(10.00m, shares.Sum());
    }

    [Fact]
    public void The_leftover_cents_go_to_the_front_of_the_list()
    {
        // 1.00 seven ways is 14.28... cents each: 14 cents all round, with 2 cents left over,
        // so the first two people pay 15 and the other five pay 14.
        IReadOnlyList<decimal> shares = MoneySplitter.SplitEvenly(1.00m, 7);

        Assert.Equal(new[] { 0.15m, 0.15m, 0.14m, 0.14m, 0.14m, 0.14m, 0.14m }, shares.ToArray());
        Assert.Equal(1.00m, shares.Sum());
    }

    [Theory]
    [InlineData("0.01", 3)]
    [InlineData("0.02", 3)]
    [InlineData("10.00", 3)]
    [InlineData("10.00", 6)]
    [InlineData("33.33", 7)]
    [InlineData("100.00", 9)]
    [InlineData("99.99", 11)]
    [InlineData("0.03", 4)]
    [InlineData("1234.56", 13)]
    public void Shares_always_add_back_up_to_the_original_amount(string amountText, int ways)
    {
        decimal amount = decimal.Parse(amountText, System.Globalization.CultureInfo.InvariantCulture);

        IReadOnlyList<decimal> shares = MoneySplitter.SplitEvenly(amount, ways);

        Assert.Equal(ways, shares.Count);
        Assert.Equal(amount, shares.Sum());
    }

    [Fact]
    public void Every_amount_and_group_size_in_a_wide_sweep_adds_back_up()
    {
        // Exhaustive over a range rather than a handful of examples: if any combination of
        // amount and group size loses or invents a cent, this finds it.
        for (int cents = 0; cents <= 1_000; cents++)
        {
            decimal amount = Money.FromCents(cents);

            for (int ways = 1; ways <= 12; ways++)
            {
                IReadOnlyList<decimal> shares = MoneySplitter.SplitEvenly(amount, ways);
                Assert.Equal(amount, shares.Sum());
            }
        }
    }

    [Fact]
    public void No_two_shares_ever_differ_by_more_than_a_cent()
    {
        for (int cents = 0; cents <= 500; cents++)
        {
            IReadOnlyList<decimal> shares = MoneySplitter.SplitEvenly(Money.FromCents(cents), 7);
            Assert.True(shares.Max() - shares.Min() <= 0.01m, $"Uneven split for {cents} cents.");
        }
    }

    // -----------------------------------------------------------------------------------
    // Edges
    // -----------------------------------------------------------------------------------

    [Fact]
    public void Splitting_one_way_gives_the_whole_amount_back()
    {
        Assert.Equal(new[] { 42.42m }, MoneySplitter.SplitEvenly(42.42m, 1).ToArray());
    }

    [Fact]
    public void Splitting_zero_gives_everyone_zero()
    {
        Assert.Equal(new[] { 0m, 0m, 0m }, MoneySplitter.SplitEvenly(0m, 3).ToArray());
    }

    [Fact]
    public void Negative_amounts_split_the_same_way_in_the_other_direction()
    {
        // Refunds and corrections have to round consistently with the charges they reverse.
        IReadOnlyList<decimal> shares = MoneySplitter.SplitEvenly(-10.00m, 3);

        Assert.Equal(new[] { -3.34m, -3.33m, -3.33m }, shares.ToArray());
        Assert.Equal(-10.00m, shares.Sum());
    }

    [Fact]
    public void Splitting_between_nobody_is_rejected()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => MoneySplitter.SplitEvenly(10m, 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => MoneySplitter.SplitEvenly(10m, -1));
    }

    [Fact]
    public void Amounts_with_sub_cent_precision_are_rejected_rather_than_rounded()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => MoneySplitter.SplitEvenly(10.005m, 2));
    }

    // -----------------------------------------------------------------------------------
    // AllocateEvenly: shares paired with the people who owe them
    // -----------------------------------------------------------------------------------

    [Fact]
    public void Allocation_pairs_each_share_with_the_person_in_the_same_position()
    {
        IReadOnlyList<Posting> postings = MoneySplitter.AllocateEvenly(
            10.00m,
            [People.Aisyah, People.Bala, People.Chong]);

        Assert.Equal(People.Aisyah, postings[0].ParticipantId);
        Assert.Equal(3.34m, postings[0].Amount);
        Assert.Equal(3.33m, postings[1].Amount);
        Assert.Equal(3.33m, postings[2].Amount);
        Assert.Equal(10.00m, postings.Sum(p => p.Amount));
    }

    [Fact]
    public void Allocating_to_nobody_is_rejected()
    {
        ArgumentException error = Assert.Throws<ArgumentException>(
            () => MoneySplitter.AllocateEvenly(10m, []));

        Assert.Contains("at least one person", error.Message);
    }

    [Fact]
    public void Naming_the_same_person_twice_in_a_split_is_rejected()
    {
        ArgumentException error = Assert.Throws<ArgumentException>(
            () => MoneySplitter.AllocateEvenly(10m, [People.Aisyah, People.Aisyah]));

        Assert.Contains("twice", error.Message);
    }

    [Fact]
    public void Allocating_to_a_null_list_is_rejected()
    {
        Assert.Throws<ArgumentNullException>(() => MoneySplitter.AllocateEvenly(10m, null!));
    }
}
