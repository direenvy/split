namespace Split.Domain;

/// <summary>
/// Money is counted in whole cents, as a <see cref="long"/>, everywhere inside the domain.
///
/// Why: a bill split of 10.00 three ways is 3.3333... — a number that cannot be written
/// exactly in binary floating point. <c>double</c> would quietly lose a fraction of a cent
/// on every expense and the balances would stop summing to zero, which is the one invariant
/// the settlement algorithm depends on. <c>decimal</c> is exact for the values we accept, and
/// integer cents make "these numbers add up exactly" a thing the compiler enforces rather
/// than something we hope for.
///
/// The public surface of the domain speaks <c>decimal</c>, because that is what callers and
/// the database store. Conversion happens at the edge, here.
/// </summary>
public static class Money
{
    /// <summary>Currencies we support are all two-decimal currencies (MYR, USD, EUR, ...).</summary>
    public const int DecimalPlaces = 2;

    private const decimal CentsPerUnit = 100m;

    /// <summary>
    /// Largest amount we accept, in major units. Well below the point where cents would
    /// overflow a <see cref="long"/>, so no arithmetic inside the domain can wrap around.
    /// </summary>
    public const decimal MaxAmount = 1_000_000_000_000m;

    /// <summary>
    /// Converts an amount to whole cents.
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException">
    /// The amount has more than two decimal places, or is outside <see cref="MaxAmount"/>.
    /// We reject rather than round: silently turning 0.005 into 0.01 (or 0.00) is how a
    /// ledger ends up a cent out, and the caller is better placed to decide.
    /// </exception>
    public static long ToCents(decimal amount)
    {
        if (amount > MaxAmount || amount < -MaxAmount)
        {
            throw new ArgumentOutOfRangeException(
                nameof(amount), amount, $"Amount must be between -{MaxAmount} and {MaxAmount}.");
        }

        if (decimal.Round(amount, DecimalPlaces) != amount)
        {
            throw new ArgumentOutOfRangeException(
                nameof(amount), amount, $"Amount must have at most {DecimalPlaces} decimal places.");
        }

        // amount * 100 is exactly integral at this point, so the cast cannot lose anything.
        return (long)(amount * CentsPerUnit);
    }

    /// <summary>Converts whole cents back to a two-decimal amount.</summary>
    public static decimal FromCents(long cents) => cents / CentsPerUnit;
}
