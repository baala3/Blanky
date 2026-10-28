using Blanky.Core.Audio;

namespace Blanky.Core.Playback;

/// <summary>What <see cref="AudioManager"/> needs from the audio engine. Lets the playback rules be tested without audio.</summary>
public interface ISoundPlayer
{
    float MasterVolume { get; set; }

    event EventHandler<string>? VoiceFinished;

    event EventHandler<VoiceFailedEventArgs>? VoiceFailed;

    void Play(string key, string path, float gain, TimeSpan fade, VoiceOptions options = default);

    void Pause(string key, TimeSpan fade);

    void Stop(string key, TimeSpan fade);

    void SetGain(string key, float gain);

    double? GetLoopProgress(string key);
}
