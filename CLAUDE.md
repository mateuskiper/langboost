# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## O que é

App de desktop WPF (Windows) para estudar idiomas. Mantém os últimos N segundos do áudio do
sistema num buffer circular (N configurável, 1–10s, padrão 5); ao apertar **Ctrl+Shift+Space**,
abre um **player de recorte** para o usuário ouvir e selecionar o trecho, que então é enviado ao
Google Gemini — que **transcreve (EN) e traduz (PT) numa única chamada** — e o resultado aparece
num overlay sempre visível, com um player para reouvir o trecho enviado. Botões no overlay
permitem abrir **configurações** (⚙) e **encerrar** (✕). Todo o código fica em `src/LangBoost/`.
Veja `README.md` para uso pelo usuário final.

## Comandos

```powershell
# Build
dotnet build LangBoost.sln -c Debug      # ou -c Release

# Executar (precisa da chave no ambiente da sessão — veja "Chave" abaixo)
dotnet run --project src/LangBoost

# Publicar versão durável (exe único self-contained, sem runtime instalado) em pasta fixa
dotnet publish src/LangBoost/LangBoost.csproj -c Release -r win-x64 --self-contained true `
  -p:PublishSingleFile=true -p:EnableCompressionInSingleFile=true `
  -p:IncludeNativeLibrariesForSelfExtract=true -o "$env:LOCALAPPDATA\Programs\LangBoost"
```

- **Não há projeto de testes** nem linter configurado. Validação = `dotnet build` + execução manual.
- Para validar a chave do Gemini sem gastar tokens:
  ```powershell
  Invoke-RestMethod "https://generativelanguage.googleapis.com/v1beta/models/gemini-2.5-flash?key=$env:GEMINI_API_KEY"
  ```

## Chave de API (Gemini)

`AppConfig.Load()` resolve a chave nesta ordem: variável de ambiente
`GEMINI_API_KEY` → `GOOGLE_API_KEY` → `%APPDATA%\LangBoost\config.json`. A env var tem precedência.

A chave também pode ser definida **pela UI** (overlay → ⚙ → janela de configurações). `AppConfig.Save()`
grava no `config.json` o campo **`apiKeyProtected`** — a chave **criptografada com DPAPI**
(`DataProtectionScope.CurrentUser`), nunca em texto puro. Ainda lê o campo legado `apiKey` (texto
puro) por compatibilidade. Como a env var tem precedência no `Load()`, a flag `AppConfig.ApiKeyFromEnv`
avisa na UI quando editar a chave não terá efeito após reabrir (a env var venceria).

**Gotcha recorrente:** `setx` só afeta terminais **novos**. Para rodar na sessão atual sem
reabrir o terminal:
```powershell
$env:GEMINI_API_KEY = [Environment]::GetEnvironmentVariable("GEMINI_API_KEY","User")
dotnet run --project src/LangBoost
```
Sem chave, o app sobe e o overlay mostra "GEMINI_API_KEY não configurada"; defina-a em ⚙ (sem
chave o atalho não captura/transcreve, mas ⚙ e ✕ funcionam).

## Arquitetura

Pipeline disparado pelo atalho global (orquestrado em `App.xaml.cs` → `OnStartup` /
`OnHotkeyTriggered` / `OnSendForTranscription`):

```
WasapiLoopbackCapture (todo o áudio do sistema)
  → AudioRingBuffer (últimos N segundos, sobrescreve o mais antigo)
  → [atalho] Snapshot() → AudioFormatConverter.ToWav16kMono (WAV 16kHz mono PCM16)
  → OverlayWindow.ShowReview(wav)  (player de recorte: ouvir + selecionar trecho)
  → [Enviar] AudioFormatConverter.TrimWav(wav, ini, fim)  (recorta o trecho)
  → GeminiClient (1 POST, responseSchema → JSON {original, traducao})
  → OverlayWindow.ShowResult(orig, trad, trimmedWav)  (texto + player; até "Concluir")
```

Mapa de responsabilidades:

