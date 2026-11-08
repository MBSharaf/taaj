namespace LabStage.Domain.Orders;

public enum StageActor { Laboratory, Dentist }

/// <summary>
/// Allowed production-stage transitions. Stages must run in order: a stage may start only when
/// every earlier selected stage is Completed, so a required stage can never be silently skipped.
/// </summary>
public static class StageWorkflow
{
    public static bool CanTransition(StageStatus from, StageStatus to, StageActor actor, bool requiresDentistReview) =>
        (from, to, actor) switch
        {
            (StageStatus.NotStarted, StageStatus.InProgress, StageActor.Laboratory) => true,
            (StageStatus.RevisionRequested, StageStatus.InProgress, StageActor.Laboratory) => true,
            (StageStatus.Blocked, StageStatus.InProgress, StageActor.Laboratory) => true,
            (StageStatus.NotStarted or StageStatus.InProgress, StageStatus.Blocked, StageActor.Laboratory) => true,
            (StageStatus.InProgress, StageStatus.SubmittedForReview, StageActor.Laboratory) => requiresDentistReview,
            (StageStatus.InProgress, StageStatus.Completed, StageActor.Laboratory) => !requiresDentistReview,
            (StageStatus.SubmittedForReview, StageStatus.Approved, StageActor.Dentist) => true,
            (StageStatus.SubmittedForReview, StageStatus.RevisionRequested, StageActor.Dentist) => true,
            (StageStatus.Approved, StageStatus.Completed, StageActor.Laboratory) => true,
            _ => false,
        };

    /// <summary>Transitions that must carry a written reason in the audit history.</summary>
    public static bool RequiresReason(StageStatus to) => to is StageStatus.Blocked or StageStatus.RevisionRequested;

    /// <summary>A stage can be started only after all earlier selected stages are completed.</summary>
    public static bool CanStart(ProductionStage stage, IReadOnlyDictionary<ProductionStage, StageStatus> orderStages) =>
        orderStages.ContainsKey(stage) &&
        orderStages.Where(kv => kv.Key < stage).All(kv => kv.Value == StageStatus.Completed);
}
