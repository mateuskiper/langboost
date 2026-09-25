using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;

namespace LangBoost;

public partial class OverlayWindow : Window
{
    private const int GWL_EXSTYLE = -20;
    private const int WS_EX_NOACTIVATE = 0x08000000;

    [DllImport("user32.dll")] private static extern int GetWindowLong(IntPtr hWnd, int nIndex);
    [DllImport("user32.dll")] private static extern int SetWindowLong(IntPtr hWnd, int nIndex, int dwNewLong);

    // Enter accelerator for Done. The overlay never gets keyboard focus
    // (WS_EX_NOACTIVATE), so a normal KeyDown never fires; a global hotkey is the only way
    // to react to Enter. It is registered only while the Result state is showing and
    // suspended while our own focusable dialogs are open (see Suspend/ResumeEnterShortcut).
    private const int WM_HOTKEY = 0x0312;
    private const int EnterHotkeyId = 0xB010;
    private const uint MOD_NOREPEAT = 0x4000;
    private const uint VK_RETURN = 0x0D;

    [DllImport("user32.dll")] private static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);
    [DllImport("user32.dll")] private static extern bool UnregisterHotKey(IntPtr hWnd, int id);

    private string _hotkeyText = "Ctrl+Enter";
    private int _bufferSeconds = 5;

    private readonly DispatcherTimer _playTimer;
    private AudioPlayer? _player;
    private Button? _activePlayButton;
    private string _activePlayLabel = "";
    private TimeSpan _playStopAt;

    private bool _userPositioned;             // true once the user has dragged the overlay
    private double _anchorLeft;               // user-chosen Left
    private double _anchorBottom;             // user-chosen bottom edge (Top + ActualHeight)

    private IntPtr _hwnd;
    private HwndSource? _source;
    private bool _enterRegistered;            // is the Enter hotkey currently registered
    private int _enterSuspend;                // >0 while a focusable dialog suppresses it
    private Action? _onEnter;                 // what Enter does in the current state (Done)

    /// <summary>Raised when the gear is clicked; the App opens the settings.</summary>
    public event Action? SettingsRequested;
    /// <summary>Raised when the X is clicked; the App shuts the application down.</summary>
    public event Action? CloseRequested;
    /// <summary>Raised when "Add" is clicked in the result view; the App stores the current phrase.</summary>
    public event Action? AddPhraseRequested;
    /// <summary>Raised when the phrases (☰) button is clicked; the App opens the phrases editor.</summary>
    public event Action? PhrasesRequested;
    /// <summary>Raised when the idle "Capture" button is clicked; same action as the hotkey.</summary>
    public event Action? CaptureRequested;
    /// <summary>Raised when "Done" is clicked on the result view; the App resumes the video.</summary>
    public event Action? DoneRequested;

    public OverlayWindow()
    {
        InitializeComponent();
        Loaded += OnLoaded;
        _playTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(40) };
        _playTimer.Tick += OnPlayTick;
    }

    public void SetHotkeyHint(string hotkeyText)
    {
        _hotkeyText = hotkeyText;
        ShowIdle();
    }

    /// <summary>Updates the buffer length shown in the hotkey hint.</summary>
    public void SetBufferSeconds(int seconds) => _bufferSeconds = seconds;

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        // Does not steal focus from the video when shown/interacted with.
        _hwnd = new WindowInteropHelper(this).Handle;
        int ex = GetWindowLong(_hwnd, GWL_EXSTYLE);
        SetWindowLong(_hwnd, GWL_EXSTYLE, ex | WS_EX_NOACTIVATE);

        _source = HwndSource.FromHwnd(_hwnd);
        _source?.AddHook(WndProc);
    }

    protected override void OnClosed(EventArgs e)
    {
        DisableEnter();
        _source?.RemoveHook(WndProc);
        base.OnClosed(e);
    }

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == WM_HOTKEY && wParam.ToInt32() == EnterHotkeyId)
        {
            _onEnter?.Invoke();
            handled = true;
        }
        return IntPtr.Zero;
    }

    // ---- Enter accelerator ---------------------------------------------------

    private void EnableEnter()
    {
        if (_enterRegistered || _enterSuspend > 0 || _hwnd == IntPtr.Zero) return;
        _enterRegistered = RegisterHotKey(_hwnd, EnterHotkeyId, MOD_NOREPEAT, VK_RETURN);
    }

    private void DisableEnter()
    {
        if (!_enterRegistered) return;
        UnregisterHotKey(_hwnd, EnterHotkeyId);
        _enterRegistered = false;
    }

    /// <summary>Releases the Enter hotkey so a focusable dialog (Settings/Phrases/MessageBox)
    /// can use Enter normally. Pair every call with <see cref="ResumeEnterShortcut"/>.</summary>
    public void SuspendEnterShortcut()
    {
        _enterSuspend++;
        DisableEnter();
    }

    /// <summary>Re-arms the Enter hotkey if the current state still wants it.</summary>
    public void ResumeEnterShortcut()
    {
        if (_enterSuspend > 0) _enterSuspend--;
        if (_enterSuspend == 0 && _onEnter is not null) EnableEnter();
    }

    private void OnLoaded(object? sender, RoutedEventArgs e) => PositionBottomCenter();

    private void PositionBottomCenter()
    {
        var area = SystemParameters.WorkArea;
        if (_userPositioned)
        {
            // Keep the user's spot, anchored by the bottom edge so it grows upward as the
            // content height changes (same feel as the default). Clamp to the work area so a
            // height change near a screen edge can't push it off-screen.
            Left = Math.Clamp(_anchorLeft, area.Left, Math.Max(area.Left, area.Right - ActualWidth));
            Top = Math.Clamp(_anchorBottom - ActualHeight, area.Top, Math.Max(area.Top, area.Bottom - ActualHeight));
            return;
        }
        Left = area.Left + (area.Width - ActualWidth) / 2;
        Top = area.Bottom - ActualHeight - 60;
    }

    // ---- Overlay states ------------------------------------------------------

    /// <summary>Idle state: just the capture button (it already shows the shortcut + intent).</summary>
    public void ShowIdle()
    {
        HideDynamicRegions();
        StatusText.Visibility = Visibility.Collapsed;
        CaptureChip.Text = _hotkeyText;
        CaptureLabel.Text = $"Capture last {_bufferSeconds}s";
        CaptureButton.Visibility = Visibility.Visible;
        Reposition();
    }

    /// <summary>Status message (e.g. error). No spinner — use <see cref="ShowBusy"/> for work in progress.</summary>
    public void ShowStatus(string message)
    {
        HideDynamicRegions();
        StatusText.Text = message;
        Reposition();
    }

    /// <summary>Status message with a spinning loader, for long-running work (capture/transcription).</summary>
    public void ShowBusy(string message)
    {
        HideDynamicRegions();
        StatusText.Text = message;
        Spinner.Visibility = Visibility.Visible;
        var spin = new DoubleAnimation(0, 360, TimeSpan.FromSeconds(0.9))
        {
            RepeatBehavior = RepeatBehavior.Forever
        };
        SpinnerRotate.BeginAnimation(RotateTransform.AngleProperty, spin);
        Reposition();
    }

    /// <summary>Shows the transcription (EN), the translation (PT) and a player of the sent clip.</summary>
    public void ShowResult(string original, string traducao, byte[] wav)
    {
        HideDynamicRegions();
        StatusText.Visibility = Visibility.Collapsed;

        OriginalText.Text = original;
        OriginalText.Visibility = Visibility.Visible;
        if (string.IsNullOrWhiteSpace(traducao))
        {
            TranslationText.Visibility = Visibility.Collapsed;
        }
        else
        {
            TranslationText.Text = traducao;
            TranslationText.Visibility = Visibility.Visible;
        }

        ResetPlayer(wav);
        AddButton.Content = "Add";
        AddButton.IsEnabled = true;
        ResultActions.Visibility = Visibility.Visible;
        _onEnter = Done; // Enter = Done
        EnableEnter();
        Reposition();
    }

    /// <summary>Dead-end notice (error / nothing detected) with an Ok button back to idle.</summary>
    public void ShowNotice(string message)
    {
        HideDynamicRegions();
        StatusText.Text = message;
        OkButton.Visibility = Visibility.Visible;
        Reposition();
    }

    /// <summary>Updates the phrases (☰) button with the in-memory count.</summary>
    public void SetPhraseCount(int n)
    {
        PhrasesButton.Content = n > 0 ? $"☰ {n}" : "☰";
        PhrasesButton.ToolTip = n > 0 ? $"Phrases ({n})" : "Phrases";
    }

    /// <summary>Brief visual confirmation that the current phrase was added.</summary>
    public void ConfirmPhraseAdded()
    {
        AddButton.Content = "Added ✓";
        AddButton.IsEnabled = false;
        var t = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(900) };
        t.Tick += (_, _) =>
        {
            t.Stop();
            AddButton.Content = "Add";
            AddButton.IsEnabled = true;
        };
        t.Start();
    }

    /// <summary>Hides everything specific to a state and stops any playback.</summary>
    private void HideDynamicRegions()
    {
        StopPlayback();
        ResetPlayer(null);

        _onEnter = null; // no Enter action outside the result view
        DisableEnter();

        SpinnerRotate.BeginAnimation(RotateTransform.AngleProperty, null);
        Spinner.Visibility = Visibility.Collapsed;
        StatusText.Visibility = Visibility.Visible;
        OriginalText.Visibility = Visibility.Collapsed;
        TranslationText.Visibility = Visibility.Collapsed;
        OriginalText.Text = "";
        TranslationText.Text = "";
        ResultActions.Visibility = Visibility.Collapsed;
        CaptureButton.Visibility = Visibility.Collapsed;
        OkButton.Visibility = Visibility.Collapsed;
    }

    private void Reposition()
    {
        // Recomputes the position after the height changes (SizeToContent=Height).
        Dispatcher.BeginInvoke(new Action(PositionBottomCenter),
            DispatcherPriority.Loaded);
    }

    // ---- Playback ------------------------------------------------------------

    private void ResetPlayer(byte[]? wav)
    {
        _player?.Dispose();
        _player = wav is null ? null : new AudioPlayer(wav);
    }

    private void StartPlayback(TimeSpan from, TimeSpan to, Button button, string idleLabel)
    {
        if (_player is null) return;
        StopPlayback();
        _player.Play(from);
        _playStopAt = to;
        _activePlayButton = button;
        _activePlayLabel = idleLabel;
        button.Content = "■";
        _playTimer.Start();
    }

    private void StopPlayback()
    {
        _playTimer.Stop();
        _player?.Stop();
        if (_activePlayButton is not null)
        {
            _activePlayButton.Content = _activePlayLabel;
            _activePlayButton = null;
        }
    }

    private void OnPlayTick(object? sender, EventArgs e)
    {
        if (_player is null || !_player.IsPlaying || _player.CurrentTime >= _playStopAt)
            StopPlayback();
    }

    private void OnResultPlayClick(object sender, RoutedEventArgs e)
    {
        if (_player is null) return;
        if (_activePlayButton == ResultPlayButton) StopPlayback();
        else StartPlayback(TimeSpan.Zero, _player.Duration, ResultPlayButton, "\U0001F50A");
    }

    // ---- Buttons -------------------------------------------------------------

    private void OnDoneClick(object sender, RoutedEventArgs e) => Done();

    private void Done()
    {
        DoneRequested?.Invoke();
        ShowIdle();
    }

    private void OnAddClick(object sender, RoutedEventArgs e) => AddPhraseRequested?.Invoke();

    private void OnPhrasesClick(object sender, RoutedEventArgs e) => PhrasesRequested?.Invoke();

    private void OnCaptureClick(object sender, RoutedEventArgs e) => CaptureRequested?.Invoke();

    // The video was already resumed before a notice is shown, so Ok just returns to idle.
    private void OnOkClick(object sender, RoutedEventArgs e) => ShowIdle();

    private void OnSettingsClick(object sender, RoutedEventArgs e) => SettingsRequested?.Invoke();

    private void OnCloseClick(object sender, RoutedEventArgs e) => CloseRequested?.Invoke();

    private void OnDragBar(object sender, MouseButtonEventArgs e)
    {
        if (e.LeftButton != MouseButtonState.Pressed) return;
        DragMove(); // blocks until the mouse is released, so Left/Top are final afterward
        _userPositioned = true;
        _anchorLeft = Left;
        _anchorBottom = Top + ActualHeight;
    }
}
