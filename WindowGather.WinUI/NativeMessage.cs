using System.Runtime.InteropServices;

namespace WindowGather.WinUI;

internal static class NativeMessage
{
    public static void Show(string text, string title) => MessageBox(0, text, title, 0x10);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int MessageBox(nint window, string text, string title, uint flags);
    public static uint GetWindowDpi(nint window) => GetDpiForWindow(window);
    [DllImport("user32.dll")]
    private static extern uint GetDpiForWindow(nint window);
    public static void RequestSystemClose(nint window) => PostMessage(window, 0x0112, 0xF060, 0);
    [DllImport("user32.dll")]
    private static extern bool PostMessage(nint window, uint message, nuint wParam, nint lParam);
}
