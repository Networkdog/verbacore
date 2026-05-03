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
            // 1) Pre-warm the foreground app's accessibility tree.
            //    For Electron/Chromium this triggers lazy a11y initialization
            //    (the same mechanism NVDA uses). Without this call, Chromium
            //    apps return null even though the user has text selected.
            IUIAutomationElement? hwndRoot = null;
            try
            {
                var fgHwnd = NativeMethods.GetForegroundWindow();
                if (fgHwnd != IntPtr.Zero)
                    hwndRoot = _uia.ElementFromHandle(fgHwnd);
            }
            catch (COMException) { /* tolerate */ }

            // 2) Fast path: focused element + its ancestors.
            var text = TryFocusedAndAncestors();
            if (text != null) return text;

            // 3) Chromium often exposes TextPattern on a Document descendant of
            //    the window root rather than an ancestor of the focused element.
            //    Search the window root's subtree for any element with a non-empty
            //    selection.
            if (hwndRoot != null)
            {
                text = SearchDescendants(hwndRoot, depth: 0, maxDepth: 8, siblingBudget: 64);
                if (text != null) return text;
            }

            // 4) On the first activation Chromium builds the a11y tree async.
            //    A single short retry catches that race without noticeable lag.
            Thread.Sleep(60);
            text = TryFocusedAndAncestors();
            if (text != null) return text;
            if (hwndRoot != null)
            {
                text = SearchDescendants(hwndRoot, depth: 0, maxDepth: 10, siblingBudget: 96);
                if (text != null) return text;
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

    private string? TryFocusedAndAncestors()
    {
        try
        {
            var focused = _uia.GetFocusedElement();
            if (focused == null) return null;

            var text = TryGetSelectionText(focused);
            if (text != null) return text;

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
        catch (COMException) { return null; }
        catch (InvalidOperationException) { return null; }
    }

    /// <summary>
    /// Bounded DFS over the UIA subtree looking for an element whose TextPattern
    /// reports a non-empty selection. Used as a fallback for Chromium/Electron
    /// where the document node isn't an ancestor of the focused element.
    /// </summary>
    private string? SearchDescendants(IUIAutomationElement element, int depth, int maxDepth, int siblingBudget)
    {
        try
        {
            var t = TryGetSelectionText(element);
            if (t != null) return t;
            if (depth >= maxDepth) return null;

            var walker = _uia.RawViewWalker;
            var child = walker.GetFirstChildElement(element);
            var visited = 0;
            while (child != null && visited < siblingBudget)
            {
                var r = SearchDescendants(child, depth + 1, maxDepth, siblingBudget);
                if (r != null) return r;
                child = walker.GetNextSiblingElement(child);
                visited++;
            }
            return null;
        }
        catch (COMException) { return null; }
        catch (InvalidOperationException) { return null; }
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
