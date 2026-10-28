using System.ComponentModel;
using Blanky.Core;
using Blanky.Core.Playback;
using Blanky.Core.Presets;
using Blanky.Services;
using Microsoft.UI;
using Microsoft.UI.Input;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Windows.Graphics;
using Windows.System;
using Windows.UI;

namespace Blanky.Views;

public sealed partial class MainWindow : Window
{
    private const double VolumeStep = 1.0 / 16; // Matches the Windows hardware volume step.

    private bool _closing;

    public MainWindow()
    {
        Audio = App.Current.Audio;
        Sidebar = new SidebarModel(Audio, App.Current.Settings);
        InitializeComponent();

        Title = AppInfo.Name;
        ConfigureWindow();
        ApplyTheme();
        ApplyAccent();

        Audio.PropertyChanged += OnAudioChanged;
        App.Current.Settings.Changed += (_, e) =>
        {
            if (e.Old.Theme != e.Current.Theme)
            {
                ApplyTheme();
            }
            if (e.Old.AccentColorName != e.Current.AccentColorName)
            {
                ApplyAccent();
            }
        };
        Audio.SoundFailed += async (_, e) => await ShowMessageAsync("Couldn't play a sound", $"{Audio.Library.Find(e.Key)?.Title ?? e.Key} couldn't be played: {e.Error.Message}");
        Root.ActualThemeChanged += (_, _) => UpdateGlow();
        Root.PreviewKeyDown += OnPreviewKeyDown;
        Root.Loaded += OnLoaded;
        Sidebar.Rebuilt += (_, _) => UpdateSectionHeaders();
        UpdateSectionHeaders();
        TimerPanel.Done += (_, _) => TimerFlyout.Hide();
        AccentPicker.AccentChosen += (_, name) =>
        {
            App.Current.Settings.Update(s => s with { AccentColorName = name });
            AccentFlyout.Hide();
        };
        SetSidebarVisible(App.Current.Settings.Current.SidebarVisible);
    }

    internal AudioManager Audio { get; }

    internal SidebarModel Sidebar { get; }

    internal Updater Updater { get; } = App.Current.Updater;

    public void BringToFront()
    {
        if (AppWindow.Presenter is OverlappedPresenter { State: OverlappedPresenterState.Minimized } presenter)
        {
            presenter.Restore();
        }
        AppWindow.Show();
        Activate();
    }

    // ---- Window chrome -----------------------------------------------------

    private void ConfigureWindow()
    {
        ExtendsContentIntoTitleBar = true;
        AppWindow.SetIcon(Path.Combine(AppContext.BaseDirectory, "Assets", "Blanky.ico"));
        AppWindow.TitleBar.PreferredHeightOption = TitleBarHeightOption.Tall;
        AppWindow.TitleBar.ButtonBackgroundColor = Colors.Transparent;
        AppWindow.TitleBar.ButtonInactiveBackgroundColor = Colors.Transparent;

        double scale = DisplayScale();
        AppWindow.Resize(new SizeInt32((int)(1240 * scale), (int)(820 * scale)));
        if (AppWindow.Presenter is OverlappedPresenter presenter)
        {
            presenter.PreferredMinimumWidth = (int)(720 * scale);
            presenter.PreferredMinimumHeight = (int)(520 * scale);
        }
        CenterOnScreen();

        // AppWindow.Closing covers the user closing the window; Closed covers Close()
        // from code, which doesn't raise Closing.
        AppWindow.Closing += (_, _) => ShutDownOnce();
        Closed += (_, _) => ShutDownOnce();
        Root.SizeChanged += (_, _) => ScheduleDragRegionUpdate();

        // Title-bar buttons come and go (Back, Save as Preset); keep them clickable.
        foreach (var button in InteractiveTitleBarElements())
        {
            button.RegisterPropertyChangedCallback(UIElement.VisibilityProperty, (_, _) => ScheduleDragRegionUpdate());
            button.SizeChanged += (_, _) => ScheduleDragRegionUpdate();
        }
    }

    private double DisplayScale()
    {
        var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(this);
        return NativeMethods.GetDpiForWindow(hwnd) / 96.0;
    }

    private void CenterOnScreen()
    {
        var area = DisplayArea.GetFromWindowId(AppWindow.Id, DisplayAreaFallback.Primary).WorkArea;
        AppWindow.Move(new PointInt32(area.X + ((area.Width - AppWindow.Size.Width) / 2), area.Y + ((area.Height - AppWindow.Size.Height) / 2)));
    }

    // The top 48 px is the drag area, except where buttons sit.
    private bool _dragRegionUpdatePending;

