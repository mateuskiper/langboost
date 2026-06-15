using System.Windows;

namespace LangBoost;

/// <summary>
/// Settings window (separate from the overlay because the overlay uses WS_EX_NOACTIVATE and
/// does not receive keyboard focus). Lets you set the Gemini key and the buffer length.
/// Applies the changes to the received <see cref="AppConfig"/> and persists it on save.
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
            BufferLabel.Text = $"Audio buffer: {seconds} s";
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
