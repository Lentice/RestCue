using System;
using RestCue.Core.Reminders;

namespace RestCue.Core.Audio;

public sealed class BreakGuideAudioCoordinator
{
    private readonly IBreakGuideAudioPlayer player;
    private readonly BreakGuideMode initialMode;
    private bool degradedEventFired;

    public BreakGuideAudioCoordinator(IBreakGuideAudioPlayer player, BreakGuideMode initialMode = BreakGuideMode.Chime)
    {
        ArgumentNullException.ThrowIfNull(player);
        this.player = player;
        this.initialMode = initialMode;
    }

    public BreakGuideMode CurrentMode { get; private set; } = BreakGuideMode.Chime;

    public bool IsDegraded { get; private set; }

    public event EventHandler<AudioFailureReason>? DegradedToVisual;

    public void BeginGuide(bool audioAllowed)
    {
        if (!audioAllowed)
        {
            CurrentMode = BreakGuideMode.VisualOnly;
            return;
        }

        CurrentMode = initialMode;

        if (!player.TryInitialize(out var failure))
        {
            Degrade(failure ?? AudioFailureReason.InitializationFailed);
        }
    }

    public void HandleCue(BreakGuideCue cue)
    {
        if (CurrentMode == BreakGuideMode.VisualOnly)
            return;

        if (!player.TryPlay(cue, CurrentMode, out var failure))
        {
            Degrade(failure ?? AudioFailureReason.PlaybackFailed);
        }
    }

    public void EndGuide()
    {
        try
        {
            player.Stop();
        }
        catch
        {
        }

        CurrentMode = initialMode;
        IsDegraded = false;
        degradedEventFired = false;
    }

    private void Degrade(AudioFailureReason reason)
    {
        CurrentMode = BreakGuideMode.VisualOnly;
        IsDegraded = true;

        try
        {
            player.Stop();
        }
        catch
        {
        }

        if (!degradedEventFired)
        {
            degradedEventFired = true;
            DegradedToVisual?.Invoke(this, reason);
        }
    }
}