    // Region changes are applied after layout, at most once per batch: setting them
    // from inside a layout pass (SizeChanged) crashes in the native title-bar code.
    private void ScheduleDragRegionUpdate()
    {
        if (_dragRegionUpdatePending || _closing)
        {
            return;
        }
        _dragRegionUpdatePending = true;
        DispatcherQueue.TryEnqueue(Microsoft.UI.Dispatching.DispatcherQueuePriority.Low, () =>
        {
            _dragRegionUpdatePending = false;
            UpdateDragRegions();
        });
    }

    private void UpdateDragRegions()
    {
        // Layout callbacks still arrive while the window tears down; its title bar is gone by then.
        if (_closing || Root.XamlRoot is null)
        {
            return;
        }
        double scale = Root.XamlRoot.RasterizationScale;
        var source = InputNonClientPointerSource.GetForWindowId(AppWindow.Id);
        var caption = new RectInt32(0, 0, (int)(Root.ActualWidth * scale), (int)(48 * scale));
        source.SetRegionRects(NonClientRegionKind.Caption, [caption]);

        var passthrough = new List<RectInt32>();
        foreach (var element in InteractiveTitleBarElements())
        {
            if (element.Visibility != Visibility.Visible || element.ActualWidth == 0)
            {
                continue;
            }
            var bounds = element.TransformToVisual(Root).TransformBounds(new Windows.Foundation.Rect(0, 0, element.ActualWidth, element.ActualHeight));
            passthrough.Add(new RectInt32((int)(bounds.X * scale), (int)(bounds.Y * scale), (int)(bounds.Width * scale), (int)(bounds.Height * scale)));
        }
        source.SetRegionRects(NonClientRegionKind.Passthrough, [.. passthrough]);
    }

    private IEnumerable<FrameworkElement> InteractiveTitleBarElements()
    {
        foreach (var button in SidebarHeader.Children.OfType<Button>())
        {
            yield return button;
        }
        foreach (var button in Header.Children.OfType<Button>())
        {
            yield return button;
        }
    }

    // ---- Theme -------------------------------------------------------------

    private void ApplyTheme()
    {
        Root.RequestedTheme = ThemeManager.ToElementTheme(App.Current.Settings.Current.Theme);
        UpdateCaptionButtonColors();
    }

    private void ApplyAccent()
    {
        App.Current.Theme.ApplyAccent(Audio.AccentColorName ?? App.Current.Settings.Current.AccentColorName);
        UpdateGlow();
    }

    private void UpdateGlow()
    {
        bool dark = Root.ActualTheme == ElementTheme.Dark;
        var accent = App.Current.Theme.CurrentAccent(dark);
        Glow.Fill = new RadialGradientBrush
        {
            Center = new Windows.Foundation.Point(0.62, 0.0),
            GradientOrigin = new Windows.Foundation.Point(0.62, 0.0),
            RadiusX = 0.75,
            RadiusY = 0.9,
            GradientStops =
            {
                new GradientStop { Color = Color.FromArgb(dark ? (byte)0x3A : (byte)0x26, accent.R, accent.G, accent.B), Offset = 0 },
                new GradientStop { Color = Color.FromArgb(0, accent.R, accent.G, accent.B), Offset = 1 },
            },
        };
        UpdateCaptionButtonColors();
    }

    private void UpdateCaptionButtonColors()
    {
        bool dark = Root.ActualTheme == ElementTheme.Dark;
        var foreground = dark ? Colors.White : Colors.Black;
        AppWindow.TitleBar.ButtonForegroundColor = foreground;
        AppWindow.TitleBar.ButtonHoverForegroundColor = foreground;
        AppWindow.TitleBar.ButtonHoverBackgroundColor = Color.FromArgb(0x18, foreground.R, foreground.G, foreground.B);
        AppWindow.TitleBar.ButtonPressedBackgroundColor = Color.FromArgb(0x10, foreground.R, foreground.G, foreground.B);
    }

