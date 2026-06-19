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
    private byte[]? _clipWav; // captured clip (WAV 16k mono) under review/trim
    private readonly List<string> _phrases = new(); // curated English phrases (in memory until saved)
    private string? _lastOriginal; // English text shown in the current result view
    private bool _videoPaused; // true while we paused the video for the capture→result flow

    protected override void OnStartup(StartupEventArgs e)
    {
        // The WPF markup compiler emits the entry-point Main() without a call to
        // App.InitializeComponent() on the current SDK, so App.xaml never loads and its
        // Application.Resources stay empty (the FlatButton style would be missing, crashing
        // every window that references it). Merge the shared styles explicitly instead.
        if (!Resources.Contains("FlatButton"))
            Resources.MergedDictionaries.Add(new ResourceDictionary
            {
                Source = new Uri("/LangBoost;component/styles.xaml", UriKind.Relative)
            });

        base.OnStartup(e);

        MediaFoundationApi.Startup(); // required for the resampler

        _config = AppConfig.Load();

        _overlay = new OverlayWindow();
        _overlay.Show();
        _overlay.SetHotkeyHint(_config.HotkeyText);
        _overlay.SettingsRequested += OnOpenSettings;
        _overlay.CloseRequested += OnCloseRequested;
        _overlay.SendRequested += OnSendForTranscription;
        _overlay.ReviewCancelled += () => { ResumeVideo(); _overlay.ShowIdle(); };
        _overlay.AddPhraseRequested += OnAddPhrase;
        _overlay.PhrasesRequested += OnOpenPhrases;
        _overlay.CaptureRequested += OnHotkeyTriggered;
        _overlay.DoneRequested += ResumeVideo;

        // The hotkey depends on the overlay HWND and is independent of the key/buffer: always register.
        // OnHotkeyTriggered already guards against a missing capture/gemini.
        _hotkey = new HotkeyManager(_overlay, _config.Modifiers, _config.Key);
        _hotkey.Triggered += OnHotkeyTriggered;

        ApplyConfig();
    }

    /// <summary>(Re)builds the pipeline according to the current config. Without a key, it does not capture.</summary>
    private void ApplyConfig()
    {
        if (string.IsNullOrWhiteSpace(_config.ApiKey))
        {
            _gemini = null;
            StopCapture();
            _overlay.ShowStatus(
                "GEMINI_API_KEY not configured. Open settings (⚙) to set the key.");
            return;
        }

        _gemini = new GeminiClient(_config.ApiKey, _config.Model);

        try
        {
            RestartCapture();
        }
        catch (Exception ex)
        {
            _overlay.ShowStatus("Failed to start audio capture: " + ex.Message);
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
        _overlay.SuspendEnterShortcut(); // let the dialog use Enter normally
        try
        {
            if (window.ShowDialog() == true)
                ApplyConfig(); // immediately applies the new key and/or buffer
        }
        finally { _overlay.ResumeEnterShortcut(); }
    }

    /// <summary>"Add" on the result view: stores the current English phrase in memory.</summary>
    private void OnAddPhrase()
    {
        if (string.IsNullOrWhiteSpace(_lastOriginal)) return;
        _phrases.Add(_lastOriginal.Trim());
        _overlay.SetPhraseCount(_phrases.Count);
        _overlay.ConfirmPhraseAdded();
    }

    /// <summary>Opens the focusable editor; it edits _phrases in place and clears it once saved.</summary>
    private void OnOpenPhrases()
    {
        var window = new PhrasesWindow(_phrases) { Owner = _overlay };
        _overlay.SuspendEnterShortcut(); // the editor needs Enter for newlines
        try { window.ShowDialog(); }
        finally { _overlay.ResumeEnterShortcut(); }
        _overlay.SetPhraseCount(_phrases.Count);
    }

    /// <summary>Close (✕): warns about unsaved phrases before shutting down.</summary>
    private void OnCloseRequested()
    {
        if (_phrases.Count == 0)
        {
            Shutdown();
            return;
        }

        _overlay.SuspendEnterShortcut(); // let the message box handle Enter
        MessageBoxResult choice;
        try
        {
            choice = MessageBox.Show(_overlay,
                $"You have {_phrases.Count} unsaved phrase(s). Save them to a file before closing?",
                "LangBoost", MessageBoxButton.YesNoCancel, MessageBoxImage.Warning);
        }
        finally { _overlay.ResumeEnterShortcut(); }

        switch (choice)
        {
            case MessageBoxResult.Yes:
                OnOpenPhrases();              // save (or not) in the editor
                if (_phrases.Count == 0)      // saved → cleared → safe to exit
                    Shutdown();
                break;                        // still has phrases → stay open
            case MessageBoxResult.No:
                Shutdown();                   // discard
                break;
            // Cancel → keep running
        }
    }

    /// <summary>Toggles Space to pause the video, so it freezes while reviewing/transcribing.</summary>
    private void PauseVideo()
    {
        if (_videoPaused) return;
        MediaControl.TogglePlayPause();
        _videoPaused = true;
    }

    /// <summary>Toggles Space to resume the video when the capture flow ends.</summary>
    private void ResumeVideo()
    {
        if (!_videoPaused) return;
        MediaControl.TogglePlayPause();
        _videoPaused = false;
    }

    /// <summary>Hotkey: freezes the captured audio and opens the trim player for review.</summary>
    private async void OnHotkeyTriggered()
    {
        if (_capture is null || _gemini is null) return;

        PauseVideo(); // freeze the video at the captured moment
        _overlay.ShowBusy("Preparing audio...");

        try
        {
            byte[] raw = _capture.Snapshot();
            var format = _capture.WaveFormat;

            byte[] wav = await Task.Run(() => AudioFormatConverter.ToWav16kMono(raw, format));

            if (wav.Length <= 44) // only the WAV header: nothing captured yet
            {
                ResumeVideo();
                _overlay.ShowStatus("No audio captured yet. Play the video and try again.");
                return;
            }

            _clipWav = wav;
            _overlay.ShowReview(wav);
        }
        catch (Exception ex)
        {
            ResumeVideo();
            _overlay.ShowStatus("Failed: " + ex.Message);
        }
    }

    /// <summary>Sends to transcription only the segment the user selected in the player.</summary>
    private async void OnSendForTranscription(TimeSpan from, TimeSpan to)
    {
        if (_gemini is null || _clipWav is null) return;

        byte[] clip = _clipWav;
        _overlay.ShowBusy("Transcribing the selected clip...");

        try
        {
            (byte[] trimmed, TranscriptionResult result) = await Task.Run(async () =>
            {
                byte[] t = AudioFormatConverter.TrimWav(clip, from, to);
                return (t, await _gemini.TranscribeAndTranslateAsync(t));
            });

            if (string.IsNullOrWhiteSpace(result.Original))
            {
                ResumeVideo();
                _overlay.ShowStatus("No speech detected in the selected clip.");
            }
            else
            {
                _lastOriginal = result.Original;
                _overlay.ShowResult(result.Original, result.Traducao, trimmed);
            }
        }
        catch (Exception ex)
        {
            ResumeVideo();
            _overlay.ShowStatus("Failed: " + ex.Message);
        }
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _hotkey?.Dispose();
        _capture?.Dispose();
        try { MediaFoundationApi.Shutdown(); } catch { /* ignore */ }
        base.OnExit(e);
    }
}
