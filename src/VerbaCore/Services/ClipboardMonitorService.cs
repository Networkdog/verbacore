using System.Windows;
using System.Windows.Interop;
using VerbaCore.Helpers;

namespace VerbaCore.Services;

public sealed class ClipboardMonitorService : IDisposable
{
    private HwndSource? _hwndSource;
    private bool _isMonitoring;

    public event EventHandler<string>? ClipboardTextChanged;

    public bool IsMonitoring => _isMonitoring;

    public void Start(Window window)
    {
        if (_isMonitoring) return;

        var helper = new WindowInteropHelper(window);
        _hwndSource = HwndSource.FromHwnd(helper.Handle);
        _hwndSource?.AddHook(WndProc);

        NativeMethods.AddClipboardFormatListener(helper.Handle);
        _isMonitoring = true;
    }

    public void Stop()
    {
        if (!_isMonitoring || _hwndSource == null) return;

        NativeMethods.RemoveClipboardFormatListener(_hwndSource.Handle);
        _hwndSource.RemoveHook(WndProc);
        _isMonitoring = false;
    }

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == NativeMethods.WM_CLIPBOARDUPDATE)
        {
            if (Clipboard.ContainsText())
            {
                var text = Clipboard.GetText().Trim();
                if (!string.IsNullOrEmpty(text) && text.Length <= 2000)
                {
                    ClipboardTextChanged?.Invoke(this, text);
                }
            }
            handled = true;
        }
        return IntPtr.Zero;
    }

    public void Dispose()
    {
        Stop();
    }
}
