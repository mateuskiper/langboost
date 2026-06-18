using System.Runtime.InteropServices;

namespace LangBoost;

/// <summary>
/// Toggles play/pause of the active media (browser video players: YouTube/Netflix/Prime)
/// by injecting the hardware media key VK_MEDIA_PLAY_PAUSE. Chrome (and other Chromium
/// browsers, with "Hardware Media Key Handling" on by default) route this to the active
/// MediaSession regardless of focus, fullscreen or held modifiers — which is why this is
/// far more reliable than a synthetic Space (Space only pauses YouTube when the player
/// itself has focus; otherwise it scrolls the page).
/// </summary>
public static class MediaControl
{
    private const int INPUT_KEYBOARD = 1;
    private const uint KEYEVENTF_KEYUP = 0x0002;
    private const uint KEYEVENTF_EXTENDEDKEY = 0x0001;

    private const ushort VK_MEDIA_PLAY_PAUSE = 0xB3;

    [StructLayout(LayoutKind.Sequential)]
    private struct INPUT
    {
        public int type;
        public InputUnion u;
    }

    [StructLayout(LayoutKind.Explicit)]
    private struct InputUnion
    {
        [FieldOffset(0)] public MOUSEINPUT mi;
        [FieldOffset(0)] public KEYBDINPUT ki;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct KEYBDINPUT
    {
        public ushort wVk;
        public ushort wScan;
        public uint dwFlags;
        public uint time;
        public IntPtr dwExtraInfo;
    }

    // Largest member of the INPUT union — its size is what makes sizeof(INPUT) == 40 on
    // x64. Without it the union (and cbSize) is too small and SendInput fails with error 87.
    [StructLayout(LayoutKind.Sequential)]
    private struct MOUSEINPUT
    {
        public int dx;
        public int dy;
        public uint mouseData;
        public uint dwFlags;
        public uint time;
        public IntPtr dwExtraInfo;
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern uint SendInput(uint nInputs, INPUT[] pInputs, int cbSize);

    /// <summary>Sends the media Play/Pause key (down + up) to toggle the active player.</summary>
    public static void TogglePlayPause()
    {
        var inputs = new[]
        {
            Key(VK_MEDIA_PLAY_PAUSE, KEYEVENTF_EXTENDEDKEY),
            Key(VK_MEDIA_PLAY_PAUSE, KEYEVENTF_EXTENDEDKEY | KEYEVENTF_KEYUP),
        };

        SendInput((uint)inputs.Length, inputs, Marshal.SizeOf<INPUT>());
    }

    private static INPUT Key(ushort vk, uint flags) => new()
    {
        type = INPUT_KEYBOARD,
        u = new InputUnion { ki = new KEYBDINPUT { wVk = vk, dwFlags = flags } }
    };
}
