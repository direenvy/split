namespace Split.Domain.Tests;

public class MoneyTests
{
    [Theory]
    [InlineData("0", 0L)]
    [InlineData("1", 100L)]
    [InlineData("0.01", 1L)]
    [InlineData("12.34", 1234L)]
    [InlineData("-12.34", -1234L)]
    [InlineData("1000000.00", 100_000_000L)]
    public void Amounts_convert_to_whole_cents(string amountText, long expected)
    {
        decimal amount = decimal.Parse(amountText, System.Globalization.CultureInfo.InvariantCulture);
        Assert.Equal(expected, Money.ToCents(amount));
    }

    [Fact]
    public void Trailing_zeros_do_not_change_the_value()
    {
        // 1.1 and 1.10 and 1.100 are the same money, even though decimal remembers the
        // difference in scale. A splitter that treated them differently would be alarming.
        Assert.Equal(110L, Money.ToCents(1.1m));
        Assert.Equal(110L, Money.ToCents(1.10m));
        Assert.Equal(110L, Money.ToCents(1.100m));
    }

    [Theory]
    [InlineData("0.001")]
    [InlineData("0.005")]
    [InlineData("1.234")]
    [InlineData("-0.001")]
    public void Sub_cent_amounts_are_rejected(string amountText)
    {
        decimal amount = decimal.Parse(amountText, System.Globalization.CultureInfo.InvariantCulture);
        Assert.Throws<ArgumentOutOfRangeException>(() => Money.ToCents(amount));
    }

    [Fact]
    public void Absurd_amounts_are_rejected_before_they_can_overflow()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => Money.ToCents(Money.MaxAmount + 1m));
        Assert.Throws<ArgumentOutOfRangeException>(() => Money.ToCents(-Money.MaxAmount - 1m));
    }

    [Fact]
    public void Cents_convert_back_to_the_amount_they_came_from()
    {
        for (long cents = -10_000; cents <= 10_000; cents++)
        {
            Assert.Equal(cents, Money.ToCents(Money.FromCents(cents)));
        }
    }
}
