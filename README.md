# LangBoost

> Study languages by watching videos: capture the last few seconds of audio, listen and trim the
> segment, and transcribe + translate with a keyboard shortcut.

A **Windows** desktop tool that helps you study languages while watching videos
(YouTube, Netflix, etc.). It keeps the **last few seconds** of system audio in memory
(1 to 10s, configurable — default 5). When you press **Ctrl+Enter**, a **trim player**
appears so you can listen and select exactly the segment you want; when you click **Send**,
the segment goes to Google Gemini, which **transcribes (English)** and **translates (Portuguese)**
in a single call. The result appears in an always-visible overlay over the browser, with a **player
to replay** the segment that was sent, and stays on screen until you click **Done**.

The overlay also has a **settings (⚙)** button — to adjust the buffer length and the API key — and
a **close (✕)** button.

## How it works

```
Browser plays video → WASAPI loopback (NAudio) → circular buffer of N seconds
   (Ctrl+Enter) → WAV 16 kHz mono → trim player (listen + select)
   (Send) → trimmed segment → Gemini → { English, Portuguese } → overlay (text + player)
```

- The audio keeps playing normally through the speakers (capture is via loopback).
- Works with DRM content (Netflix), since DRM affects the video, not the audio.
- Captures **all system audio** (not just the browser), so avoid sound notifications
  while using it.

## Prerequisites

- Windows 10/11
- [.NET 8 SDK](https://dotnet.microsoft.com/download) (to build)
- A **Google Gemini API key** — create one at https://aistudio.google.com/apikey

## Step by step to run

**1. Configure the Gemini key.** Choose **one** of the options:

- **Through the interface itself** (simplest): open the app and click **⚙** in the overlay. In the
  settings window, paste the key and click **Save**. The key is stored **encrypted** (DPAPI, bound
  to your Windows user) in `%APPDATA%\LangBoost\config.json` — never in plain text.

- **Environment variable**:
  ```powershell
  setx GEMINI_API_KEY "your_key_here"
  ```
  > ⚠️ `setx` only applies to **new** terminals. After running it, **close and open a
  > new PowerShell** before step 3 — the current window does not see the variable.
  > (The app also accepts `GOOGLE_API_KEY`.) The environment variable **takes precedence** over the
  > key saved through the interface.

- **Config file** (model/buffer only): copy `config.json.example` to
  `%APPDATA%\LangBoost\config.json`. Set the **key** through the UI (⚙) or the environment variable
  — both options above store/handle it securely. Avoid writing the key in plain text into this file:
  the app still reads a legacy plain-text `apiKey` field for compatibility, but it is **not
  encrypted** and is not recommended.

> The key is never committed: `config.json` (and `.env`/`secrets.json`) are in `.gitignore`.

**2. (Optional) Build** to verify everything:
```powershell
dotnet build LangBoost.sln -c Release
```

**3. Run** in a terminal that already has the key:
```powershell
dotnet run --project src/LangBoost
```
> If you prefer not to open a new terminal, inject the key into the current session before running:
> ```powershell
> $env:GEMINI_API_KEY = [Environment]::GetEnvironmentVariable("GEMINI_API_KEY","User")
> dotnet run --project src/LangBoost
> ```

**4. Use it:**
1. On startup, the overlay appears at the bottom of the screen with the shortcut hint.
2. Play a video in English.
3. Press **Ctrl+Enter**. The **trim player** opens with the last few captured seconds.
4. Use **▶ Play** to listen and drag the **two handles** on the track to delimit the segment. Click
   **Send** to transcribe only the selection (or **Cancel** to discard it).
5. Read the transcription (EN) and the translation (PT). Use **▶ Play audio** to replay the segment
   that was sent. Click **Done** to clear it.
6. Drag the overlay with the mouse to reposition it, if you like.

> If the overlay shows *"GEMINI_API_KEY not configured"*, set the key in **⚙** (or see step 1).

## Settings (⚙)

Click **⚙** in the overlay to open the settings:

- **Audio buffer:** how many seconds to keep in memory (1 to 10). The change applies
  **immediately**, without restarting the app.
- **Gemini API key:** set and saved encrypted (see step 1).

The **✕** button closes the application.

## Durable installation (single exe + shortcut)

So you don't depend on the terminal or an installed .NET, publish a **self-contained** executable
(single file) into a fixed folder:

```powershell
dotnet publish src/LangBoost/LangBoost.csproj -c Release -r win-x64 --self-contained true `
  -p:PublishSingleFile=true -p:EnableCompressionInSingleFile=true `
  -p:IncludeNativeLibrariesForSelfExtract=true -o "$env:LOCALAPPDATA\Programs\LangBoost"
```

This produces `%LOCALAPPDATA%\Programs\LangBoost\LangBoost.exe`. Create a shortcut on the Desktop and
in the Start Menu:

```powershell
$exe = "$env:LOCALAPPDATA\Programs\LangBoost\LangBoost.exe"
$sh = New-Object -ComObject WScript.Shell
foreach ($loc in @([Environment]::GetFolderPath('Desktop'), "$env:APPDATA\Microsoft\Windows\Start Menu\Programs")) {
  $lnk = $sh.CreateShortcut("$loc\LangBoost.lnk")
  $lnk.TargetPath = $exe; $lnk.WorkingDirectory = (Split-Path $exe); $lnk.IconLocation = "$exe,0"
  $lnk.Save()
}
```

Then just click the shortcut. The first time, set the key in **⚙**. To update, close the app and run
`dotnet publish` again (the shortcut stays valid). To uninstall, delete the `.lnk` files and the
`%LOCALAPPDATA%\Programs\LangBoost` folder.

## Code structure (`src/LangBoost`)

| File | Responsibility |
|---|---|
| `AudioCaptureService.cs` | WASAPI loopback capture (NAudio) |
| `AudioRingBuffer.cs` | Circular buffer of the last N seconds |
| `AudioFormatConverter.cs` | Converts to WAV 16 kHz mono PCM16; trims the selected segment |
| `AudioPlayer.cs` | Plays the in-memory audio (trim and result players) |
| `GeminiClient.cs` | Transcription + translation in one call |
| `HotkeyManager.cs` | Global hotkey (RegisterHotKey) |
| `OverlayWindow.xaml(.cs)` | Always-visible overlay; trim and result players; ⚙/✕ buttons |
| `SettingsWindow.xaml(.cs)` | Settings window (buffer and API key) |
| `AppConfig.cs` | Key/model/seconds; saves the encrypted key (DPAPI) |
| `App.xaml.cs` | Service orchestration |

## Cost and privacy

Each submission sends to Google Gemini only the segment you selected in the trim player
(at most the N seconds of the buffer), with per-use cost according to the chosen model. The audio
leaves your machine for Google's service.
