using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace LangBoost;

/// <summary>
/// Configuração simples. A chave do Gemini vem da variável de ambiente GEMINI_API_KEY
/// (precedência) ou do arquivo %APPDATA%\LangBoost\config.json, onde fica criptografada
/// com DPAPI (escopo do usuário atual). O atalho é fixo (Ctrl+Shift+Space).
/// </summary>
public sealed class AppConfig
{
    public string ApiKey { get; internal set; } = "";
    public string Model { get; internal set; } = "gemini-2.5-flash";
    public int BufferSeconds { get; internal set; } = 5;

    /// <summary>True quando a chave veio de variável de ambiente (tem precedência sobre o
    /// arquivo). Nesse caso, editar a chave pela UI não terá efeito após reabrir o app.</summary>
    public bool ApiKeyFromEnv { get; private set; }

    public uint Modifiers => HotkeyManager.MOD_CONTROL | HotkeyManager.MOD_SHIFT;
    public uint Key => 0x20; // VK_SPACE
    public string HotkeyText => "Ctrl+Shift+Space";

    public static string ConfigPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "LangBoost", "config.json");

    public static AppConfig Load()
    {
        var cfg = new AppConfig();

        if (File.Exists(ConfigPath))
        {
            try
            {
                var file = JsonSerializer.Deserialize<FileModel>(File.ReadAllText(ConfigPath));
                if (file is not null)
                {
                    if (!string.IsNullOrWhiteSpace(file.Model)) cfg.Model = file.Model!;
                    if (file.BufferSeconds is > 0) cfg.BufferSeconds = file.BufferSeconds.Value;

                    // Chave criptografada (formato atual) tem prioridade; texto puro é compat.
                    if (!string.IsNullOrWhiteSpace(file.ApiKeyProtected))
                        cfg.ApiKey = Unprotect(file.ApiKeyProtected!) ?? "";
                    else if (!string.IsNullOrWhiteSpace(file.ApiKey))
                        cfg.ApiKey = file.ApiKey!;
                }
            }
            catch { /* config inválido: usa padrões */ }
        }

        // Variável de ambiente tem precedência sobre o arquivo.
        var env = Environment.GetEnvironmentVariable("GEMINI_API_KEY")
                  ?? Environment.GetEnvironmentVariable("GOOGLE_API_KEY");
        if (!string.IsNullOrWhiteSpace(env))
        {
            cfg.ApiKey = env.Trim();
            cfg.ApiKeyFromEnv = true;
        }

        return cfg;
    }

    /// <summary>Grava Model, BufferSeconds e a chave (criptografada com DPAPI) em config.json.</summary>
    public void Save()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(ConfigPath)!);

        var file = new FileModel
        {
            Model = Model,
            BufferSeconds = BufferSeconds,
            ApiKeyProtected = string.IsNullOrWhiteSpace(ApiKey) ? null : Protect(ApiKey),
        };

        File.WriteAllText(ConfigPath,
            JsonSerializer.Serialize(file, new JsonSerializerOptions { WriteIndented = true }));
    }

    private static string Protect(string plain)
    {
        byte[] cipher = ProtectedData.Protect(
            Encoding.UTF8.GetBytes(plain), null, DataProtectionScope.CurrentUser);
        return Convert.ToBase64String(cipher);
    }

    private static string? Unprotect(string base64)
    {
        try
        {
            byte[] plain = ProtectedData.Unprotect(
                Convert.FromBase64String(base64), null, DataProtectionScope.CurrentUser);
            return Encoding.UTF8.GetString(plain);
        }
        catch
        {
            // Não decripta (ex.: config copiado de outra máquina/usuário): trata como sem chave.
            return null;
        }
    }

    private sealed class FileModel
    {
        public string? ApiKey { get; set; }          // legado (texto puro) — apenas leitura
        public string? ApiKeyProtected { get; set; } // chave criptografada com DPAPI (base64)
        public string? Model { get; set; }
        public int? BufferSeconds { get; set; }
    }
}
