using LabStage.Domain.Finance;
using LabStage.Domain.Orders;

namespace LabStage.Tests.Domain;

public class FinanceRulesTests
{
    [Fact]
    public void Commission_on_10000_order_with_20_80_split_is_40_and_160_for_lab_and_20_and_80_for_dentist()
    {
        var plan = MilestonePlan.Build(Money.FromPounds(10_000),
            [ProductionStage.Design, ProductionStage.Milling, ProductionStage.Layering, ProductionStage.Glazing]);

        Assert.Equal(2, plan.Count);
        Assert.Equal(Money.FromPounds(2_000), plan[0].Amount); // 20%
        Assert.Equal(Money.FromPounds(8_000), plan[1].Amount); // 80%

        var labCommissions = plan.Select(m => Commission.Calculate(m.Amount, 200)).ToList();
        Assert.Equal(Money.FromPounds(40), labCommissions[0]);
        Assert.Equal(Money.FromPounds(160), labCommissions[1]);
        Assert.Equal(Money.FromPounds(200), labCommissions[0] + labCommissions[1]);
        Assert.Equal(Money.FromPounds(1_960), Commission.LabNet(plan[0].Amount, labCommissions[0]));

        var dentistCommissions = plan.Select(m => Commission.CalculateDentistCommission(m.Amount)).ToList();
        Assert.Equal(Money.FromPounds(20), dentistCommissions[0]);
        Assert.Equal(Money.FromPounds(80), dentistCommissions[1]);
        Assert.Equal(Money.FromPounds(100), dentistCommissions[0] + dentistCommissions[1]);
    }

    [Theory]
    [InlineData(25, 200, 1)]      // 0.50 piastre rounds half-up to 1
    [InlineData(24, 200, 0)]      // 0.48 rounds down
    [InlineData(0, 200, 0)]
    [InlineData(333_333, 200, 6_667)]
    public void Commission_rounds_half_up_to_piastre(long paid, int bps, long expected) =>
        Assert.Equal(expected, Commission.Calculate(new Money(paid), bps).Piastres);

    [Fact]
    public void Odd_total_splits_20_80_without_losing_a_piastre()
    {
        var plan = MilestonePlan.Build(new Money(1_000_001), [ProductionStage.Design, ProductionStage.Glazing]);
        Assert.Equal(200_000, plan[0].Amount.Piastres);
        Assert.Equal(800_001, plan[1].Amount.Piastres);
        Assert.Equal(1_000_001, (plan[0].Amount + plan[1].Amount).Piastres);
    }

    [Fact]
    public void M1_follows_design_approval_and_M2_follows_last_selected_stage()
    {
        var plan = MilestonePlan.Build(Money.FromPounds(1000), [ProductionStage.Milling, ProductionStage.Design]);
        Assert.Equal((MilestoneTrigger.StageApproved, ProductionStage.Design), (plan[0].Trigger, plan[0].Stage!.Value));
        Assert.Equal((MilestoneTrigger.StageCompleted, ProductionStage.Milling), (plan[1].Trigger, plan[1].Stage!.Value));
    }

    [Fact]
    public void Without_design_M1_is_due_at_finalization()
    {
        var plan = MilestonePlan.Build(Money.FromPounds(1000), [ProductionStage.Layering, ProductionStage.Glazing]);
        Assert.Equal(MilestoneTrigger.OrderFinalized, plan[0].Trigger);
        Assert.Null(plan[0].Stage);
        Assert.Equal(ProductionStage.Glazing, plan[1].Stage);
    }

    [Fact]
    public void Plan_requires_positive_total_and_stages()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => MilestonePlan.Build(Money.Zero, [ProductionStage.Design]));
        Assert.Throws<ArgumentException>(() => MilestonePlan.Build(Money.FromPounds(10), []));
    }

    [Fact]
    public void Refund_reversal_is_proportional_and_full_refund_reverses_exactly()
    {
        var paid = Money.FromPounds(5_000);
        var commission = Commission.Calculate(paid, 200);
        Assert.Equal(commission, Commission.ReverseForRefund(paid, commission, paid));
        Assert.Equal(Money.FromPounds(50), Commission.ReverseForRefund(paid, commission, Money.FromPounds(2_500)));
        Assert.Throws<ArgumentOutOfRangeException>(() => Commission.ReverseForRefund(paid, commission, Money.FromPounds(5_001)));
    }

    [Fact]
    public void Money_rejects_sub_piastre_precision() =>
        Assert.Throws<ArgumentException>(() => Money.FromDecimal(10.005m));
}

public class StageWorkflowTests
{
    [Fact]
    public void Review_stage_cannot_be_completed_without_dentist_approval()
    {
        Assert.False(StageWorkflow.CanTransition(StageStatus.InProgress, StageStatus.Completed, StageActor.Laboratory, true));
        Assert.True(StageWorkflow.CanTransition(StageStatus.InProgress, StageStatus.SubmittedForReview, StageActor.Laboratory, true));
        Assert.False(StageWorkflow.CanTransition(StageStatus.SubmittedForReview, StageStatus.Approved, StageActor.Laboratory, true));
        Assert.True(StageWorkflow.CanTransition(StageStatus.SubmittedForReview, StageStatus.Approved, StageActor.Dentist, true));
        Assert.True(StageWorkflow.CanTransition(StageStatus.Approved, StageStatus.Completed, StageActor.Laboratory, true));
    }

    [Fact]
    public void Later_stage_cannot_start_before_earlier_selected_stages_complete()
    {
        var stages = new Dictionary<ProductionStage, StageStatus>
        {
            [ProductionStage.Design] = StageStatus.Approved,
            [ProductionStage.Glazing] = StageStatus.NotStarted,
        };
        Assert.False(StageWorkflow.CanStart(ProductionStage.Glazing, stages));
        stages[ProductionStage.Design] = StageStatus.Completed;
        Assert.True(StageWorkflow.CanStart(ProductionStage.Glazing, stages));
        Assert.False(StageWorkflow.CanStart(ProductionStage.Milling, stages)); // not selected
    }

    [Fact]
    public void Blocking_and_revisions_require_reason()
    {
        Assert.True(StageWorkflow.RequiresReason(StageStatus.Blocked));
        Assert.True(StageWorkflow.RequiresReason(StageStatus.RevisionRequested));
        Assert.False(StageWorkflow.RequiresReason(StageStatus.Completed));
    }
}
