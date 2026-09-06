namespace JobFlow.Core;

public enum JobSceneState
{
    WaitingAtIntake,
    BeingWorked,
    Recovering,
    Delivered,
    Unfinished,
    UnderInvestigation
}
