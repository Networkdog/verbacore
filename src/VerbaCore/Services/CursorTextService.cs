using System.Runtime.InteropServices;
using VerbaCore.Helpers;

namespace VerbaCore.Services;

/// <summary>
/// Extracts selected text from the focused application using COM-based UIA3.
/// The managed System.Windows.Automation uses UIA2, which Chromium/Electron
/// does not support for TextPattern. COM UIA3 is the API used by NVDA and FlaUI.
/// </summary>
public sealed class CursorTextService
{
    private readonly IUIAutomation _uia = UIA3.CreateAutomation();

    /// <summary>
    /// Gets the currently selected (highlighted) text from the focused application
    /// using COM UIA3 TextPattern. Works with native apps, browsers, and Electron/Chromium.
    /// Does not use the clipboard.
    /// </summary>
    public string? GetSelectedText()
    {
        try
        {
            var focused = _uia.GetFocusedElement();
            if (focused == null) return null;

            // Try TextPattern on the focused element
            var text = TryGetSelectionText(focused);
            if (text != null) return text;

            // Walk up the UIA tree — Chromium exposes TextPattern on Document/Pane parent
            var walker = _uia.RawViewWalker;
            var parent = walker.GetParentElement(focused);
            for (var depth = 0; parent != null && depth < 8; depth++)
            {
                text = TryGetSelectionText(parent);
                if (text != null) return text;
                parent = walker.GetParentElement(parent);
            }

            return null;
        }
        catch (COMException)
        {
            return null;
        }
        catch
        {
            return null;
        }
    }

    private static string? TryGetSelectionText(IUIAutomationElement element)
    {
        try
        {
            var iid = typeof(IUIAutomationTextPattern).GUID;
            var ptr = element.GetCurrentPatternAs(UIA3.UIA_TextPatternId, ref iid);
            if (ptr == IntPtr.Zero) return null;

            var tp = (IUIAutomationTextPattern)Marshal.GetObjectForIUnknown(ptr);
            Marshal.Release(ptr);

            var ranges = tp.GetSelection();
            if (ranges == null || ranges.Length == 0) return null;

            var range = ranges.GetElement(0);
            if (range == null) return null;

            var text = range.GetText(2000)?.Trim();
            return string.IsNullOrEmpty(text) ? null : text;
        }
        catch (COMException)
        {
            return null;
        }
        catch (InvalidOperationException)
        {
            return null;
        }
    }

    public string? GetTextUnderCursor()
    {
        try
        {
            NativeMethods.GetCursorPos(out var point);
            var tagPt = new tagPOINT { x = point.X, y = point.Y };

            var element = _uia.ElementFromPoint(tagPt);
            if (element == null) return null;

            // Try selected text
            var text = TryGetSelectionText(element);
            if (text != null) return text;

            // Fallback: Name property (via UIA property ID 30005 = UIA_NamePropertyId)
            try
            {
                var name = element.GetCurrentPropertyValue(30005) as string;
                return string.IsNullOrWhiteSpace(name) ? null : name.Trim();
            }
            catch (COMException) { return null; }
        }
        catch (COMException)
        {
            return null;
        }
        catch
        {
            return null;
        }
    }
}
