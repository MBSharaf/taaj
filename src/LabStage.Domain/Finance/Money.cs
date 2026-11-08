namespace LabStage.Domain.Finance;

/// <summary>
/// Amount in Egyptian pounds stored as integer piastres (1 EGP = 100 piastres).
/// Never use floating point for money.
/// </summary>
public readonly record struct Money(long Piastres) : IComparable<Money>
{
    public const string Currency = "EGP";
    public static readonly Money Zero = new(0);

    public static Money FromPounds(long pounds) => new(checked(pounds * 100));

    public static Money FromDecimal(decimal pounds)
    {
        var scaled = pounds * 100m;
        if (scaled != decimal.Truncate(scaled))
            throw new ArgumentException("EGP amounts support at most 2 decimal places.", nameof(pounds));
        return new Money(checked((long)scaled));
    }

    public decimal ToDecimal() => Piastres / 100m;

    public bool IsNegative => Piastres < 0;

    public static Money operator +(Money a, Money b) => new(checked(a.Piastres + b.Piastres));
    public static Money operator -(Money a, Money b) => new(checked(a.Piastres - b.Piastres));
    public static Money operator -(Money a) => new(checked(-a.Piastres));
    public static bool operator >(Money a, Money b) => a.Piastres > b.Piastres;
    public static bool operator <(Money a, Money b) => a.Piastres < b.Piastres;
    public static bool operator >=(Money a, Money b) => a.Piastres >= b.Piastres;
    public static bool operator <=(Money a, Money b) => a.Piastres <= b.Piastres;

    public int CompareTo(Money other) => Piastres.CompareTo(other.Piastres);

    public override string ToString() => $"{ToDecimal():0.00} {Currency}";
}
