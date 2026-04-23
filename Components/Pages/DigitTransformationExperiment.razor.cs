using System.Globalization;
using MemoryGame.Models;
using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;

namespace MemoryGame.Components.Pages;

public partial class DigitTransformationExperiment : ComponentBase, IAsyncDisposable
{
    [Inject] private IJSRuntime Js { get; set; } = null!;

    private const string SpeechJs = "digitExperimentSpeech.speakDigit";
    private const string AudioPrepareJs = "digitExperimentAudio.prepare";
    private const string AudioClickJs = "digitExperimentAudio.playMetronomeClick";
    private const string AudioLongBeatJs = "digitExperimentAudio.playLongBeat";
    private const double FinalBeatLengthMultiplier = 2.5;

    private DigitTransformationExperimentModel Model { get; } = new();
    private int _beatInput = 1000;
    private double _beatHzInput = 1.0;
    private bool _beatPulse;
    private bool _longBeatActive;
    private int _longBeatDurationMs = 2500;
    private bool _sequenceInProgress;
    private int _runEpoch;
    private CancellationTokenSource? _runCts;
    private int[]? _replaySeedSnapshot;
    private PresentationMode _replayModeSnapshot;
    private TransformationOperation _replayOpSnapshot;
    private int _replayBeatMsSnapshot;

    private string StartButtonLabel => _sequenceInProgress ? "Restart" : "Start";

    private string? BeatRingClass =>
        _longBeatActive
            ? "dte__beat-ring--long"
            : _beatPulse
                ? "dte__beat-ring--pulse"
                : null;

    private string? BeatRingStyle =>
        _longBeatActive ? $"--dte-long-beat: {_longBeatDurationMs}ms;" : null;

    private void OnModeChange(ChangeEventArgs e)
    {
        if (e.Value is not string s || !Enum.TryParse<PresentationMode>(s, out var mode))
        {
            return;
        }
        Model.Mode = mode;
    }

    private void OnOpChange(ChangeEventArgs e)
    {
        Model.Operation = e.Value is string { } v && v == "Add3"
            ? TransformationOperation.Add3
            : TransformationOperation.Add1;
    }

    private string? GetTopCardClass(int index) =>
        Model.ActiveSeedForSpeech == index && Model.Phase == ExperimentPhase.Presenting
            ? "dte__card--active"
            : null;

    private string? GetBottomCardClass(int index) =>
        Model.RecallPacingIndex == index && Model.Phase == ExperimentPhase.AnswerRecall
            ? "dte__card--active"
            : null;

    private static string? GetFlipClass(bool faceUp) => faceUp ? "dte__card-inner--flipped" : null;

    private bool IsTopRowFaceUp(int index) =>
        Model.IsRevealed
        || (Model.Phase == ExperimentPhase.Presenting
            && Model.ActiveSeedForSpeech == index
            && Model.Mode is PresentationMode.ShowOnly or PresentationMode.ReadAndShow);

    private bool IsBottomRowFaceUp(int _) =>
        Model.IsRevealed;

    private string FormatBeatHz() => _beatHzInput.ToString("0.##", CultureInfo.InvariantCulture);

    private void OnBeatHzInput(ChangeEventArgs e)
    {
        if (!double.TryParse(e.Value?.ToString(), NumberStyles.Any, CultureInfo.InvariantCulture, out var hz))
        {
            return;
        }
        hz = Math.Clamp(hz, 0.2, 3.4);
        _beatHzInput = hz;
        var ms = (int)Math.Round(1000.0 / hz);
        _beatInput = Math.Clamp(ms, 300, 5000);
        Model.BeatDurationMs = _beatInput;
        _beatHzInput = Math.Round(1000.0 / _beatInput, 3);
    }

    private void OnBeatMsInput(ChangeEventArgs e)
    {
        if (!int.TryParse(e.Value?.ToString(), out var ms))
        {
            return;
        }
        _beatInput = Math.Clamp(ms, 300, 5000);
        Model.BeatDurationMs = _beatInput;
        _beatHzInput = Math.Round(1000.0 / _beatInput, 3);
    }