    private void OnAudioChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(AudioManager.AccentColorName))
        {
            ApplyAccent();
        }
        else if (e.PropertyName == nameof(AudioManager.Mode))
        {
            ScheduleDragRegionUpdate();
        }
    }

    // ---- Lifecycle ---------------------------------------------------------

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        ScheduleDragRegionUpdate();

        // Otherwise WinUI focuses the first button (+) and draws a focus ring on
        // it at launch. Programmatic focus moves keyboard focus without the ring.
        if (!PlayButton.Focus(FocusState.Programmatic))
        {
            VolumeSlider.Focus(FocusState.Programmatic);
        }
        if (App.Current.CapturePath is { } path)
        {
            // Scripted screenshots (scripts/screenshots.py) can open a panel first.
            if (App.Current.ArgValue("--demo-timer") is { } minutes)
            {
                Audio.SleepTimer.Start(TimeSpan.FromMinutes(double.Parse(minutes, System.Globalization.CultureInfo.InvariantCulture)));
                TimerPanel.Refresh();
                TimerFlyout.ShowAt(TimerButton);
            }
            else if (App.Current.ArgValue("--open") == "accent")
            {
                AccentFlyout.ShowAt(AccentButton);
            }

            // Let fades, progress rings and Mica settle before the screenshot.
            await Task.Delay(1500);
            await WindowCapture.SaveAsync(this, path);
            Close();
        }
        else if (App.Current.ExitAfter is { } delay)
        {
            await Task.Delay(delay);
            Close();
        }
    }

    // Clean up synchronously and let the window close normally; the app exits when
    // its last window does. (Cancelling and calling Exit from an async continuation
    // raced with XAML teardown and crashed.)
    private void ShutDownOnce()
    {
        if (_closing)
        {
            return;
        }
        _closing = true;
        Audio.PropertyChanged -= OnAudioChanged;
        App.Current.Shutdown();
    }

    // ---- Playback controls -------------------------------------------------

    private void OnPlayPause(object sender, RoutedEventArgs e) => Audio.TogglePlayback();

    private void OnBackToPreset(object sender, RoutedEventArgs e) => Audio.ReturnToPreset();

    private void OnQuickMix(object sender, RoutedEventArgs e) => Audio.EnterQuickMix();

    // Closing saves the mix as usual; Velopack swaps in the update and relaunches.
    private void OnRestartToUpdate(object sender, RoutedEventArgs e)
    {
        Updater.PrepareRestart();
        Close();
    }

    private void OnTimerFlyoutOpening(object sender, object e) => TimerPanel.Refresh();

    private void OnPreviewKeyDown(object sender, KeyRoutedEventArgs e)
    {
        bool ctrl = InputKeyboardSource.GetKeyStateForCurrentThread(VirtualKey.Control).HasFlag(Windows.UI.Core.CoreVirtualKeyStates.Down);
        var focused = FocusManager.GetFocusedElement(Root.XamlRoot);
        switch (e.Key)
        {
            // Space follows the media-app convention, unless a control that uses Space has focus.
            case VirtualKey.Space when !ctrl && focused is not (TextBox or NumberBox or Button or Slider or ToggleSwitch or CheckBox):
                Audio.TogglePlayback();
                e.Handled = true;
                break;
            case VirtualKey.Up when ctrl:
                Audio.Volume += VolumeStep;
                e.Handled = true;
                break;
            case VirtualKey.Down when ctrl:
                Audio.Volume -= VolumeStep;
                e.Handled = true;
                break;
            case VirtualKey.Right when ctrl:
                Audio.NextFavorite();
                e.Handled = true;
                break;
            case VirtualKey.Left when ctrl:
                Audio.PreviousFavorite();
                e.Handled = true;
                break;
            case VirtualKey.S when ctrl:
                OnToggleSidebar(this, new RoutedEventArgs());
                e.Handled = true;
                break;
            case VirtualKey.N when ctrl:
                OnNewPreset(this, new RoutedEventArgs());
                e.Handled = true;
                break;
            case VirtualKey.I when ctrl:
                OnImport(this, new RoutedEventArgs());
                e.Handled = true;
                break;
        }
    }

    // ---- Sidebar -------------------------------------------------------------

    private void OnToggleSidebar(object sender, RoutedEventArgs e)
    {
        bool visible = SidebarColumn.Width.Value == 0;
        SetSidebarVisible(visible);
        App.Current.Settings.Update(s => s with { SidebarVisible = visible });
    }

    private void SetSidebarVisible(bool visible)
    {
        SidebarColumn.Width = new GridLength(visible ? 296 : 0);
        SidebarPane.Visibility = visible ? Visibility.Visible : Visibility.Collapsed;
        ShowSidebarButton.Visibility = visible ? Visibility.Collapsed : Visibility.Visible;
        ScheduleDragRegionUpdate();
    }

    // Empty sections lose their headings, so the sidebar never shows a bare title.
    private void UpdateSectionHeaders()
    {
        FavoritesHeader.Visibility = Sidebar.Favorites.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        PresetsHeader.Visibility = Sidebar.Presets.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    private void OnSidebarRowClick(object sender, RoutedEventArgs e)
    {
        if (((FrameworkElement)sender).Tag is SidebarRow row)
        {
            Audio.Activate(row.Item);
        }
    }

    private void OnStarClick(object sender, RoutedEventArgs e)
    {
        if (((FrameworkElement)sender).Tag is SidebarRow row)
        {
            Audio.SetStarred(row.Item, !row.IsStarred);
        }
    }

    private void OnSidebarRowRightTapped(object sender, RightTappedRoutedEventArgs e)
    {
        if (((FrameworkElement)sender).Tag is not SidebarRow row)
        {
            return;
        }
        var menu = new MenuFlyout();
        void Add(string text, string glyph, Action action)
        {
            var item = new MenuFlyoutItem { Text = text, Icon = new FontIcon { Glyph = glyph } };
            item.Click += (_, _) => action();
            menu.Items.Add(item);
        }

        Add(row.IsStarred ? "Remove from Favorites" : "Add to Favorites", row.IsStarred ? "\uE735" : "\uE734", () => Audio.SetStarred(row.Item, !row.IsStarred));
        if (row.Item is PlayableItem.PresetItem { Id: var id } && Audio.Presets.Find(id) is { } preset)
        {
            Add("Rename Preset…", "\uE8AC", async () => await RenamePresetAsync(preset));
            menu.Items.Add(AccentSubmenu(preset));
            Add("Export Preset…", "\uEDE1", async () => await ExportPresetAsync(preset));
            menu.Items.Add(new MenuFlyoutSeparator());
            Add("Delete Preset…", "\uE74D", async () => await DeletePresetAsync(preset));
        }
        else if (row.Item is PlayableItem.SingleSound { FileName: var file } && Audio.Library.Find(file) is { IsCustom: true } sound)
        {
            menu.Items.Add(new MenuFlyoutSeparator());
            Add("Delete Sound…", "\uE74D", async () => await DeleteSoundAsync(sound));
        }
        menu.ShowAt((FrameworkElement)sender, e.GetPosition((FrameworkElement)sender));
        e.Handled = true;
    }

    /// <summary>"Accent Color" submenu: a preset can override the app accent while it's active.</summary>
    private MenuFlyoutSubItem AccentSubmenu(Core.Model.Preset preset)
    {
        var submenu = new MenuFlyoutSubItem { Text = "Accent Color", Icon = new FontIcon { Glyph = "\uE790" } };
        void Add(string? name, string label, Color? swatch)
        {
            var item = new ToggleMenuFlyoutItem { Text = label, IsChecked = preset.AccentColorName == name };
            if (swatch is { } color)
            {
                item.Icon = new FontIcon { Glyph = "\uEA3B", Foreground = new SolidColorBrush(color) };
            }
            item.Click += (_, _) =>
            {
                if (Audio.Presets.Find(preset.Id) is { } current)
                {
                    Audio.UpdatePreset(current with { AccentColorName = name });
                }
            };
            submenu.Items.Add(item);
        }

        Add(null, "Use App Accent", null);
        submenu.Items.Add(new MenuFlyoutSeparator());
        bool dark = Root.ActualTheme == ElementTheme.Dark;
        foreach (var accent in Core.Model.AccentColors.All)
        {
            var rgb = dark ? accent.Dark : accent.Light;
            Add(accent.Name, accent.DisplayName, Color.FromArgb(255, rgb.R, rgb.G, rgb.B));
        }
        return submenu;
    }

    // ---- Presets and import -------------------------------------------------

    private async void OnNewPreset(object sender, RoutedEventArgs e)
    {
        var name = new TextBox { Header = "Name", PlaceholderText = "My Preset" };
        var sounds = new ListView
        {
            SelectionMode = ListViewSelectionMode.Multiple,
            ItemsSource = Audio.Library.Sounds.Where(s => !s.IsHidden).ToList(),
            DisplayMemberPath = nameof(SoundItem.Title),
            Height = 300,
        };
        var dialog = new ContentDialog
        {
            XamlRoot = Root.XamlRoot,
            Title = "New Preset",
            PrimaryButtonText = "Create",
            CloseButtonText = "Cancel",
            DefaultButton = ContentDialogButton.Primary,
            Content = new StackPanel
            {
                Spacing = 12,
                Width = 360,
                Children = { name, new TextBlock { Text = "Sounds", Style = (Style)Application.Current.Resources["BodyStrongTextBlockStyle"] }, sounds },
            },
        };
        sounds.Loaded += (_, _) =>
        {
            foreach (var sound in Audio.VisibleSounds.Where(s => s.IsSelected))
            {
                sounds.SelectedItems.Add(sound);
            }
        };
        if (await dialog.ShowAsync() != ContentDialogResult.Primary)
        {
            return;
        }
        var chosen = sounds.SelectedItems.OfType<SoundItem>().Select(s => s.FileName).ToList();
        if (chosen.Count == 0)
        {
            await ShowMessageAsync("No sounds chosen", "Pick at least one sound for the preset.");
            return;
        }
        Audio.CreatePreset(string.IsNullOrWhiteSpace(name.Text) ? "New Preset" : name.Text, chosen);
    }

    private async void OnImport(object sender, RoutedEventArgs e)
    {
        var picker = new Windows.Storage.Pickers.FileOpenPicker { ViewMode = Windows.Storage.Pickers.PickerViewMode.List };
        foreach (var extension in SoundLibrary.ImportExtensions.Append(AppInfo.PresetExtension).Append(AppInfo.BlankiePresetExtension))
        {
            picker.FileTypeFilter.Add(extension);
        }
        WinRT.Interop.InitializeWithWindow.Initialize(picker, WinRT.Interop.WindowNative.GetWindowHandle(this));
        var files = await picker.PickMultipleFilesAsync();
        foreach (var file in files)
        {
            try
            {
                var extension = Path.GetExtension(file.Path).ToLowerInvariant();
                if (extension is AppInfo.PresetExtension or AppInfo.BlankiePresetExtension)
                {
                    var preset = await new PresetArchive(Audio, App.Current.Paths).ImportAsync(file.Path);
                    Audio.ApplyPreset(preset);
                }
                else
                {
                    await Audio.Library.ImportAsync(file.Path);
                }
            }
            catch (Exception ex) when (ex is IOException or InvalidDataException or NotSupportedException or UnauthorizedAccessException or System.Runtime.InteropServices.COMException)
            {
                await ShowMessageAsync($"Couldn't import {file.Name}", ex.Message);
            }
        }
    }

    private async Task RenamePresetAsync(Core.Model.Preset preset)
    {
        var box = new TextBox { Text = preset.Name, SelectionStart = 0, SelectionLength = preset.Name.Length };
        var dialog = new ContentDialog
        {
            XamlRoot = Root.XamlRoot,
            Title = "Rename Preset",
            Content = box,
            PrimaryButtonText = "Rename",
            CloseButtonText = "Cancel",
            DefaultButton = ContentDialogButton.Primary,
        };
        if (await dialog.ShowAsync() == ContentDialogResult.Primary && !string.IsNullOrWhiteSpace(box.Text))
        {
            Audio.UpdatePreset(preset with { Name = Audio.Presets.UniqueName(box.Text.Trim(), preset.Id) });
        }
    }

    private async Task ExportPresetAsync(Core.Model.Preset preset)
    {
        var picker = new Windows.Storage.Pickers.FileSavePicker { SuggestedFileName = preset.Name };
        picker.FileTypeChoices.Add($"{AppInfo.Name} preset", [AppInfo.PresetExtension]);
        WinRT.Interop.InitializeWithWindow.Initialize(picker, WinRT.Interop.WindowNative.GetWindowHandle(this));
        if (await picker.PickSaveFileAsync() is { } file)
        {
            try
            {
                await new PresetArchive(Audio, App.Current.Paths).ExportAsync(Audio.Presets.Find(preset.Id) ?? preset, file.Path);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                await ShowMessageAsync("Export failed", ex.Message);
            }
        }
    }

    private async Task DeletePresetAsync(Core.Model.Preset preset)
    {
        if (await ConfirmAsync("Delete Preset", $"Are you sure you want to delete '{preset.Name}'? This action cannot be undone."))
        {
            Audio.DeletePreset(preset);
        }
    }

    private async Task DeleteSoundAsync(SoundItem sound)
    {
        if (await ConfirmAsync("Delete Sound", $"Delete '{sound.Title}'? It will also be removed from any presets that use it."))
        {
            Audio.Library.DeleteCustom(sound);
        }
    }

    private async Task<bool> ConfirmAsync(string title, string message)
    {
        var dialog = new ContentDialog
        {
            XamlRoot = Root.XamlRoot,
            Title = title,
            Content = message,
            PrimaryButtonText = "Delete",
            CloseButtonText = "Cancel",
            DefaultButton = ContentDialogButton.Close,
        };
        return await dialog.ShowAsync() == ContentDialogResult.Primary;
    }

    private async Task ShowMessageAsync(string title, string message)
    {
        if (Root.XamlRoot is null)
        {
            return;
        }
        var dialog = new ContentDialog { XamlRoot = Root.XamlRoot, Title = title, Content = message, CloseButtonText = "OK" };
        await dialog.ShowAsync();
    }
}