| Arquivo | Papel |
|---|---|
| `App.xaml.cs` | Liga tudo; `OnHotkeyTriggered` prepara o WAV e abre o recorte; `OnSendForTranscription` recorta+chama o Gemini em `Task.Run`; `ApplyConfig`/`RestartCapture` reconstroem o pipeline ao salvar settings |
| `AudioCaptureService.cs` | `WasapiLoopbackCapture`; grava no ring buffer no `DataAvailable` |
| `AudioRingBuffer.cs` | Buffer circular thread-safe (lock); `Snapshot()` retorna em ordem cronológica |
| `AudioFormatConverter.cs` | `MediaFoundationResampler` faz downmix+resample para WAV 16kHz mono; `TrimWav` recorta o intervalo selecionado |
| `AudioPlayer.cs` | Reprodutor de WAV em memória (`WaveOutEvent`); usado pelos players de recorte e de resultado |
| `GeminiClient.cs` | REST `generateContent`; áudio inline base64; parseia `{original, traducao}` |
| `HotkeyManager.cs` | `RegisterHotKey`/`WM_HOTKEY` via `HwndSource` da overlay |
| `OverlayWindow.xaml(.cs)` | Overlay borderless/topmost/semi-transparente; estados idle/status/**review (recorte)**/result; botões ⚙/✕; trilha de recorte com 2 alças e playhead |
| `SettingsWindow.xaml(.cs)` | Janela **focável** (separada do overlay) para buffer (1–10s) e chave de API |
| `AppConfig.cs` | Resolve chave/modelo/segundos; `Save()` persiste com a chave criptografada (DPAPI) |

### Restrições não óbvias (não quebre)

- **`MediaFoundationApi.Startup()`** é chamado em `OnStartup` e é **obrigatório** para o
  `MediaFoundationResampler` funcionar. `Shutdown()` em `OnExit`.
- **O hotkey depende do HWND da janela**: só crie o `HotkeyManager` depois que a `OverlayWindow`
  tem handle (após `Show()` / `OnSourceInitialized`). Antes disso `PresentationSource.FromVisual`
  retorna null.
- **`WS_EX_NOACTIVATE`** é aplicado em `OverlayWindow.OnSourceInitialized` para o overlay **não
  roubar o foco** do vídeo. Mantenha ao mexer no estilo da janela.
- **O overlay não recebe foco de teclado** (consequência do `WS_EX_NOACTIVATE`): cliques e arrastes
  de mouse funcionam (botões, sliders, alças de recorte), mas **campos de texto não**. Por isso a
  chave de API fica na `SettingsWindow` (janela normal, focável, aberta via `ShowDialog`), e nunca
  no overlay.
- **Trilha de recorte:** as posições das alças são em **pixels** sobre uma trilha de largura fixa
  (`TrackWidth`), convertidas para tempo via `XToTime`/`TimeToX` usando `AudioPlayer.Duration`.
  Um `DispatcherTimer` move o playhead e **para a reprodução ao fim da seleção** (`WaveOutEvent`
  não tem "tocar até X"). Sempre pare a reprodução (`StopPlayback`) ao trocar de estado.
- **DPAPI:** a chave criptografada (`apiKeyProtected`) só descriptografa no **mesmo usuário Windows**;
  config copiado para outra máquina/usuário falha no `Unprotect` e é tratado como "sem chave".
- **Captura TODO o áudio do sistema** (não só o navegador) — notificações entram no buffer. Migrar
  para Process Loopback seria a evolução, mas exige P/Invoke de `ActivateAudioInterfaceAsync`.
- O formato do `Snapshot()` é o **nativo da captura** (geralmente float 48kHz estéreo); sempre passe
  `_capture.WaveFormat` ao `AudioFormatConverter`, nunca assuma o formato.

### Gotcha de build

`dotnet build`/`run` falha ao copiar `LangBoost.exe` se uma instância estiver aberta (lock). Encerre antes:
```powershell
Get-Process LangBoost -ErrorAction SilentlyContinue | Stop-Process -Force
```

O ícone do app é `src/LangBoost/app.ico` (referenciado por `<ApplicationIcon>` no `.csproj`);
é um `.ico` multi-resolução gerado por script (System.Drawing), não desenhado à mão. A chave
criptografada usa o pacote `System.Security.Cryptography.ProtectedData`.

## Estilo

- Código e comentários em **português**; nomes de tipos/membros em inglês ou português conforme o
  arquivo (siga o vizinho). Mantenha simples e funcional — evite abstrações que o escopo não exige.
