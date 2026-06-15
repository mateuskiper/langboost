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

    private const double TrackWidth = 560;   // largura útil da trilha de recorte (px)
    private const double ThumbHalf = 6;       // metade da largura da alça (px)

    private string _hotkeyText = "Ctrl+Shift+Space";
    private int _bufferSeconds = 5;

    private readonly DispatcherTimer _playTimer;
    private AudioPlayer? _player;
    private double _startX;                    // posição (px) da alça de início
    private double _endX = TrackWidth;         // posição (px) da alça de fim
    private bool _trimming;                    // true quando o player de recorte está ativo
    private Button? _activePlayButton;
    private string _activePlayLabel = "";
    private TimeSpan _playStopAt;

    /// <summary>Disparado ao clicar na engrenagem; o App abre as configurações.</summary>
    public event Action? SettingsRequested;
    /// <summary>Disparado ao clicar no X; o App encerra a aplicação.</summary>
    public event Action? CloseRequested;
    /// <summary>Disparado ao clicar em "Enviar"; informa o trecho [início, fim] a transcrever.</summary>
    public event Action<TimeSpan, TimeSpan>? SendRequested;
    /// <summary>Disparado ao clicar em "Cancelar" no recorte.</summary>
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

    /// <summary>Atualiza o tamanho do buffer exibido na dica do atalho.</summary>
    public void SetBufferSeconds(int seconds) => _bufferSeconds = seconds;

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        // Não rouba o foco do vídeo ao aparecer/interagir.
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

    // ---- Estados do overlay --------------------------------------------------

    /// <summary>Estado ocioso: apenas a dica do atalho.</summary>
    public void ShowIdle()
    {
        HideDynamicRegions();
        StatusText.Text = $"Pressione {_hotkeyText} para transcrever os últimos {_bufferSeconds}s";
        Reposition();
    }

    /// <summary>Mensagem de status (ex.: "Transcrevendo...", erro).</summary>
    public void ShowStatus(string message)
    {
        HideDynamicRegions();
        StatusText.Text = message;
        Reposition();
    }

    /// <summary>Player de recorte: ouvir o clipe e selecionar o trecho a transcrever.</summary>
    public void ShowReview(byte[] wav)
    {
        HideDynamicRegions();
        StatusText.Text = "Ouça e recorte o trecho que deseja transcrever, depois clique em Enviar.";

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

    /// <summary>Mostra a transcrição (EN), a tradução (PT) e um player do trecho enviado.</summary>
    public void ShowResult(string original, string traducao, byte[] wav)
    {
        HideDynamicRegions();
        StatusText.Text = "Transcrição (EN) · Tradução (PT)";

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

    /// <summary>Esconde tudo que é específico de um estado e para qualquer reprodução.</summary>
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
        // Recalcula a posição após a altura mudar (SizeToContent=Height).
        Dispatcher.BeginInvoke(new Action(PositionBottomCenter),
            DispatcherPriority.Loaded);
    }

    // ---- Reprodução ----------------------------------------------------------

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
        button.Content = "■ Parar";
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
        else StartPlayback(SelectionStart, SelectionEnd, ReviewPlayButton, "▶ Tocar");
    }

    private void OnResultPlayClick(object sender, RoutedEventArgs e)
    {
        if (_player is null) return;
        if (_activePlayButton == ResultPlayButton) StopPlayback();
        else StartPlayback(TimeSpan.Zero, _player.Duration, ResultPlayButton, "▶ Ouvir áudio");
    }

    // ---- Recorte (alças) -----------------------------------------------------

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
        // No recorte mostramos a posição da seleção; o segundo número é a duração total.
        TimeSpan shown = _playTimer.IsEnabled ? position : SelectionStart;
        TimeLabel.Text = $"{Fmt(shown)} / {Fmt(ClipDuration)}  ·  seleção {Fmt(SelectionStart)}–{Fmt(SelectionEnd)}";
    }

    private static string Fmt(TimeSpan t) => t.ToString(@"mm\:ss\.f");

    // ---- Botões --------------------------------------------------------------

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
