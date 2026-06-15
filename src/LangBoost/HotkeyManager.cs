using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace LangBoost;

/// <summary>
/// Registra um atalho global do Windows (RegisterHotKey) que dispara mesmo com o
/// navegador em foco ou em tela cheia. Padrão: Ctrl+Shift+Space.
/// </summary>
public sealed class HotkeyManager : IDisposable
{
    public const uint MOD_CONTROL = 0x0002;
    public const uint MOD_SHIFT = 0x0004;
    public const uint MOD_NOREPEAT = 0x4000;

    private const int WM_HOTKEY = 0x0312;
    private const int HotkeyId = 0xB001;

    [DllImport("user32.dll")]
    private static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);

    [DllImport("user32.dll")]
    private static extern bool UnregisterHotKey(IntPtr hWnd, int id);

    private readonly IntPtr _hwnd;
    private readonly HwndSource _source;

    public event Action? Triggered;

    public HotkeyManager(Window window, uint modifiers, uint virtualKey)
    {
        _source = (HwndSource)PresentationSource.FromVisual(window)
                  ?? throw new InvalidOperationException("A janela ainda não tem handle. Crie o HotkeyManager após OnSourceInitialized.");
        _hwnd = _source.Handle;
        _source.AddHook(WndProc);

        if (!RegisterHotKey(_hwnd, HotkeyId, modifiers | MOD_NOREPEAT, virtualKey))
            throw new InvalidOperationException("Não foi possível registrar o atalho global (talvez já esteja em uso).");
    }

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == WM_HOTKEY && wParam.ToInt32() == HotkeyId)
        {
            Triggered?.Invoke();
            handled = true;
        }
        return IntPtr.Zero;
    }

    public void Dispose()
    {
        UnregisterHotKey(_hwnd, HotkeyId);
        _source.RemoveHook(WndProc);
    }
}
