using Blanky.Core.Audio;

namespace Blanky.Core.Tests;

public class SampleRingBufferTests
{
    [Fact]
    public void Wraps_around_without_losing_or_reordering_samples()
    {
        var ring = new SampleRingBuffer(8);
        var output = new List<float>();
        var read = new float[5];
        float next = 0;
        for (int round = 0; round < 20; round++)
        {
            var chunk = Enumerable.Range(0, 5).Select(_ => next++).ToArray();
            int written = ring.Write(chunk);
            next -= chunk.Length - written;
            int got = ring.Read(read);
            output.AddRange(read[..got]);
        }
        output.AddRange(Drain(ring));

        Assert.Equal(Enumerable.Range(0, output.Count).Select(i => (float)i), output);
    }

    [Fact]
    public void Write_stops_at_capacity()
    {
        var ring = new SampleRingBuffer(4);
        Assert.Equal(4, ring.Write(new float[6]));
        Assert.Equal(0, ring.Free);
    }

    private static float[] Drain(SampleRingBuffer ring)
    {
        var rest = new float[ring.Count];
        ring.Read(rest);
        return rest;
    }
}

public class LoopingSourceTests
{
    [Fact]
    public void Butt_splice_repeats_the_file_exactly_and_measures_the_loop()
    {
        using var source = new LoopingSource(() => new RampDecoder(10));
        var buffer = new float[2 * 25];

        Assert.Equal(buffer.Length, source.Read(buffer));

        var left = buffer.Where((_, i) => i % 2 == 0).ToArray();
        Assert.Equal(Enumerable.Range(0, 25).Select(i => (float)(i % 10)), left);
        Assert.Equal(10, source.LoopFrames);
    }

    [Fact]
    public void Play_once_source_ends_after_one_pass()
    {
        using var source = new LoopingSource(() => new RampDecoder(10), loop: false);
        var buffer = new float[2 * 25];

        Assert.Equal(20, source.Read(buffer));
        Assert.True(source.IsEnded);
        Assert.Equal(0, source.Read(buffer));
    }

    [Fact]
    public void Seam_crossfade_blends_tail_into_head_and_shortens_each_pass()
    {
        const int frames = 100;
        const int seam = 10;
        using var source = new LoopingSource(() => new RampDecoder(frames), seamFrames: seam);
        var buffer = new float[2 * 300];
        source.Read(buffer);
        var left = buffer.Where((_, i) => i % 2 == 0).ToArray();

        // First pass: frames 0..89 untouched, then a 10-frame blend of 90..99 into 0..9.
        Assert.Equal(Enumerable.Range(0, 90).Select(i => (float)i), left[..90]);
        Assert.Equal(90, left[90], 3);
        Assert.Equal(9, left[99], 3);

        // Then the rest of the head continues seamlessly: 10, 11, ...
        Assert.Equal(10, left[100]);
        Assert.Equal(frames - seam, source.LoopFrames);
    }

    [Fact]
    public void An_empty_file_fails_instead_of_spinning()
    {
        using var looping = new LoopingSource(() => new RampDecoder(0));
        using var seamed = new LoopingSource(() => new RampDecoder(0), seamFrames: 10);

        Assert.Throws<InvalidDataException>(() => looping.Read(new float[64]));
        Assert.Throws<InvalidDataException>(() => seamed.Read(new float[64]));
    }

    [Fact]
    public void Seam_crossfade_survives_a_file_shorter_than_the_seam()
    {
        using var source = new LoopingSource(() => new RampDecoder(4), seamFrames: 10);
        var buffer = new float[2 * 50];

        Assert.Equal(buffer.Length, source.Read(buffer));
        Assert.All(buffer, s => Assert.InRange(s, 0, 3));
    }
}

public class MixerTests
{
    [Fact]
    public void Voice_fades_in_from_silence_and_holds_its_place_when_paused()
    {
        var mixer = new Mixer();
        var voice = NewVoice(amplitude: 0.5);
        mixer.Add(voice);
        voice.SetGain(1);
        voice.SnapGain();
        Fill(voice);

        voice.FadeTo(1, 0.01);
        var block = Render(mixer, 1200);
        Assert.True(Math.Abs(block[0]) < 0.05, "starts near silence");
        Assert.True(block.Skip(900).Max(Math.Abs) > 0.4, "reaches full level after the fade");

        voice.FadeTo(0, 0);
        double before = voice.LoopProgress;
        var silent = Render(mixer, 1200);
        Assert.All(silent, s => Assert.Equal(0, s));
        Assert.Equal(before, voice.LoopProgress);
        Assert.False(voice.IsAudible);
    }

    [Fact]
    public void Limiter_keeps_a_loud_sum_under_the_ceiling()
    {
        var mixer = new Mixer();
        for (int i = 0; i < 4; i++)
        {
            var voice = NewVoice(amplitude: 0.9);
            voice.SetGain(1);
            voice.SnapGain();
            Fill(voice);
            voice.FadeTo(1, 0);
            mixer.Add(voice);
        }

        var block = Render(mixer, 4800);
        Assert.All(block, s => Assert.InRange(s, -0.892, 0.892));
    }

    [Fact]
    public void Master_volume_scales_output()
    {
        var mixer = new Mixer { MasterVolume = 0 };
        var voice = NewVoice(amplitude: 0.5);
        voice.SetGain(1);
        voice.SnapGain();
        Fill(voice);
        voice.FadeTo(1, 0);
        mixer.Add(voice);

        // The master ramps from its initial 1.0, so skip the first 200 ms.
        Render(mixer, 9600);
        Assert.All(Render(mixer, 1200), s => Assert.True(Math.Abs(s) < 0.001));
    }

