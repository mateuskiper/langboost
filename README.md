# LangBoost

> Estude idiomas assistindo vídeos: capture os últimos segundos de áudio, ouça e recorte o
> trecho, e transcreva + traduza com um atalho de teclado.

Ferramenta de desktop para **Windows** que ajuda a estudar idiomas assistindo vídeos
(YouTube, Netflix, etc.). Ela mantém em memória os **últimos segundos** do áudio do sistema
(1 a 10s, configurável — padrão 5). Ao pressionar **Ctrl+Shift+Space**, aparece um **player de
recorte** para você ouvir e selecionar exatamente o trecho desejado; ao clicar em **Enviar**,
o trecho vai ao Google Gemini, que **transcreve (inglês)** e **traduz (português)** numa única
chamada. O resultado aparece num overlay sempre visível sobre o navegador, com um **player para
reouvir** o trecho enviado, e permanece na tela até você clicar em **Concluir**.

O overlay ainda tem um botão de **configurações (⚙)** — para ajustar o tempo do buffer e a chave
de API — e um botão de **encerrar (✕)**.

## Como funciona

```
Navegador toca vídeo → WASAPI loopback (NAudio) → buffer circular de N segundos
   (Ctrl+Shift+Space) → WAV 16 kHz mono → player de recorte (ouvir + selecionar)
   (Enviar) → trecho recortado → Gemini → { inglês, português } → overlay (texto + player)
```

- O áudio continua tocando normalmente nos alto-falantes (a captura é por loopback).
- Funciona com conteúdo DRM (Netflix), pois o DRM afeta o vídeo, não o áudio.
- Captura **todo o áudio do sistema** (não só o navegador), então evite notificações
  sonoras durante o uso.

## Pré-requisitos

- Windows 10/11
- [.NET 8 SDK](https://dotnet.microsoft.com/download) (para compilar)
- Uma **chave de API do Google Gemini** — crie em https://aistudio.google.com/apikey

## Passo a passo para executar

**1. Configure a chave do Gemini.** Escolha **uma** das opções:

- **Pela própria interface** (mais simples): abra o app e clique em **⚙** no overlay. Na janela de
  configurações, cole a chave e clique em **Salvar**. A chave é gravada **criptografada** (DPAPI,
  vinculada ao seu usuário do Windows) em `%APPDATA%\LangBoost\config.json` — nunca em texto puro.

- **Variável de ambiente**:
  ```powershell
  setx GEMINI_API_KEY "sua_chave_aqui"
  ```
  > ⚠️ O `setx` só vale para terminais **novos**. Depois de rodá-lo, **feche e abra um
  > novo PowerShell** antes do passo 3 — a janela atual não enxerga a variável.
  > (O app também aceita `GOOGLE_API_KEY`.) A variável de ambiente **tem precedência** sobre a
  > chave salva pela interface.

- **Arquivo de config**: copie `config.json.example` para
  `%APPDATA%\LangBoost\config.json` e preencha `apiKey`.

> A chave nunca é commitada: `config.json` está no `.gitignore`.

**2. (Opcional) Compile** para verificar tudo:
```powershell
dotnet build LangBoost.sln -c Release
```

**3. Execute** num terminal que já tenha a chave:
```powershell
dotnet run --project src/LangBoost
```
> Se preferir não abrir um terminal novo, injete a chave na sessão atual antes de rodar:
> ```powershell
> $env:GEMINI_API_KEY = [Environment]::GetEnvironmentVariable("GEMINI_API_KEY","User")
> dotnet run --project src/LangBoost
> ```

**4. Use:**
1. Ao iniciar, o overlay aparece na parte inferior da tela com a dica do atalho.
2. Reproduza um vídeo em inglês.
3. Pressione **Ctrl+Shift+Space**. Abre o **player de recorte** com os últimos segundos capturados.
4. Use **▶ Tocar** para ouvir e arraste as **duas alças** na trilha para delimitar o trecho. Clique
   em **Enviar** para transcrever só a seleção (ou **Cancelar** para descartar).
5. Leia a transcrição (EN) e a tradução (PT). Use **▶ Ouvir áudio** para reouvir o trecho enviado.
   Clique em **Concluir** para limpar.
6. Arraste o overlay com o mouse para reposicioná-lo, se quiser.

> Se o overlay mostrar *"GEMINI_API_KEY não configurada"*, defina a chave em **⚙** (ou veja o passo 1).

## Configurações (⚙)

Clique em **⚙** no overlay para abrir as configurações:

- **Tempo de áudio (buffer):** quantos segundos manter em memória (1 a 10). A mudança vale
  **imediatamente**, sem reiniciar o app.
- **Chave da API do Gemini:** definida e salva criptografada (veja o passo 1).

O botão **✕** encerra a aplicação.

## Instalação durável (exe único + atalho)

Para não depender do terminal nem do .NET instalado, publique um executável **self-contained**
(arquivo único) numa pasta fixa:

```powershell
dotnet publish src/LangBoost/LangBoost.csproj -c Release -r win-x64 --self-contained true `
  -p:PublishSingleFile=true -p:EnableCompressionInSingleFile=true `
  -p:IncludeNativeLibrariesForSelfExtract=true -o "$env:LOCALAPPDATA\Programs\LangBoost"
```

Isso gera `%LOCALAPPDATA%\Programs\LangBoost\LangBoost.exe`. Crie um atalho na Área de Trabalho e
no Menu Iniciar:

```powershell
$exe = "$env:LOCALAPPDATA\Programs\LangBoost\LangBoost.exe"
$sh = New-Object -ComObject WScript.Shell
foreach ($loc in @([Environment]::GetFolderPath('Desktop'), "$env:APPDATA\Microsoft\Windows\Start Menu\Programs")) {
  $lnk = $sh.CreateShortcut("$loc\LangBoost.lnk")
  $lnk.TargetPath = $exe; $lnk.WorkingDirectory = (Split-Path $exe); $lnk.IconLocation = "$exe,0"
  $lnk.Save()
}
```

Depois é só clicar no atalho. Na primeira vez, defina a chave em **⚙**. Para atualizar, feche o app
e rode o `dotnet publish` de novo (o atalho continua válido). Para desinstalar, apague os `.lnk` e a
pasta `%LOCALAPPDATA%\Programs\LangBoost`.

## Estrutura do código (`src/LangBoost`)

| Arquivo | Responsabilidade |
|---|---|
| `AudioCaptureService.cs` | Captura loopback WASAPI (NAudio) |
| `AudioRingBuffer.cs` | Buffer circular dos últimos N segundos |
| `AudioFormatConverter.cs` | Converte para WAV 16 kHz mono PCM16; recorta o trecho selecionado |
| `AudioPlayer.cs` | Reproduz o áudio em memória (players de recorte e de resultado) |
| `GeminiClient.cs` | Transcrição + tradução em uma chamada |
| `HotkeyManager.cs` | Atalho global (RegisterHotKey) |
| `OverlayWindow.xaml(.cs)` | Overlay sempre visível; player de recorte e de resultado; botões ⚙/✕ |
| `SettingsWindow.xaml(.cs)` | Janela de configurações (buffer e chave de API) |
| `AppConfig.cs` | Chave/modelo/segundos; salva a chave criptografada (DPAPI) |
| `App.xaml.cs` | Orquestração dos serviços |

## Custo e privacidade

Cada envio manda ao Google Gemini apenas o trecho que você selecionou no player de recorte
(no máximo os N segundos do buffer), com custo por uso conforme o modelo escolhido. O áudio sai
da sua máquina para o serviço do Google.
