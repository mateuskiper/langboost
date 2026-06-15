using System.Windows;

namespace LangBoost;

/// <summary>
/// Janela de configurações (separada do overlay porque o overlay usa WS_EX_NOACTIVATE e
/// não recebe foco de teclado). Permite definir a chave do Gemini e o tamanho do buffer.
/// Aplica as mudanças no <see cref="AppConfig"/> recebido e o persiste ao salvar.
/// </summary>
public partial class SettingsWindow : Window
{
    private readonly AppConfig _config;

    public SettingsWindow(AppConfig config)
    {
        _config = config;
        InitializeComponent();

        ApiKeyBox.Password = _config.ApiKey;
        BufferSlider.Value = _config.BufferSeconds;
        UpdateBufferLabel(_config.BufferSeconds);

        if (_config.ApiKeyFromEnv)
            EnvWarning.Visibility = Visibility.Visible;

        Loaded += (_, _) => ApiKeyBox.Focus();
    }

    private void OnBufferChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        => UpdateBufferLabel((int)e.NewValue);

    private void UpdateBufferLabel(int seconds)
    {
        if (BufferLabel is not null)
            BufferLabel.Text = $"Tempo de áudio (buffer): {seconds} s";
    }

    private void OnSave(object sender, RoutedEventArgs e)
    {
        _config.ApiKey = ApiKeyBox.Password.Trim();
        _config.BufferSeconds = (int)BufferSlider.Value;
        _config.Save();

        DialogResult = true;
        Close();
    }

    private void OnCancel(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }
}
