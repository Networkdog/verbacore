using System.Runtime.InteropServices;
using System.Text;
using Accessibility;
using VerbaCore.Helpers;

namespace VerbaCore.Services;

internal static class AccessibleSelectionReader
{
    internal static string? TryRead(SelectionTarget target, CancellationToken ct)
    {
        var remaining = 128;
        foreach (var window in SelectionInterop.CandidateWindows(target)
                 .OrderByDescending(window => SelectionInterop.ClassName(window).StartsWith("Chrome_RenderWidgetHostHWND", StringComparison.Ordinal)))
        {
            if (ct.IsCancellationRequested || remaining <= 0 || !target.IsValid) return null;
            var className = SelectionInterop.ClassName(window);
            if (window != target.FocusWindow && window != target.Window
                && !className.StartsWith("Chrome_RenderWidgetHostHWND", StringComparison.Ordinal)) continue;
            var owned = new List<object>();
            try
            {
                if (className.StartsWith("Chrome", StringComparison.Ordinal))
                {
                    SelectionInterop.AccessibleObjectFromWindow(window, 1, SelectionInterop.AccessibleId, out var detected);
                    if (detected != null && Marshal.IsComObject(detected)) owned.Add(detected);
                }
                if (SelectionInterop.AccessibleObjectFromWindow(window, SelectionInterop.ClientObject,
                        SelectionInterop.AccessibleId, out var root) < 0 || root is not IAccessible accessible) continue;
                owned.Add(root);
                var pending = new Stack<IAccessible>();
                var visited = new HashSet<IAccessible>();
                pending.Push(accessible);
                while (pending.TryPop(out var current) && remaining-- > 0 && !ct.IsCancellationRequested)
                {
                    if (!visited.Add(current)) continue;
                    try
                    {
                        if (current.get_accState(0) is int state && (state & 0x20000000) != 0) continue;
                        var text = ReadText(current, ct);
                        if (text != null) return text;

                        var count = Math.Clamp(current.accChildCount, 0, Math.Min(64, Math.Max(0, remaining - pending.Count)));
                        if (count > 0)
                        {
                            var children = new object[count];
                            if (SelectionInterop.AccessibleChildren(current, 0, count, children, out var obtained) >= 0)
                                for (var index = Math.Min(obtained, count) - 1; index >= 0; index--)
                                    Add(children[index], current, pending, owned);
                        }
                        Add(current.accFocus, current, pending, owned);
                    }
                    catch (Exception exception) when (exception is COMException or InvalidCastException or ArgumentException) { }
                }
            }
            catch (Exception exception) when (exception is COMException or InvalidCastException or ArgumentException) { }
            finally
            {
                for (var index = owned.Count - 1; index >= 0; index--)
                    if (Marshal.IsComObject(owned[index])) Marshal.ReleaseComObject(owned[index]);
            }
        }
        return null;
    }

    private static void Add(object? child, IAccessible parent, Stack<IAccessible> pending, List<object> owned)
    {
        try
        {
            if (child is int childId && childId != 0) child = parent.get_accChild(childId);
            if (child is not IAccessible accessible) return;
            owned.Add(accessible);
            pending.Push(accessible);
        }
        catch (COMException) { }
    }

    private static string? ReadText(IAccessible accessible, CancellationToken ct)
    {
        if (accessible is not IAccessibleServiceProvider provider) return null;
        provider.QueryService(SelectionInterop.AccessibleId, SelectionInterop.Accessible2Id, out var identity);
        if (identity != IntPtr.Zero) Marshal.Release(identity);
        var interfaceId = typeof(IAccessibleText).GUID;
        if (provider.QueryService(SelectionInterop.AccessibleId, interfaceId, out var pointer) < 0
            || pointer == IntPtr.Zero) return null;
        IAccessibleText pattern;
        try { pattern = (IAccessibleText)Marshal.GetObjectForIUnknown(pointer); }
        finally { Marshal.Release(pointer); }
        try
        {
            var result = new StringBuilder();
            var count = Math.Clamp(pattern.SelectionCount, 0, 32);
            for (var index = 0; index < count && result.Length < OfficeSelectionReader.MaxTextLength; index++)
            {
                if (ct.IsCancellationRequested) return null;
                var end = pattern.GetSelection(index, out var start);
                if (start < 0 || end <= start) continue;
                if (result.Length > 0) result.AppendLine();
                var length = Math.Min(end - start, OfficeSelectionReader.MaxTextLength - result.Length);
                if (length > 0)
                {
                    var text = pattern.GetText(start, start + length);
                    if (text.Contains('\uFFFC')) return null;
                    result.Append(text);
                }
            }
            return OfficeSelectionReader.Normalize(result.ToString());
        }
        finally
        {
            Marshal.ReleaseComObject(pattern);
        }
    }
}