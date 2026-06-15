using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Threading;

namespace LangBoost;

public partial class OverlayWindow : Window
{
    private const int GWL_EXSTYLE = -20;
    private const int WS_EX_NOACTIVATE = 0x08000000;

    [DllImport("user32.dll")] private static extern int GetWindowLong(IntPtr hWnd, int nIndex);
    [DllImport("user32.dll")] private static extern int SetWindowLong(IntPtr hWnd, int nIndex, int dwNewLong);

    private const double TrackWidth = 560;   // usable width of the trim track (px)
    private const double ThumbHalf = 6;       // half the handle width (px)

    private string _hotkeyText = "Ctrl+Shift+Space";
    private int _bufferSeconds = 5;

    private readonly DispatcherTimer _playTimer;
    private AudioPlayer? _player;
    private double _startX;                    // position (px) of the start handle
    private double _endX = TrackWidth;         // position (px) of the end handle
    private bool _trimming;                    // true when the trim player is active
    private Button? _activePlayButton;
    private string _activePlayLabel = "";
    private TimeSpan _playStopAt;

    /// <summary>Raised when the gear is clicked; the App opens the settings.</summary>
    public event Action? SettingsRequested;
    /// <summary>Raised when the X is clicked; the App shuts the application down.</summary>
    public event Action? CloseRequested;
    /// <summary>Raised when "Send" is clicked; reports the [start, end] clip to transcribe.</summary>
    public event Action<TimeSpan, TimeSpan>? SendRequested;
    /// <summary>Raised when "Cancel" is clicked in the trim view.</summary>
    public event Action? ReviewCancelled;

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
        var hwnd = new WindowInteropHelper(this).Handle;
        int ex = GetWindowLong(hwnd, GWL_EXSTYLE);
        SetWindowLong(hwnd, GWL_EXSTYLE, ex | WS_EX_NOACTIVATE);
    }

    private void OnLoaded(object? sender, RoutedEventArgs e) => PositionBottomCenter();

    private void PositionBottomCenter()
    {
        var area = SystemParameters.WorkArea;
        Left = area.Left + (area.Width - ActualWidth) / 2;
        Top = area.Bottom - ActualHeight - 60;
    }

    // ---- Overlay states ------------------------------------------------------

    /// <summary>Idle state: just the hotkey hint.</summary>
    public void ShowIdle()
    {
        HideDynamicRegions();
        StatusText.Text = $"Press {_hotkeyText} to transcribe the last {_bufferSeconds}s";
        Reposition();
    }

    /// <summary>Status message (e.g. "Transcribing...", error).</summary>
    public void ShowStatus(string message)
    {
        HideDynamicRegions();
        StatusText.Text = message;
        Reposition();
    }

    /// <summary>Trim player: listen to the clip and select the segment to transcribe.</summary>
    public void ShowReview(byte[] wav)
    {
        HideDynamicRegions();
        StatusText.Text = "Listen and trim the clip you want to transcribe, then click Send.";

        ResetPlayer(wav);
        _trimming = true;
        _startX = 0;
        _endX = TrackWidth;
        Playhead.Visibility = Visibility.Collapsed;
        UpdateSelectionVisual();
        UpdateTimeLabel(TimeSpan.Zero);
        ReviewPanel.Visibility = Visibility.Visible;
        Reposition();
    }

    /// <summary>Shows the transcription (EN), the translation (PT) and a player of the sent clip.</summary>
    public void ShowResult(string original, string traducao, byte[] wav)
    {
        HideDynamicRegions();
        StatusText.Text = "Transcription (EN) · Translation (PT)";

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
        _trimming = false;
        ResultPlayButton.Visibility = Visibility.Visible;
        DoneButton.Visibility = Visibility.Visible;
        Reposition();
    }

    /// <summary>Hides everything specific to a state and stops any playback.</summary>
    private void HideDynamicRegions()
    {
        StopPlayback();
        ResetPlayer(null);

        OriginalText.Visibility = Visibility.Collapsed;
        TranslationText.Visibility = Visibility.Collapsed;
        OriginalText.Text = "";
        TranslationText.Text = "";
        ResultPlayButton.Visibility = Visibility.Collapsed;
        ReviewPanel.Visibility = Visibility.Collapsed;
        DoneButton.Visibility = Visibility.Collapsed;
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
        button.Content = "■ Stop";
        if (_trimming) Playhead.Visibility = Visibility.Visible;
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
        if (_trimming)
        {
            Playhead.Visibility = Visibility.Collapsed;
            UpdateTimeLabel(TimeSpan.Zero);
        }
    }

    private void OnPlayTick(object? sender, EventArgs e)
    {
        if (_player is null) { StopPlayback(); return; }

        TimeSpan t = _player.CurrentTime;
        if (_trimming)
        {
            UpdateTimeLabel(t);
            Canvas.SetLeft(Playhead, TimeToX(t));
        }

        if (!_player.IsPlaying || t >= _playStopAt)
            StopPlayback();
    }

    private void OnReviewPlayClick(object sender, RoutedEventArgs e)
    {
        if (_activePlayButton == ReviewPlayButton) StopPlayback();
        else StartPlayback(SelectionStart, SelectionEnd, ReviewPlayButton, "▶ Play");
    }

    private void OnResultPlayClick(object sender, RoutedEventArgs e)
    {
        if (_player is null) return;
        if (_activePlayButton == ResultPlayButton) StopPlayback();
        else StartPlayback(TimeSpan.Zero, _player.Duration, ResultPlayButton, "▶ Play audio");
    }

    // ---- Trim (handles) ------------------------------------------------------

    private TimeSpan ClipDuration => _player?.Duration ?? TimeSpan.Zero;
    private TimeSpan SelectionStart => XToTime(_startX);
    private TimeSpan SelectionEnd => XToTime(_endX);

    private double TimeToX(TimeSpan t)
    {
        double total = ClipDuration.TotalSeconds;
        return total <= 0 ? 0 : Math.Clamp(t.TotalSeconds / total * TrackWidth, 0, TrackWidth);
    }

    private TimeSpan XToTime(double x)
    {
        double total = ClipDuration.TotalSeconds;
        return TimeSpan.FromSeconds(Math.Clamp(x, 0, TrackWidth) / TrackWidth * total);
    }

    private void OnStartThumbDrag(object sender, DragDeltaEventArgs e)
    {
        StopPlayback();
        _startX = Math.Clamp(_startX + e.HorizontalChange, 0, _endX - 4);
        UpdateSelectionVisual();
    }

    private void OnEndThumbDrag(object sender, DragDeltaEventArgs e)
    {
        StopPlayback();
        _endX = Math.Clamp(_endX + e.HorizontalChange, _startX + 4, TrackWidth);
        UpdateSelectionVisual();
    }

    private void UpdateSelectionVisual()
    {
        Canvas.SetLeft(StartThumb, _startX - ThumbHalf);
        Canvas.SetLeft(EndThumb, _endX - ThumbHalf);
        Canvas.SetLeft(SelectionBar, _startX);
        SelectionBar.Width = Math.Max(0, _endX - _startX);
        UpdateTimeLabel(TimeSpan.Zero);
    }

    private void UpdateTimeLabel(TimeSpan position)
    {
        // While trimming we show the selection position; the second number is the total duration.
        TimeSpan shown = _playTimer.IsEnabled ? position : SelectionStart;
        TimeLabel.Text = $"{Fmt(shown)} / {Fmt(ClipDuration)}  ·  selection {Fmt(SelectionStart)}–{Fmt(SelectionEnd)}";
    }

    private static string Fmt(TimeSpan t) => t.ToString(@"mm\:ss\.f");

    // ---- Buttons -------------------------------------------------------------

    private void OnReviewSendClick(object sender, RoutedEventArgs e)
    {
        StopPlayback();
        SendRequested?.Invoke(SelectionStart, SelectionEnd);
    }

    private void OnReviewCancelClick(object sender, RoutedEventArgs e)
    {
        StopPlayback();
        ReviewCancelled?.Invoke();
    }

    private void OnDoneClick(object sender, RoutedEventArgs e) => ShowIdle();

    private void OnSettingsClick(object sender, RoutedEventArgs e) => SettingsRequested?.Invoke();

    private void OnCloseClick(object sender, RoutedEventArgs e) => CloseRequested?.Invoke();

    private void OnDragBar(object sender, MouseButtonEventArgs e)
    {
        if (e.LeftButton == MouseButtonState.Pressed)
            DragMove();
    }
}
