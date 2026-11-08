namespace LabStage.Domain.Finance;

/// <summary>
/// Platform commission calculation.
/// - Laboratory commission default: 200 bps (2%) deducted from paid order milestones.
/// - Dentist service commission default: 100 bps (1%) applied to dentist order payments.
/// Each party's commission is strictly confidential and never exposed to the other party.
/// </summary>
public static class Commission
{
    public const int MaxBasisPoints = 10_000;
    public const int DefaultLabRateBps = 200;     // 2%
    public const int DefaultDentistRateBps = 100; // 1%

    /// <summary>Commission on a paid amount, rounded half-up to the nearest piastre.</summary>
    public static Money Calculate(Money paidAmount, int rateBasisPoints)
    {
        if (rateBasisPoints is < 0 or > MaxBasisPoints)
            throw new ArgumentOutOfRangeException(nameof(rateBasisPoints));
        if (paidAmount.IsNegative)
            throw new ArgumentOutOfRangeException(nameof(paidAmount), "Use Reverse for refunds.");

        var product = checked(paidAmount.Piastres * rateBasisPoints);
        return new Money((product + MaxBasisPoints / 2) / MaxBasisPoints);
    }

    /// <summary>1% platform commission applicable to the dentist.</summary>
    public static Money CalculateDentistCommission(Money paidAmount, int dentistRateBps = DefaultDentistRateBps) =>
        Calculate(paidAmount, dentistRateBps);

    /// <summary>
    /// Commission to reverse for a (partial) refund, proportional to the commission actually charged
    /// on the original payment, so a full refund reverses exactly what was charged.
    /// </summary>
    public static Money ReverseForRefund(Money originalPaid, Money originalCommission, Money refundAmount)
    {
        if (originalPaid.Piastres <= 0) throw new ArgumentOutOfRangeException(nameof(originalPaid));
        if (refundAmount.IsNegative || refundAmount > originalPaid)
            throw new ArgumentOutOfRangeException(nameof(refundAmount));
        if (refundAmount == originalPaid) return originalCommission;

        var product = checked(originalCommission.Piastres * refundAmount.Piastres);
        return new Money((product + originalPaid.Piastres / 2) / originalPaid.Piastres);
    }

    /// <summary>Amount owed to the laboratory for a paid amount (gateway fees are NOT deducted).</summary>
    public static Money LabNet(Money paidAmount, Money commission) => paidAmount - commission;
}