    private void ApplyBeatDurationFromInput()
    {
        var v = _beatInput;
        if (v < 300)
        {
            v = 300;
        }
        if (v > 5000)
        {
            v = 5000;
        }
        _beatInput = v;
        Model.BeatDurationMs = v;
        _beatHzInput = Math.Round(1000.0 / v, 3);
    }

    private Task OnStartOrRestartAsync() => StartInternalAsync(newSeeds: true);

    private Task OnReplayAsync()
    {
        if (_replaySeedSnapshot is not { } snap)
        {
            return Task.CompletedTask;
        }
        Array.Copy(snap, Model.SeedDigits, snap.Length);
        Model.Mode = _replayModeSnapshot;
        Model.Operation = _replayOpSnapshot;
        Model.BeatDurationMs = _replayBeatMsSnapshot;
        _beatInput = _replayBeatMsSnapshot;
        _beatHzInput = Math.Round(1000.0 / _beatInput, 3);
        return StartInternalAsync(newSeeds: false);
    }

    private Task OnNewRoundAsync() => StartInternalAsync(newSeeds: true);

    protected override void OnInitialized()
    {
        _beatHzInput = Math.Round(1000.0 / _beatInput, 3);
    }

    private async Task StartInternalAsync(bool newSeeds)
    {
        try
        {
            await Js.InvokeVoidAsync(AudioPrepareJs);
        }
        catch (JSDisconnectedException)
        {
            // Circuit closed
        }
        _runCts?.Cancel();
        _runCts?.Dispose();
        _runCts = new CancellationTokenSource();
        var token = _runCts.Token;
        var epoch = Interlocked.Increment(ref _runEpoch);
        ApplyBeatDurationFromInput();
        if (newSeeds)
        {
            FillRandomSeeds();
        }
        ComputeAnswers();
        _sequenceInProgress = true;
        Model.IsRevealed = false;
        Model.CountdownValue = null;
        Model.ActiveSeedForSpeech = null;
        Model.RecallPacingIndex = null;
        _longBeatActive = false;
        await InvokeAsync(StateHasChanged);
        try
        {
            await RunSequenceAsync(token);
        }
        catch (OperationCanceledException)
        {
            // Expected when user restarts
        }
        finally
        {
            if (epoch == _runEpoch)
            {
                _sequenceInProgress = false;
            }
            await InvokeAsync(StateHasChanged);
        }
    }

    private void FillRandomSeeds()
    {
        var r = Random.Shared;
        for (var i = 0; i < DigitTransformationExperimentModel.SeedCount; i++)
        {
            Model.SeedDigits[i] = r.Next(0, 10);
        }
    }

    private void ComputeAnswers()
    {
        var n = (int)Model.Operation;
        for (var i = 0; i < DigitTransformationExperimentModel.SeedCount; i++)
        {
            Model.AnswerDigits[i] = (Model.SeedDigits[i] + n) % 10;
        }
    }

    private void CaptureRoundSnapshot()
    {
        _replaySeedSnapshot = (int[])Model.SeedDigits.Clone();
        _replayModeSnapshot = Model.Mode;
        _replayOpSnapshot = Model.Operation;
        _replayBeatMsSnapshot = Model.BeatDurationMs;
    }

