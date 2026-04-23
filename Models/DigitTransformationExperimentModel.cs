namespace MemoryGame.Models;

/// <summary>Snapshot of experiment configuration and run state (easy to extend or serialize later).</summary>
public sealed class DigitTransformationExperimentModel
{
    public const int SeedCount = 4;

    public int[] SeedDigits { get; set; } = new int[SeedCount];
    public int[] AnswerDigits { get; set; } = new int[SeedCount];
    public PresentationMode Mode { get; set; } = PresentationMode.ReadAndShow;
    public TransformationOperation Operation { get; set; } = TransformationOperation.Add1;
    public int BeatDurationMs { get; set; } = 1000;
    public ExperimentPhase Phase { get; set; } = ExperimentPhase.Ready;
    public string StatusText { get; set; } = "Ready";
    public int? CountdownValue { get; set; }
    /// <summary>Which seed (0-3) is being read on this beat; no card flip, status/UI only.</summary>
    public int? ActiveSeedForSpeech { get; set; }
    public int? RecallPacingIndex { get; set; }
    public int CurrentDigitIndex { get; set; }
    public int PresentationBeatIndex { get; set; }
    public int GlobalBeatNumber { get; set; }
    public bool IsRevealed { get; set; }
}
