using System.Runtime.InteropServices;
using System.Text;

namespace VerbaCore.Helpers;

internal static class NativeMethods
{
    // Cursor position
    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool GetCursorPos(out POINT lpPoint);

    [StructLayout(LayoutKind.Sequential)]
    public struct POINT
    {
        public int X;
        public int Y;
    }

    // Window activation
    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool SetForegroundWindow(IntPtr hWnd);

    [DllImport("user32.dll")]
    public static extern IntPtr SetFocus(IntPtr hWnd);

    [DllImport("user32.dll")]
    public static extern IntPtr GetFocus();

    [DllImport("user32.dll")]
    private static extern short GetAsyncKeyState(int virtualKey);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern uint SendInput(uint count, INPUT[] inputs, int size);

    [StructLayout(LayoutKind.Sequential)]
    private struct INPUT
    {
        public uint Type;
        public INPUTUNION Data;
    }

    [StructLayout(LayoutKind.Explicit)]
    private struct INPUTUNION
    {
        [FieldOffset(0)] public KEYBDINPUT Keyboard;
        [FieldOffset(0)] public MOUSEINPUT Mouse;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct KEYBDINPUT
    {
        public ushort VirtualKey;
        public ushort ScanCode;
        public uint Flags;
        public uint Time;
        public UIntPtr ExtraInfo;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MOUSEINPUT
    {
        public int DeltaX;
        public int DeltaY;
        public uint MouseData;
        public uint Flags;
        public uint Time;
        public UIntPtr ExtraInfo;
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr OpenInputDesktop(uint flags, [MarshalAs(UnmanagedType.Bool)] bool inherit, uint access);

    [DllImport("user32.dll")]
    private static extern IntPtr GetThreadDesktop(uint threadId);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetUserObjectInformation(IntPtr handle, int index, StringBuilder value, uint length, out uint required);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CloseDesktop(IntPtr desktop);

    internal static bool IsInputDesktopCurrent()
    {
        var desktop = OpenInputDesktop(0, false, 0x0001);
        if (desktop == IntPtr.Zero) return false;
        try
        {
            var inputName = new StringBuilder(256);
            var currentName = new StringBuilder(256);
            return GetUserObjectInformation(desktop, 2, inputName, 512, out _)
                && GetUserObjectInformation(GetThreadDesktop(GetCurrentThreadId()), 2, currentName, 512, out _)
                && inputName.ToString() == currentName.ToString();
        }
        finally
        {
            CloseDesktop(desktop);
        }
    }

    internal static bool TryUnlockForegroundForQuickTap()
    {
        if (!IsInputDesktopCurrent()) return false;
        foreach (var virtualKey in new[] { 0x10, 0x11, 0x12, 0x14, 0x5B, 0x5C })
            if ((GetAsyncKeyState(virtualKey) & 0x8000) != 0) return false;

        var key = new KEYBDINPUT { VirtualKey = 0xA4, ScanCode = 0x38, ExtraInfo = new UIntPtr(0x56434647) };
        INPUT[] inputs =
        [
            new() { Type = 1, Data = new INPUTUNION { Keyboard = key } },
            new() { Type = 1, Data = new INPUTUNION { Keyboard = key } }
        ];
        inputs[1].Data.Keyboard.Flags = KEYEVENTF_KEYUP;
        var sent = SendInput(2, inputs, Marshal.SizeOf<INPUT>());
        if (sent == 1) SendInput(1, [inputs[1]], Marshal.SizeOf<INPUT>());
        return sent == 2;
    }

    public const int DWMWA_CLOAKED = 14;
    public const int DWM_CLOAKED_SHELL = 2;

    [DllImport("dwmapi.dll")]
    public static extern int DwmGetWindowAttribute(IntPtr hwnd, int attribute, out int value, int size);

    // Low-level keyboard hook
    public const int WH_KEYBOARD_LL = 13;
    public const int WM_KEYDOWN = 0x0100;
    public const int WM_KEYUP = 0x0101;
    public const int WM_SYSKEYDOWN = 0x0104;
    public const int WM_SYSKEYUP = 0x0105;
    public const int VK_CAPITAL = 0x14;
    public const uint LLKHF_INJECTED = 0x10;

    public delegate IntPtr LowLevelKeyboardProc(int nCode, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll", SetLastError = true)]
    public static extern IntPtr SetWindowsHookEx(int idHook, LowLevelKeyboardProc lpfn, IntPtr hMod, uint dwThreadId);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool UnhookWindowsHookEx(IntPtr hhk);

    [DllImport("user32.dll")]
    public static extern IntPtr CallNextHookEx(IntPtr hhk, int nCode, IntPtr wParam, IntPtr lParam);

    [DllImport("kernel32.dll", CharSet = CharSet.Auto, SetLastError = true)]
    public static extern IntPtr GetModuleHandle(string? lpModuleName);

    // Message loop — required by the dedicated hook thread. A WH_KEYBOARD_LL hook is
    // dispatched on the thread that installed it, so that thread must pump messages.
    public const int WM_QUIT = 0x0012;

    [StructLayout(LayoutKind.Sequential)]
    public struct MSG
    {
        public IntPtr hwnd;
        public uint message;
        public IntPtr wParam;
        public IntPtr lParam;
        public uint time;
        public POINT pt;
    }

    [DllImport("user32.dll")]
    public static extern int GetMessage(out MSG lpMsg, IntPtr hWnd, uint wMsgFilterMin, uint wMsgFilterMax);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool TranslateMessage(ref MSG lpMsg);

    [DllImport("user32.dll")]
    public static extern IntPtr DispatchMessage(ref MSG lpMsg);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool PostThreadMessage(uint idThread, uint msg, IntPtr wParam, IntPtr lParam);

    [StructLayout(LayoutKind.Sequential)]
    public struct KBDLLHOOKSTRUCT
    {
        public uint vkCode;
        public uint scanCode;
        public uint flags;
        public uint time;
        public IntPtr dwExtraInfo;
    }

    // CapsLock state control
    [DllImport("user32.dll")]
    public static extern short GetKeyState(int nVirtKey);

    [DllImport("user32.dll")]
    public static extern void keybd_event(byte bVk, byte bScan, uint dwFlags, UIntPtr dwExtraInfo);

    public const uint KEYEVENTF_KEYUP = 0x0002;

    /// <summary>
    /// Returns true if CapsLock toggle state is ON.
    /// </summary>
    public static bool IsCapsLockOn() => (GetKeyState(VK_CAPITAL) & 0x0001) != 0;

    /// <summary>
    /// Simulates a CapsLock press+release to toggle it off.
    /// </summary>
    public static void ToggleCapsLockOff()
    {
        if (!IsCapsLockOn()) return;
        keybd_event((byte)VK_CAPITAL, 0x45, 0, UIntPtr.Zero);
        keybd_event((byte)VK_CAPITAL, 0x45, KEYEVENTF_KEYUP, UIntPtr.Zero);
    }

    // Foreground window / thread attachment (for forcing focus)
    [DllImport("user32.dll")]
    public static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll")]
    public static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);

    [DllImport("kernel32.dll")]
    public static extern uint GetCurrentThreadId();

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool AttachThreadInput(uint idAttach, uint idAttachTo, bool fAttach);

    // Low-level mouse hook
    public const int WH_MOUSE_LL = 14;
    public const int WM_LBUTTONDOWN = 0x0201;
    public const int WM_RBUTTONDOWN = 0x0204;
    public const int WM_MBUTTONDOWN = 0x0207;
    public const int WM_NCLBUTTONDOWN = 0x00A1;

    public delegate IntPtr LowLevelMouseProc(int nCode, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll", SetLastError = true)]
    public static extern IntPtr SetWindowsHookEx(int idHook, LowLevelMouseProc lpfn, IntPtr hMod, uint dwThreadId);

    [StructLayout(LayoutKind.Sequential)]
    public struct MSLLHOOKSTRUCT
    {
        public POINT pt;
        public uint mouseData;
        public uint flags;
        public uint time;
        public IntPtr dwExtraInfo;
    }

    // Window rect
    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool GetWindowRect(IntPtr hWnd, out RECT lpRect);

    [StructLayout(LayoutKind.Sequential)]
    public struct RECT
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    // Icon cleanup
    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool DestroyIcon(IntPtr hIcon);

    // Shell tray icon
    [DllImport("shell32.dll")]
    public static extern IntPtr ExtractIcon(IntPtr hInst, string lpszExeFileName, int nIconIndex);

    /// <summary>
    /// Cached module handle for the main module — avoids Process.GetCurrentProcess() + MainModule
    /// allocation on every hook install/reinstall.
    /// </summary>
    private static IntPtr s_cachedModuleHandle;
    public static IntPtr CachedModuleHandle
    {
        get
        {
            if (s_cachedModuleHandle == IntPtr.Zero)
            {
                using var process = System.Diagnostics.Process.GetCurrentProcess();
                using var module = process.MainModule!;
                s_cachedModuleHandle = GetModuleHandle(module.ModuleName);
            }
            return s_cachedModuleHandle;
        }
    }
}