    private async Task RunSequenceAsync(CancellationToken token)
    {
        CaptureRoundSnapshot();
        Model.Phase = ExperimentPhase.Countdown;
        for (var n = 3; n >= 1; n--)
        {
            token.ThrowIfCancellationRequested();
            Model.CountdownValue = n;
            Model.StatusText = "Countdown";
            await InvokeAsync(StateHasChanged);
            await PlayMetronomeClickAsync();
            await Task.Delay(1000, token);
        }
        Model.CountdownValue = null;
        var beat = Model.BeatDurationMs;
        Model.GlobalBeatNumber = 1;
        Model.Phase = ExperimentPhase.CueBeat;
        Model.StatusText = "Beat 1";
        Model.ActiveSeedForSpeech = null;
        await InvokeAsync(StateHasChanged);
        await RunSingleBeatAsync(beat, token, speakDigit: null);

        for (var d = 0; d < DigitTransformationExperimentModel.SeedCount; d++)
        {
            token.ThrowIfCancellationRequested();
            Model.PresentationBeatIndex = d;
            Model.CurrentDigitIndex = d;
            Model.Phase = ExperimentPhase.Presenting;
            Model.StatusText = $"Presenting digit {d + 1} of 4";
            Model.ActiveSeedForSpeech = d;
            await InvokeAsync(StateHasChanged);
            int? toSpeak = Model.Mode is PresentationMode.ReadOnly or PresentationMode.ReadAndShow
                ? Model.SeedDigits[d]
                : null;
            await RunSingleBeatAsync(beat, token, toSpeak);
        }
        Model.ActiveSeedForSpeech = null;

        token.ThrowIfCancellationRequested();
        Model.Phase = ExperimentPhase.InterPause;
        Model.StatusText = "Pause — 2 beats before you say your answers";
        await InvokeAsync(StateHasChanged);
        await RunSingleBeatAsync(beat, token, null);

        token.ThrowIfCancellationRequested();
        Model.StatusText = "Pause — 1 beat";
        await InvokeAsync(StateHasChanged);
        await RunSingleBeatAsync(beat, token, null);

        token.ThrowIfCancellationRequested();
        Model.Phase = ExperimentPhase.AnswerRecall;
        for (var a = 0; a < DigitTransformationExperimentModel.SeedCount; a++)
        {
            token.ThrowIfCancellationRequested();
            Model.RecallPacingIndex = a;
            Model.StatusText = $"Your answers — beat {a + 1} of 4 (say answer digit {a + 1})";
            await InvokeAsync(StateHasChanged);
            await RunSingleBeatAsync(beat, token, null);
        }
        Model.RecallPacingIndex = null;

        var longMs = (int)(beat * FinalBeatLengthMultiplier);
        if (longMs < beat + 400)
        {
            longMs = beat + 400;
        }
        token.ThrowIfCancellationRequested();
        Model.Phase = ExperimentPhase.FinalLong;
        Model.StatusText = "End of round — hold for the long beat";
        await InvokeAsync(StateHasChanged);
        await RunLongFinalBeatAsync(longMs, token);

        token.ThrowIfCancellationRequested();
        Model.Phase = ExperimentPhase.Reveal;
        Model.IsRevealed = true;
        Model.StatusText = "Round over";
        Model.CountdownValue = null;
        await InvokeAsync(StateHasChanged);
    }

    private async Task PlayMetronomeClickAsync()
    {
        try
        {
            await Js.InvokeVoidAsync(AudioClickJs);
        }
        catch (JSDisconnectedException)
        {
            // Circuit closed
        }
    }

    private async Task RunLongFinalBeatAsync(int totalMs, CancellationToken token)
    {
        _longBeatDurationMs = totalMs;
        _beatPulse = false;
        try
        {
            await Js.InvokeVoidAsync(AudioLongBeatJs, totalMs);
        }
        catch (JSDisconnectedException)
        {
            // Circuit closed
        }
        _longBeatActive = true;
        await InvokeAsync(StateHasChanged);
        try
        {
            await Task.Delay(totalMs, token);
        }
        catch (OperationCanceledException)
        {
            _longBeatActive = false;
            throw;
        }
        _longBeatActive = false;
        await InvokeAsync(StateHasChanged);
    }

    private async Task RunSingleBeatAsync(
        int beatDurationMs,
        CancellationToken token,
        int? speakDigit)
    {
        await PlayMetronomeClickAsync();
        if (speakDigit is { } digit)
        {
            try
            {
                await Js.InvokeVoidAsync(SpeechJs, digit);
            }
            catch (JSDisconnectedException)
            {
                // Circuit closed
            }
        }
        _beatPulse = true;
        await InvokeAsync(StateHasChanged);
        var pulseMs = Math.Min(420, Math.Max(120, beatDurationMs / 3));
        try
        {
            await Task.Delay(pulseMs, token);
        }
        catch (OperationCanceledException)
        {
            _beatPulse = false;
            throw;
        }
        _beatPulse = false;
        await InvokeAsync(StateHasChanged);
        var rest = Math.Max(0, beatDurationMs - pulseMs);
        if (rest > 0)
        {
            await Task.Delay(rest, token);
        }
    }

    public async ValueTask DisposeAsync()
    {
        _runCts?.Cancel();
        _runCts?.Dispose();
        _runCts = null;
        await Task.CompletedTask;
    }
}
