namespace MemoryGame.Models;

public enum ExperimentPhase
{
    Ready,
    Countdown,
    CueBeat,
    Presenting,
    /// <summary>2-beat rest after the top-row presentation, before user recall.</summary>
    InterPause,
    /// <summary>4 beats (one per answer slot) for the user to say answers; no TTS, cards stay face-down.</summary>
    AnswerRecall,
    /// <summary>One long beat, then all cards can flip to reveal.</summary>
    FinalLong,
    Reveal
}
