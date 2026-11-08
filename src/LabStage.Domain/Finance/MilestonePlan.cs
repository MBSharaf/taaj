using LabStage.Domain.Orders;

namespace LabStage.Domain.Finance;

public enum MilestoneTrigger
{
    /// <summary>Due when the order's commercial terms are finalized (used when Design is not selected).</summary>
    OrderFinalized = 1,
    /// <summary>Due when the dentist approves the given stage.</summary>
    StageApproved = 2,
    /// <summary>Due when the laboratory completes the given stage.</summary>
    StageCompleted = 3,
}

public sealed record MilestoneDefinition(int Sequence, Money Amount, MilestoneTrigger Trigger, ProductionStage? Stage);

/// <summary>
/// Builds the two-installment payment plan from the stages actually selected for an order.
/// Documented rule:
///  - M1 = round(total * 20%), due on dentist approval of Design; if Design is not selected, due at finalization.
///  - M2 = total - M1 (80%), due on completion of the last selected stage (Glazing when selected).
///  - If only Design is selected, M2 is due when Design is completed after approval.
/// </summary>
public static class MilestonePlan
{
    public const int FirstInstallmentPercent = 20;
    public const int SecondInstallmentPercent = 80;

    public static IReadOnlyList<MilestoneDefinition> Build(Money total, IEnumerable<ProductionStage> selectedStages)
    {
        if (total.Piastres <= 0) throw new ArgumentOutOfRangeException(nameof(total));
        var stages = selectedStages.Distinct().OrderBy(s => s).ToList();
        if (stages.Count == 0) throw new ArgumentException("At least one production stage is required.", nameof(selectedStages));

        var first = new Money((checked(total.Piastres * FirstInstallmentPercent) + 50) / 100);
        var second = total - first;

        var m1 = stages.Contains(ProductionStage.Design)
            ? new MilestoneDefinition(1, first, MilestoneTrigger.StageApproved, ProductionStage.Design)
            : new MilestoneDefinition(1, first, MilestoneTrigger.OrderFinalized, null);

        var m2 = new MilestoneDefinition(2, second, MilestoneTrigger.StageCompleted, stages[^1]);

        return second.Piastres == 0 ? [m1] : [m1, m2];
    }
}
