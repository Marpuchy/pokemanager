namespace pk3DS.Core.CTR;

/// <summary>
/// Progress of a long-running operation: <see cref="Value"/> steps done out of <see cref="Maximum"/>.
/// Replaces the WinForms ProgressBar previously used for progress reporting.
/// </summary>
public readonly record struct ProgressState(int Value, int Maximum);
