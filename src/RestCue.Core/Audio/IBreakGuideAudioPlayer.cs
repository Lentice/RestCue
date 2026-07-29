using RestCue.Core.Reminders;

namespace RestCue.Core.Audio;

public interface IBreakGuideAudioPlayer : IDisposable
{
    bool TryInitialize(out AudioFailureReason? failure);
    bool TryPlay(BreakGuideCue cue, BreakGuideMode mode, out AudioFailureReason? failure);
    void Stop();
}
