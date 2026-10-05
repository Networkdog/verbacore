using System.Runtime.InteropServices;
using System.Text;

namespace VerbaCore.Helpers;

internal readonly record struct SelectionTarget(IntPtr Window, IntPtr FocusWindow, IntPtr CaretWindow, uint ProcessId)
{
    internal static SelectionTarget Capture(IntPtr window)
    {
        if (window == IntPtr.Zero) window = NativeMethods.GetForegroundWindow();
        window = SelectionInterop.GetAncestor(window, 2);
        var thread = NativeMethods.GetWindowThreadProcessId(window, out var process);
        if (thread == 0) return default;
        var info = new SelectionInterop.GuiThreadInfo { Size = Marshal.SizeOf<SelectionInterop.GuiThreadInfo>() };
        SelectionInterop.GetGUIThreadInfo(thread, ref info);
        var focus = SelectionInterop.ContainsWindow(window, info.Focus) ? info.Focus : window;
        var caret = SelectionInterop.ContainsWindow(window, info.Caret) ? info.Caret : IntPtr.Zero;
        return new SelectionTarget(window, focus, caret, process);
    }

    internal bool IsValid => Window != IntPtr.Zero
        && NativeMethods.GetWindowThreadProcessId(Window, out var process) != 0 && process == ProcessId;
}

internal static class SelectionInterop
{
    internal const uint NativeObjectModel = 0xFFFFFFF0;
    internal const uint ClientObject = 0xFFFFFFFC;
    internal static readonly Guid DispatchId = new("00020400-0000-0000-C000-000000000046");
    internal static readonly Guid AccessibleId = new("618736E0-3C3D-11CF-810C-00AA00389B71");
    internal static readonly Guid Accessible2Id = new("E89F726E-C4F4-4C19-BB19-B647D7FA8478");

    [StructLayout(LayoutKind.Sequential)]
    internal struct GuiThreadInfo
    {
        internal int Size;
        internal uint Flags;
        internal IntPtr Active;
        internal IntPtr Focus;
        internal IntPtr Capture;
        internal IntPtr MenuOwner;
        internal IntPtr MoveSize;
        internal IntPtr Caret;
        internal NativeMethods.RECT CaretRect;
    }

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool GetGUIThreadInfo(uint thread, ref GuiThreadInfo info);

    [DllImport("user32.dll")]
    internal static extern IntPtr GetAncestor(IntPtr window, uint flags);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsChild(IntPtr parent, IntPtr child);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetClassName(IntPtr window, StringBuilder name, int capacity);

    private delegate bool EnumWindowProc(IntPtr window, IntPtr parameter);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool EnumChildWindows(IntPtr parent, EnumWindowProc callback, IntPtr parameter);

    [DllImport("oleacc.dll")]
    internal static extern int AccessibleObjectFromWindow(IntPtr window, uint objectId, in Guid interfaceId,
        [MarshalAs(UnmanagedType.Interface)] out object? accessible);

    [DllImport("oleacc.dll")]
    internal static extern int AccessibleChildren(Accessibility.IAccessible parent, int start, int count,
        [Out, MarshalAs(UnmanagedType.LPArray, SizeParamIndex = 2)] object[] children, out int obtained);

    internal static bool ContainsWindow(IntPtr root, IntPtr window) => root != IntPtr.Zero && window != IntPtr.Zero
        && (root == window || IsChild(root, window));

    internal static string ClassName(IntPtr window)
    {
        var name = new StringBuilder(256);
        GetClassName(window, name, name.Capacity);
        return name.ToString();
    }

    internal static List<IntPtr> CandidateWindows(SelectionTarget target)
    {
        var windows = new List<IntPtr> { target.FocusWindow };
        if (target.Window != target.FocusWindow) windows.Add(target.Window);
        EnumChildWindows(target.Window, (window, _) =>
        {
            if (!windows.Contains(window)) windows.Add(window);
            return windows.Count < 128;
        }, IntPtr.Zero);
        return windows;
    }
}

[ComImport, Guid("6D5140C1-7436-11CE-8034-00AA006009FA")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IAccessibleServiceProvider
{
    [PreserveSig]
    int QueryService(in Guid service, in Guid interfaceId, out IntPtr result);
}

[ComImport, Guid("24FD2FFB-3AAD-4A08-8335-A3AD89C0FB4B")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IAccessibleText
{
    void AddSelection(int start, int end);
    [return: MarshalAs(UnmanagedType.BStr)]
    string GetAttributes(int offset, out int start, out int end);
    int CaretOffset { get; }
    int GetCharacterExtents(int offset, int coordinateType, out int left, out int top, out int width);
    int SelectionCount { get; }
    int GetOffsetAtPoint(int left, int top, int coordinateType);
    int GetSelection(int index, out int start);
    [return: MarshalAs(UnmanagedType.BStr)]
    string GetText(int start, int end);
}