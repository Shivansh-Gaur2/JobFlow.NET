namespace JobFlow.Core;

/// <summary>Safe, user-facing interpretation of persisted job and attempt facts.</summary>
public sealed record JobScene(JobSceneState State, string Message, int AttemptNumber, int MaxAttempts);
