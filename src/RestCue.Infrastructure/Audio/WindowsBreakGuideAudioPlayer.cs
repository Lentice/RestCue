using System.IO;
using System.Media;
using System.Speech.Synthesis;
using RestCue.Core.Audio;
using RestCue.Core.Reminders;

namespace RestCue.Infrastructure.Audio;

public sealed class WindowsBreakGuideAudioPlayer : IBreakGuideAudioPlayer
{
    private SoundPlayer? chimePlayer;
    private SpeechSynthesizer? synthesizer;
    private bool disposed;

    public bool TryInitialize(out AudioFailureReason? failure)
    {
        failure = null;
        return true;
    }

    public bool TryPlay(BreakGuideCue cue, BreakGuideMode mode, out AudioFailureReason? failure)
    {
        failure = null;
        try
        {
            if (mode == BreakGuideMode.Chime)
            {
                PlayChime(cue);
            }
            else if (mode == BreakGuideMode.Speech)
            {
                PlaySpeech(cue);
            }
            return true;
        }
        catch
        {
            failure = AudioFailureReason.PlaybackFailed;
            return false;
        }
    }

    public void Stop()
    {
        try
        {
            chimePlayer?.Stop();
        }
        catch
        {
        }

        try
        {
            synthesizer?.SpeakAsyncCancelAll();
        }
        catch
        {
        }
    }

    public void Dispose()
    {
        if (disposed) return;
        disposed = true;

        try
        {
            chimePlayer?.Dispose();
        }
        catch
        {
        }

        try
        {
            synthesizer?.Dispose();
        }
        catch
        {
        }
    }

    private void PlayChime(BreakGuideCue cue)
    {
        int frequency = cue switch
        {
            BreakGuideCue.Start => 440,
            BreakGuideCue.Middle => 330,
            BreakGuideCue.End => 660,
            _ => 440
        };

        var stream = GenerateToneWav(frequency, 200);
        chimePlayer?.Dispose();
        chimePlayer = new SoundPlayer(stream);
        chimePlayer.Play();
    }

    private void PlaySpeech(BreakGuideCue cue)
    {
        synthesizer ??= new SpeechSynthesizer();
        var text = GetSpeechText(cue);
        synthesizer.Speak(text);
    }

    public static string GetSpeechText(BreakGuideCue cue) => cue switch
    {
        BreakGuideCue.Start => "看向約六公尺外",
        BreakGuideCue.Middle => "慢慢眨眼、放鬆肩膀",
        BreakGuideCue.End => "休息結束",
        _ => ""
    };

    internal static MemoryStream GenerateToneWav(int frequency, int durationMs, int sampleRate = 44100)
    {
        int sampleCount = sampleRate * durationMs / 1000;
        var stream = new MemoryStream(44 + sampleCount * 2);
        using var writer = new BinaryWriter(stream, System.Text.Encoding.UTF8, leaveOpen: true);

        byte[] riff = "RIFF"u8.ToArray();
        writer.Write(riff);
        writer.Write(36 + sampleCount * 2);
        byte[] wave = "WAVE"u8.ToArray();
        writer.Write(wave);
        byte[] fmt = "fmt "u8.ToArray();
        writer.Write(fmt);
        writer.Write(16);
        writer.Write((short)1);
        writer.Write((short)1);
        writer.Write(sampleRate);
        writer.Write(sampleRate * 2);
        writer.Write((short)2);
        writer.Write((short)16);
        byte[] data = "data"u8.ToArray();
        writer.Write(data);
        writer.Write(sampleCount * 2);

        double ampl = 0.3;
        for (int i = 0; i < sampleCount; i++)
        {
            double t = (double)i / sampleRate;
            short sample = (short)(ampl * short.MaxValue * Math.Sin(2 * Math.PI * frequency * t));
            writer.Write(sample);
        }

        writer.Flush();
        stream.Position = 0;
        return stream;
    }
}
