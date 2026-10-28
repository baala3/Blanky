using Blanky.Core.Model;
using Blanky.Core.Playback;

namespace Blanky.Core.Tests;

public sealed class AudioManagerTests : IDisposable
{
    private readonly TestApp _app = new();

    private AudioManager Audio => _app.Audio;

    public AudioManagerTests() => Audio.Restore();

    [Fact]
    public void Starts_on_the_all_sounds_preset_with_every_sound_visible_and_nothing_playing()
    {
        Assert.Equal(PlaybackMode.Preset, Audio.Mode);
        Assert.True(Audio.CurrentPreset!.IsDefault);
        Assert.Equal(24, Audio.VisibleSounds.Count);
        Assert.False(Audio.IsPlaying);
        Assert.Empty(_app.Player.State);
    }

    [Fact]
    public void Tapping_a_sound_while_paused_selects_it_and_starts_playback()
    {
        Audio.ToggleOrResume(_app.Sound("rain"));

        Assert.True(Audio.IsPlaying);
        Assert.True(_app.Sound("rain").IsPlaying);
        Assert.Equal(["rain"], _app.Player.Playing);
    }

    [Fact]
    public void Tapping_while_playing_toggles_sounds_and_the_last_one_out_pauses()
    {
        Audio.ToggleOrResume(_app.Sound("rain"));
        Audio.ToggleOrResume(_app.Sound("waves"));
        Assert.Equal(["rain", "waves"], _app.Player.Playing);

        Audio.ToggleOrResume(_app.Sound("rain"));
        Assert.Equal(["waves"], _app.Player.Playing);

        Audio.ToggleOrResume(_app.Sound("waves"));
        Assert.False(Audio.IsPlaying);
    }

    [Fact]
    public void Pause_keeps_the_selection_and_resume_brings_it_back()
    {
        Audio.ToggleOrResume(_app.Sound("rain"));
        Audio.ToggleOrResume(_app.Sound("fireplace"));

        Audio.TogglePlayback();
        Assert.Empty(_app.Player.Playing);
        Assert.True(_app.Sound("rain").IsSelected);

        Audio.TogglePlayback();
        Assert.Equal(["fireplace", "rain"], _app.Player.Playing);
    }

    [Fact]
    public void Only_one_music_track_plays_at_a_time()
    {
        Audio.ToggleOrResume(_app.Sound("rain"));
        Audio.ToggleOrResume(_app.Sound("lofi-beats"));
        Audio.ToggleOrResume(_app.Sound("warm-piano"));

        Assert.Equal(["rain", "warm-piano"], _app.Player.Playing);
        Assert.False(_app.Sound("lofi-beats").IsSelected);
    }

    [Fact]
    public void Volume_uses_the_cubic_curve_and_loudness_correction()
    {
        var rain = _app.Sound("rain");
        Audio.ToggleOrResume(rain);

        rain.Volume = 0.5f;

        Assert.Equal(0.125f * rain.MeasuredNormalization, _app.Player.Gains["rain"], 4);
    }

    [Fact]
    public void Changes_to_the_all_sounds_preset_are_saved_after_a_short_delay()
    {
        Audio.ToggleOrResume(_app.Sound("rain"));
        _app.Sound("rain").Volume = 0.3f;

        _app.Time.Advance(TimeSpan.FromSeconds(1));

        var saved = _app.Store.LoadPresets().Single(p => p.IsDefault).SoundStates.Single(s => s.FileName == "rain");
        Assert.True(saved.IsSelected);
        Assert.Equal(0.3f, saved.Volume);
    }

    [Fact]
    public void A_preset_shows_only_its_sounds_and_switching_presets_swaps_the_mix()
    {
        Audio.ToggleOrResume(_app.Sound("rain"));
        var preset = Audio.CreatePreset("Cozy", ["fireplace", "coffee-shop"]);

        Assert.Equal(preset.Id, Audio.CurrentPreset!.Id);
        Assert.Equal(["fireplace", "coffee-shop"], Audio.VisibleSounds.Select(s => s.FileName));
        Assert.Equal(["coffee-shop", "fireplace"], _app.Player.Playing);
        Assert.False(_app.Sound("rain").IsSelected);

        Audio.ApplyPreset(Audio.Presets.Default);
        Assert.Equal(["rain"], _app.Player.Playing);
    }

    [Fact]
    public void Opening_all_sounds_never_starts_playback_and_pauses_what_was_playing()
    {
        Audio.ToggleOrResume(_app.Sound("rain"));
        var cozy = Audio.CreatePreset("Cozy", ["fireplace"]);
        Audio.ApplyPreset(cozy);
        Assert.Equal(["fireplace"], _app.Player.Playing);

        Audio.Activate(new PlayableItem.AllSounds());

        Assert.True(Audio.CurrentPreset!.IsDefault);
        Assert.False(Audio.IsPlaying);
        Assert.Empty(_app.Player.Playing);
        Assert.True(_app.Sound("rain").IsSelected, "the saved selection is kept for the play button");

        Audio.TogglePlayback();
        Assert.Equal(["rain"], _app.Player.Playing);
    }

    [Fact]
    public void Edits_while_a_preset_is_active_save_into_that_preset_only()
    {
        var preset = Audio.CreatePreset("Focus", ["white-noise", "rain"]);
        Audio.ApplyPreset(preset);
        Audio.ToggleOrResume(_app.Sound("rain"));
        Audio.FlushPresetSave();

        var saved = _app.Presets.Find(preset.Id)!;
        Assert.False(saved.SoundStates.Single(s => s.FileName == "rain").IsSelected);
        Assert.DoesNotContain(_app.Presets.Default.SoundStates, s => s.FileName == "white-noise" && s.IsSelected);
    }

