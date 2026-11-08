namespace LabStage.Domain.Orders;

/// <summary>Production stages in their fixed workflow order.</summary>
public enum ProductionStage
{
    Design = 1,
    Milling = 2,
    Layering = 3,
    Glazing = 4,
}

public enum StageStatus
{
    NotStarted = 0,
    InProgress = 1,
    SubmittedForReview = 2,
    RevisionRequested = 3,
    Approved = 4,
    Completed = 5,
    Blocked = 6,
}
