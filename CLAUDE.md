# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## What it is

A WPF desktop app (Windows) for studying languages. It keeps the last N seconds of system audio in
a circular buffer (N configurable, 1–10s, default 5); when you press **Ctrl+Shift+Space**, it opens
a **trim player** so the user can listen and select the segment, which is then sent to Google Gemini
— which **transcribes (EN) and translates (PT) in a single call** — and the result appears in an
always-visible overlay, with a player to replay the segment that was sent. Buttons on the overlay
allow opening **settings** (⚙) and **closing** (✕). All code lives in `src/LangBoost/`.
See `README.md` for end-user usage.

## Commands

```powershell
# Build
dotnet build LangBoost.sln -c Debug      # or -c Release

# Run (needs the key in the session environment — see "Key" below)
dotnet run --project src/LangBoost

# Test (xUnit project in tests/LangBoost.Tests)
dotnet test LangBoost.sln -c Debug

# Publish a durable version (single self-contained exe, no installed runtime) to a fixed folder
dotnet publish src/LangBoost/LangBoost.csproj -c Release -r win-x64 --self-contained true `
  -p:PublishSingleFile=true -p:EnableCompressionInSingleFile=true `
  -p:IncludeNativeLibrariesForSelfExtract=true -o "$env:LOCALAPPDATA\Programs\LangBoost"
```

- **Tests** live in `tests/LangBoost.Tests` (xUnit): `AudioRingBuffer`, `AudioFormatConverter`
  (`TrimWav`/`ToWav16kMono`), `GeminiClient.ParseResult`/`ExtractError`, and the DPAPI
  `AppConfig.Protect`/`Unprotect`. There is no configured linter. Validation = `dotnet build` +
  `dotnet test` + manual run.
- **Do NOT modify anything under `tests/`, `.githooks/`, `hooks/protect-tests.ps1` or
  `.claude/settings.json`.** These are the protected test/governance suite — they may be changed
  **only by the developer**, never by Claude Code. A PreToolUse hook (`hooks/protect-tests.ps1`)
  technically blocks edits to them; do not try to work around it.
- A **git pre-commit hook** (`.githooks/pre-commit`, enabled via `git config core.hooksPath
  .githooks`) runs `dotnet test` and aborts the commit if any test fails. The tests are the
  regression contract — make code changes pass them; never weaken a test to make a commit go through.
- To validate the Gemini key without spending tokens:
  ```powershell
  Invoke-RestMethod "https://generativelanguage.googleapis.com/v1beta/models/gemini-2.5-flash" `
    -Headers @{ "x-goog-api-key" = $env:GEMINI_API_KEY }
  ```

## API key (Gemini)

`AppConfig.Load()` resolves the key in this order: environment variable
`GEMINI_API_KEY` → `GOOGLE_API_KEY` → `%APPDATA%\LangBoost\config.json`. The env var takes precedence.

The key can also be set **through the UI** (overlay → ⚙ → settings window). `AppConfig.Save()`
writes the **`apiKeyProtected`** field to `config.json` — the key **encrypted with DPAPI**
(`DataProtectionScope.CurrentUser`), never in plain text. It still reads the legacy `apiKey` field
(plain text) for compatibility. Since the env var takes precedence in `Load()`, the
`AppConfig.ApiKeyFromEnv` flag warns in the UI when editing the key will have no effect after
reopening (the env var would win).

**Recurring gotcha:** `setx` only affects **new** terminals. To run in the current session without
reopening the terminal:
```powershell
$env:GEMINI_API_KEY = [Environment]::GetEnvironmentVariable("GEMINI_API_KEY","User")
dotnet run --project src/LangBoost
```
Without a key, the app starts and the overlay shows "GEMINI_API_KEY not configured"; set it in ⚙
(without a key the shortcut does not capture/transcribe, but ⚙ and ✕ work).

## Architecture

Pipeline triggered by the global hotkey (orchestrated in `App.xaml.cs` → `OnStartup` /
`OnHotkeyTriggered` / `OnSendForTranscription`):

```
WasapiLoopbackCapture (all system audio)
  → AudioRingBuffer (last N seconds, overwrites the oldest)
  → [hotkey] Snapshot() → AudioFormatConverter.ToWav16kMono (WAV 16kHz mono PCM16)
  → OverlayWindow.ShowReview(wav)  (trim player: listen + select segment)
  → [Send] AudioFormatConverter.TrimWav(wav, start, end)  (trims the segment)
  → GeminiClient (1 POST, responseSchema → JSON {original, traducao})
  → OverlayWindow.ShowResult(orig, trad, trimmedWav)  (text + player; until "Done")