    [Fact]
    public void Single_sound_plays_alone_without_touching_presets_and_returns_to_the_mix()
    {
        Audio.ToggleOrResume(_app.Sound("rain"));
        Audio.ToggleOrResume(_app.Sound("waves"));
        Audio.FlushPresetSave();

        Audio.PlaySingleSound(_app.Sound("train"));
        Assert.Equal(PlaybackMode.SingleSound, Audio.Mode);
        Assert.Equal(["train"], _app.Player.Playing);
        Assert.Equal("Train", Audio.NowPlayingTitle);

        Audio.ReturnToPreset();
        Assert.Equal(PlaybackMode.Preset, Audio.Mode);
        Assert.Equal(["rain", "waves"], _app.Player.Playing);
    }

    [Fact]
    public void Quick_mix_starts_empty_at_80_percent_and_restores_the_preset_after()
    {
        Audio.ToggleOrResume(_app.Sound("storm"));

        Audio.EnterQuickMix();
        Assert.Equal(AppSettings.DefaultQuickMixSounds, Audio.VisibleSounds.Select(s => s.FileName));
        Assert.All(Audio.VisibleSounds, s => Assert.Equal(AudioManager.QuickMixVolume, s.Volume));
        Assert.False(Audio.IsPlaying);

        Audio.ToggleOrResume(_app.Sound("birds"));
        Assert.Equal(["birds"], _app.Player.Playing);

        Audio.ReturnToPreset();
        Assert.Equal(["storm"], _app.Player.Playing);
        Assert.DoesNotContain(_app.Presets.Default.SoundStates, s => s.FileName == "birds" && s.IsSelected);
    }

    [Fact]
    public void Favorites_cycle_in_starred_order()
    {
        var cozy = Audio.CreatePreset("Cozy", ["fireplace"]);
        Audio.SetStarred(new PlayableItem.AllSounds(), true);
        Audio.SetStarred(new PlayableItem.PresetItem(cozy.Id), true);
        Audio.SetStarred(new PlayableItem.SingleSound("rain"), true);
        Audio.ApplyPreset(_app.Presets.Default);

        Audio.NextFavorite();
        Assert.Equal(cozy.Id, Audio.CurrentPreset!.Id);
        Audio.NextFavorite();
        Assert.Equal("rain", Audio.SingleSound?.FileName);
        Audio.NextFavorite();
        Assert.True(Audio.CurrentPreset!.IsDefault);
        Audio.PreviousFavorite();
        Assert.Equal("rain", Audio.SingleSound?.FileName);
    }

    [Fact]
    public void Sleep_timer_pauses_playback_when_it_expires()
    {
        Audio.ToggleOrResume(_app.Sound("rain"));
        Audio.SleepTimer.Start(TimeSpan.FromMinutes(30));

        _app.Time.Advance(TimeSpan.FromMinutes(29));
        Assert.True(Audio.IsPlaying);
        Assert.Equal("1:00", SleepTimer.Format(Audio.SleepTimer.Remaining));

        _app.Time.Advance(TimeSpan.FromMinutes(1));
        Assert.False(Audio.IsPlaying);
        Assert.False(Audio.SleepTimer.IsActive);
        Assert.True(_app.Sound("rain").IsSelected);
    }

    [Fact]
    public void Deleting_the_active_preset_falls_back_to_all_sounds_and_unstars_it()
    {
        var preset = Audio.CreatePreset("Gone", ["rain"]);
        Audio.SetStarred(new PlayableItem.PresetItem(preset.Id), true);

        Audio.DeletePreset(preset);

        Assert.True(Audio.CurrentPreset!.IsDefault);
        Assert.Empty(Audio.Favorites);
        Assert.Null(_app.Presets.Find(preset.Id));
    }

    [Fact]
    public void A_play_once_sound_leaves_the_mix_when_it_ends()
    {
        var chime = _app.Sound("rain");
        _app.Library.Customize(chime, c => c with { Loop = false });
        Audio.ToggleOrResume(chime);

        _app.Player.Finish("rain");

        Assert.False(chime.IsSelected);
        Assert.False(Audio.IsPlaying);
    }

    [Fact]
    public void A_sound_that_fails_to_decode_is_dropped_and_reported()
    {
        string? failed = null;
        Audio.SoundFailed += (_, e) => failed = e.Key;
        Audio.ToggleOrResume(_app.Sound("rain"));

        _app.Player.Fail("rain");

        Assert.Equal("rain", failed);
        Assert.False(_app.Sound("rain").IsSelected);
    }

    [Fact]
    public void Hiding_a_sound_removes_it_from_the_all_sounds_grid()
    {
        _app.Library.Customize(_app.Sound("train"), c => c with { IsHidden = true });

        Assert.DoesNotContain(Audio.VisibleSounds, s => s.FileName == "train");
        Assert.Equal(23, Audio.VisibleSounds.Count);
    }

    [Fact]
    public void Restore_brings_back_the_last_preset_and_respects_auto_play()
    {
        var preset = Audio.CreatePreset("Night", ["rain", "summer-night"]);
        Audio.ApplyPreset(preset);
        _app.Settings.Update(s => s with { AutoPlayOnLaunch = true });
        _app.Settings.Flush();

        using var relaunched = new TestApp(reuseRoot: true, root: _app.Root);
        relaunched.Audio.Restore();

        Assert.Equal("Night", relaunched.Audio.CurrentPreset!.Name);
        Assert.True(relaunched.Audio.IsPlaying);
        Assert.Equal(["rain", "summer-night"], relaunched.Player.Playing);
    }

    public void Dispose()
    {
        _app.Dispose();
        _app.DeleteRoot();
    }
}
