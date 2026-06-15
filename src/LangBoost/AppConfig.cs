using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace LangBoost;

/// <summary>
/// Simple configuration. The Gemini key comes from the GEMINI_API_KEY environment variable
/// (precedence) or from the %APPDATA%\LangBoost\config.json file, where it is encrypted
/// with DPAPI (current-user scope). The hotkey is fixed (Ctrl+Shift+Space).
/// </summary>
public sealed class AppConfig
{
    public string ApiKey { get; internal set; } = "";
    public string Model { get; internal set; } = "gemini-2.5-flash";
    public int BufferSeconds { get; internal set; } = 5;

    /// <summary>True when the key came from an environment variable (takes precedence over the
    /// file). In that case, editing the key through the UI has no effect after reopening the app.</summary>
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

                    // The encrypted key (current format) takes priority; plain text is for compat.
                    if (!string.IsNullOrWhiteSpace(file.ApiKeyProtected))
                        cfg.ApiKey = Unprotect(file.ApiKeyProtected!) ?? "";
                    else if (!string.IsNullOrWhiteSpace(file.ApiKey))
                        cfg.ApiKey = file.ApiKey!;
                }
            }
            catch { /* invalid config: use defaults */ }
        }

        // The environment variable takes precedence over the file.
        var env = Environment.GetEnvironmentVariable("GEMINI_API_KEY")
                  ?? Environment.GetEnvironmentVariable("GOOGLE_API_KEY");
        if (!string.IsNullOrWhiteSpace(env))
        {
            cfg.ApiKey = env.Trim();
            cfg.ApiKeyFromEnv = true;
        }

        return cfg;
    }

    /// <summary>Writes Model, BufferSeconds and the key (DPAPI-encrypted) to config.json.</summary>
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

    internal static string Protect(string plain)
    {
        byte[] cipher = ProtectedData.Protect(
            Encoding.UTF8.GetBytes(plain), null, DataProtectionScope.CurrentUser);
        return Convert.ToBase64String(cipher);
    }

    internal static string? Unprotect(string base64)
    {
        try
        {
            byte[] plain = ProtectedData.Unprotect(
                Convert.FromBase64String(base64), null, DataProtectionScope.CurrentUser);
            return Encoding.UTF8.GetString(plain);
        }
        catch
        {
            // Cannot decrypt (e.g. config copied from another machine/user): treat as no key.
            return null;
        }
    }

    private sealed class FileModel
    {
        public string? ApiKey { get; set; }          // legacy (plain text) — read only
        public string? ApiKeyProtected { get; set; } // DPAPI-encrypted key (base64)
        public string? Model { get; set; }
        public int? BufferSeconds { get; set; }
    }
}
