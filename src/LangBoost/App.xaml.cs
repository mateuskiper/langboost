using System.Windows;
using NAudio.MediaFoundation;

namespace LangBoost;

public partial class App : Application
{
    private AppConfig _config = null!;
    private OverlayWindow _overlay = null!;
    private AudioCaptureService? _capture;
    private GeminiClient? _gemini;
    private HotkeyManager? _hotkey;
    private byte[]? _clipWav; // clipe capturado (WAV 16k mono) em revisão/recorte

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        MediaFoundationApi.Startup(); // necessário para o resampler

        _config = AppConfig.Load();

        _overlay = new OverlayWindow();
        _overlay.Show();
        _overlay.SetHotkeyHint(_config.HotkeyText);
        _overlay.SettingsRequested += OnOpenSettings;
        _overlay.CloseRequested += () => Shutdown();
        _overlay.SendRequested += OnSendForTranscription;
        _overlay.ReviewCancelled += () => _overlay.ShowIdle();

        // O atalho depende do HWND da overlay e independe da chave/buffer: registra sempre.
        // OnHotkeyTriggered já protege contra captura/gemini ausentes.
        _hotkey = new HotkeyManager(_overlay, _config.Modifiers, _config.Key);
        _hotkey.Triggered += OnHotkeyTriggered;

        ApplyConfig();
    }

    /// <summary>(Re)constrói o pipeline conforme a config atual. Sem chave, não captura.</summary>
    private void ApplyConfig()
    {
        if (string.IsNullOrWhiteSpace(_config.ApiKey))
        {
            _gemini = null;
            StopCapture();
            _overlay.ShowStatus(
                "GEMINI_API_KEY não configurada. Abra as configurações (⚙) para definir a chave.");
            return;
        }

        _gemini = new GeminiClient(_config.ApiKey, _config.Model);

        try
        {
            RestartCapture();
        }
        catch (Exception ex)
        {
            _overlay.ShowStatus("Falha ao iniciar a captura de áudio: " + ex.Message);
            return;
        }

        _overlay.SetBufferSeconds(_config.BufferSeconds);
        _overlay.ShowIdle();
    }

    private void RestartCapture()
    {
        StopCapture();
        _capture = new AudioCaptureService(_config.BufferSeconds);
        _capture.Start();
    }

    private void StopCapture()
    {
        _capture?.Dispose();
        _capture = null;
    }

    private void OnOpenSettings()
    {
        var window = new SettingsWindow(_config) { Owner = _overlay };
        if (window.ShowDialog() == true)
            ApplyConfig(); // aplica imediatamente a nova chave e/ou buffer
    }

    /// <summary>Atalho: congela o áudio capturado e abre o player de recorte para revisão.</summary>
    private async void OnHotkeyTriggered()
    {
        if (_capture is null || _gemini is null) return;

        _overlay.ShowStatus("Preparando áudio...");

        try
        {
            byte[] raw = _capture.Snapshot();
            var format = _capture.WaveFormat;

            byte[] wav = await Task.Run(() => AudioFormatConverter.ToWav16kMono(raw, format));

            if (wav.Length <= 44) // só o cabeçalho WAV: nada capturado ainda
            {
                _overlay.ShowStatus("Nenhum áudio capturado ainda. Toque o vídeo e tente de novo.");
                return;
            }

            _clipWav = wav;
            _overlay.ShowReview(wav);
        }
        catch (Exception ex)
        {
            _overlay.ShowStatus("Falha: " + ex.Message);
        }
    }

    /// <summary>Envia à transcrição apenas o trecho selecionado pelo usuário no player.</summary>
    private async void OnSendForTranscription(TimeSpan from, TimeSpan to)
    {
        if (_gemini is null || _clipWav is null) return;

        byte[] clip = _clipWav;
        _overlay.ShowStatus("Transcrevendo o trecho selecionado...");

        try
        {
            (byte[] trimmed, TranscriptionResult result) = await Task.Run(async () =>
            {
                byte[] t = AudioFormatConverter.TrimWav(clip, from, to);
                return (t, await _gemini.TranscribeAndTranslateAsync(t));
            });

            if (string.IsNullOrWhiteSpace(result.Original))
                _overlay.ShowStatus("Nenhuma fala detectada no trecho selecionado.");
            else
                _overlay.ShowResult(result.Original, result.Traducao, trimmed);
        }
        catch (Exception ex)
        {
            _overlay.ShowStatus("Falha: " + ex.Message);
        }
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _hotkey?.Dispose();
        _capture?.Dispose();
        try { MediaFoundationApi.Shutdown(); } catch { /* ignora */ }
        base.OnExit(e);
    }
}