    private static Voice NewVoice(double amplitude) =>
        new("test", new LoopingSource(() => new SineDecoder(Mixer.SampleRate, Mixer.SampleRate, amplitude, 440)), Mixer.SampleRate);

    private static void Fill(Voice voice)
    {
        while (voice.Pump())
        {
        }
    }

    private static float[] Render(Mixer mixer, int frames)
    {
        var buffer = new float[frames * 2];
        mixer.Read(buffer);
        return buffer;
    }
}

public class LoudnessTests
{
    [Fact]
    public void Matches_EBU_Tech_3341_case_1()
    {
        // 1 kHz sine at -23 dBFS in both channels measures -23.0 LUFS (±0.1).
        double amplitude = Math.Pow(10, -23.0 / 20);
        using var decoder = new SineDecoder(20 * Mixer.SampleRate, Mixer.SampleRate, amplitude, 1000);

        double lufs = Loudness.MeasureIntegratedLufs(decoder, cancellationToken: TestContext.Current.CancellationToken);

        Assert.InRange(lufs, -23.1, -22.9);
    }

    [Fact]
    public void Silence_measures_as_negative_infinity()
    {
        using var decoder = new SineDecoder(5 * Mixer.SampleRate, Mixer.SampleRate, 0, 1000);
        Assert.True(double.IsNegativeInfinity(Loudness.MeasureIntegratedLufs(decoder, cancellationToken: TestContext.Current.CancellationToken)));
    }

    [Theory]
    [InlineData(-27, 1.0)]
    [InlineData(-21, 0.501187)]
    [InlineData(-33, 1.995262)]
    [InlineData(-55, 7.943282)] // boost capped at +18 dB
    [InlineData(-70, 1.0)] // too quiet to be signal: left alone
    public void Normalization_targets_minus_27_LUFS(double lufs, double expected)
    {
        Assert.Equal(expected, Loudness.NormalizationFactor(lufs), 5);
    }

    [Fact]
    public void Volume_curve_is_cubic()
    {
        Assert.Equal(0.125f, Loudness.VolumeCurve(0.5), 5);
        Assert.Equal(1f, Loudness.VolumeCurve(2));
        Assert.Equal(0f, Loudness.VolumeCurve(-1));
    }
}

public class AudioEngineTests
{
    [Fact]
    public async Task Starts_the_device_for_sound_and_releases_it_after_silence()
    {
        var output = new PullingOutput();
        await using var engine = new AudioEngine(new SineDecoderFactory(seconds: 2), output);

        engine.Play("rain", "rain.m4a", 1f, TimeSpan.Zero);
        await WaitFor(() => output.Started && output.Peak > 0.05);

        engine.Pause("rain", TimeSpan.Zero);
        await WaitFor(() => !engine.IsOutputRunning, TimeSpan.FromSeconds(5));
        Assert.True(output.Stopped);
        Assert.True(engine.IsLoaded("rain"), "a paused sound keeps its place");
    }

    [Fact]
    public async Task Play_once_voice_reports_finished()
    {
        var output = new PullingOutput();
        await using var engine = new AudioEngine(new SineDecoderFactory(seconds: 0.2), output);
        // Inline continuations on purpose: the test then disposes the engine from
        // whichever thread raised the event, which must not deadlock.
        var finished = new TaskCompletionSource<string>();
        engine.VoiceFinished += (_, key) => finished.TrySetResult(key);

        engine.Play("chime", "chime.wav", 1f, TimeSpan.Zero, new VoiceOptions(Loop: false));

        Assert.Equal("chime", await finished.Task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken));
        Assert.False(engine.IsLoaded("chime"));
    }

    private static async Task WaitFor(Func<bool> condition, TimeSpan? timeout = null)
    {
        var deadline = DateTime.UtcNow + (timeout ?? TimeSpan.FromSeconds(3));
        while (!condition())
        {
            Assert.True(DateTime.UtcNow < deadline, "timed out");
            await Task.Delay(20, TestContext.Current.CancellationToken);
        }
    }

    /// <summary>Pulls audio on a background loop like a real device would.</summary>
    private sealed class PullingOutput : IAudioOutput
    {
        private CancellationTokenSource? _cts;
        private volatile float _peak;

        public bool Started { get; private set; }

        public bool Stopped { get; private set; }

        public float Peak => _peak;

        public event EventHandler<Exception?>? Faulted
        {
            add { }
            remove { }
        }

        public Task StartAsync(NAudio.Wave.ISampleProvider source)
        {
            Started = true;
            _cts = new CancellationTokenSource();
            var token = _cts.Token;
            _ = Task.Run(async () =>
            {
                var buffer = new float[960];
                while (!token.IsCancellationRequested)
                {
                    source.Read(buffer);
                    _peak = Math.Max(_peak, buffer.Max(Math.Abs));
                    await Task.Delay(5, CancellationToken.None);
                }
            }, token);
            return Task.CompletedTask;
        }

        public Task StopAsync()
        {
            Stopped = true;
            _cts?.Cancel();
            return Task.CompletedTask;
        }

        public ValueTask DisposeAsync()
        {
            _cts?.Cancel();
            return ValueTask.CompletedTask;
        }
    }
}