```

Responsibility map:

| File | Role |
|---|---|
| `App.xaml.cs` | Wires everything together; `OnHotkeyTriggered` prepares the WAV and opens the trim view; `OnSendForTranscription` trims + calls Gemini in `Task.Run`; `ApplyConfig`/`RestartCapture` rebuild the pipeline when settings are saved |
| `AudioCaptureService.cs` | `WasapiLoopbackCapture`; writes to the ring buffer on `DataAvailable` |
| `AudioRingBuffer.cs` | Thread-safe circular buffer (lock); `Snapshot()` returns in chronological order |
| `AudioFormatConverter.cs` | `MediaFoundationResampler` downmixes+resamples to WAV 16kHz mono; `TrimWav` trims the selected range |
| `AudioPlayer.cs` | In-memory WAV player (`WaveOutEvent`); used by the trim and result players |
| `GeminiClient.cs` | REST `generateContent`; inline base64 audio; parses `{original, traducao}` |
| `HotkeyManager.cs` | `RegisterHotKey`/`WM_HOTKEY` via the overlay's `HwndSource` |
| `OverlayWindow.xaml(.cs)` | Borderless/topmost/semi-transparent overlay; idle/status/**review (trim)**/result states; ⚙/✕ buttons; trim track with 2 handles and playhead |
| `SettingsWindow.xaml(.cs)` | **Focusable** window (separate from the overlay) for buffer (1–10s) and API key |
| `AppConfig.cs` | Resolves key/model/seconds; `Save()` persists with the key encrypted (DPAPI) |

### Non-obvious constraints (don't break)

- **`MediaFoundationApi.Startup()`** is called in `OnStartup` and is **mandatory** for the
  `MediaFoundationResampler` to work. `Shutdown()` in `OnExit`.
- **The hotkey depends on the window's HWND**: only create the `HotkeyManager` after the
  `OverlayWindow` has a handle (after `Show()` / `OnSourceInitialized`). Before that
  `PresentationSource.FromVisual` returns null.
- **`WS_EX_NOACTIVATE`** is applied in `OverlayWindow.OnSourceInitialized` so the overlay **does not
  steal focus** from the video. Keep it when touching the window style.
- **The overlay does not receive keyboard focus** (a consequence of `WS_EX_NOACTIVATE`): mouse clicks
  and drags work (buttons, sliders, trim handles), but **text fields do not**. That's why the API
  key lives in the `SettingsWindow` (a normal, focusable window opened via `ShowDialog`), and never
  in the overlay.
- **Trim track:** handle positions are in **pixels** over a fixed-width track (`TrackWidth`),
  converted to time via `XToTime`/`TimeToX` using `AudioPlayer.Duration`. A `DispatcherTimer` moves
  the playhead and **stops playback at the end of the selection** (`WaveOutEvent` has no "play until
  X"). Always stop playback (`StopPlayback`) when switching states.
- **DPAPI:** the encrypted key (`apiKeyProtected`) only decrypts on the **same Windows user**; a
  config copied to another machine/user fails on `Unprotect` and is treated as "no key".
- **Captures ALL system audio** (not just the browser) — notifications enter the buffer. Migrating
  to Process Loopback would be the evolution, but it requires P/Invoke of
  `ActivateAudioInterfaceAsync`.
- The format of `Snapshot()` is the **native capture format** (usually float 48kHz stereo); always
  pass `_capture.WaveFormat` to `AudioFormatConverter`, never assume the format.

### Build gotcha

`dotnet build`/`run` fails to copy `LangBoost.exe` if an instance is open (lock). Close it first:
```powershell
Get-Process LangBoost -ErrorAction SilentlyContinue | Stop-Process -Force
```

The app icon is `src/LangBoost/app.ico` (referenced by `<ApplicationIcon>` in the `.csproj`);
it is a multi-resolution `.ico` generated by a script (System.Drawing), not hand-drawn. The
encrypted key uses the `System.Security.Cryptography.ProtectedData` package.

## Style

- Code and comments in **English**; type/member names follow the surrounding file. Keep it simple and
  functional — avoid abstractions the scope does not require.
