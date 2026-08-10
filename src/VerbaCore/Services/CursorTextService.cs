using System.Collections.Concurrent;
using System.Runtime.InteropServices;
using VerbaCore.Helpers;

namespace VerbaCore.Services;

/// <summary>
/// Extracts selected text from the focused application using COM-based UIA3.
/// The managed System.Windows.Automation uses UIA2, which Chromium/Electron
/// does not support for TextPattern. COM UIA3 is the API used by NVDA and FlaUI.
/// </summary>
/// <remarks>
/// All automation calls run on a dedicated STA thread. They are cross-process and can take
/// hundreds of milliseconds, which would blow past the 300 ms low-level keyboard hook
/// timeout if executed on the hook or UI thread. The IUIAutomation object is created on
/// that thread too, so calls stay in-apartment instead of marshalling back to the UI thread.
/// </remarks>
public sealed class CursorTextService : IDisposable
{
    private readonly BlockingCollection<Action> _queue = new();
    private readonly Thread _worker;
    private IUIAutomation _uia = null!;
    private long _requestSeq;

    public CursorTextService()
    {
        _worker = new Thread(WorkerMain)
        {
            IsBackground = true,
            Name = "VerbaCore.Uia"
        };
        _worker.SetApartmentState(ApartmentState.STA);
        _worker.Start();
    }

    private void WorkerMain()
    {
        _uia = UIA3.CreateAutomation();
        foreach (var work in _queue.GetConsumingEnumerable())
        {
            try { work(); }
            catch (Exception ex) { System.Diagnostics.Debug.WriteLine($"[CursorTextService] {ex}"); }
        }
    }

    /// <summary>
    /// Queues a selected-text lookup on the automation thread. <paramref name="foregroundWindow"/>
    /// is the window captured before the overlay stole focus; pass <see cref="IntPtr.Zero"/> to
    /// resolve it at call time.
    /// </summary>
    public Task<string?> GetSelectedTextAsync(IntPtr foregroundWindow, CancellationToken ct)
    {
        var seq = Interlocked.Increment(ref _requestSeq);
        var tcs = new TaskCompletionSource<string?>(TaskCreationOptions.RunContinuationsAsynchronously);
        try
        {
            _queue.Add(() =>
            {
                // Drop stale requests so rapid CapsLock presses don't queue up behind a
                // slow lookup of a window the user has already moved on from.
                if (ct.IsCancellationRequested || Interlocked.Read(ref _requestSeq) != seq)
                    tcs.TrySetResult(null);
                else
                    tcs.TrySetResult(GetSelectedText(foregroundWindow));
            }, ct);
        }
        catch (Exception)
        {
            tcs.TrySetResult(null);
        }
        return tcs.Task;
    }

    /// <summary>
    /// Warms up the automation stack so the first real lookup after a long idle period
    /// does not pay COM/accessibility initialization cost.
    /// </summary>
    public void PreWarm()
    {
        try
        {
            _queue.Add(() =>
            {
                try { _ = _uia.GetFocusedElement(); }
                catch (COMException) { }
            });
        }
        catch (Exception) { /* queue already completed */ }
    }

    /// <summary>
    /// Pre-warms the UIA client so the first real <see cref="GetSelectedText"/> call does
    /// not pay the (multi-second) one-time COM/accessibility initialization cost on the
    /// critical path when the user first presses CapsLock. Best-effort; safe to call once
    /// at startup on the same (UI) thread that later calls <see cref="GetSelectedText"/>.
    /// </summary>
    public void WarmUp()
    {
        try
        {
            var fg = NativeMethods.GetForegroundWindow();
            if (fg != IntPtr.Zero)
            {
                var root = _uia.ElementFromHandle(fg);
                if (root != null)
                    _ = TryGetSelectionText(root);
            }

            var focused = _uia.GetFocusedElement();
            if (focused != null)
                _ = TryGetSelectionText(focused);
        }
        catch
        {
            // Warm-up is best-effort; ignore any failure.
        }
    }

    /// <summary>
    /// Gets the currently selected (highlighted) text from the focused application
    /// using COM UIA3 TextPattern. Works with native apps, browsers, and Electron/Chromium.
    /// Does not use the clipboard. Automation-thread only.
    /// </summary>
    private string? GetSelectedText(IntPtr foregroundWindow)
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
                var fgHwnd = foregroundWindow != IntPtr.Zero
                    ? foregroundWindow
                    : NativeMethods.GetForegroundWindow();
                if (fgHwnd != IntPtr.Zero)
                    hwndRoot = _uia.ElementFromHandle(fgHwnd);
            }
            catch (COMException) { /* tolerate */ }

            // 2) Fast path: focused element + its ancestors. This runs before the overlay
            //    has taken focus in practice, since the grab is queued at CapsLock-down.
            var text = TryFocusedAndAncestors();
            if (text != null) return text;

            // 3) Chromium often exposes TextPattern on a Document descendant of the window
            //    root rather than an ancestor of the focused element. The captured handle
            //    still points at the original app even once the overlay owns focus.
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

    public void Dispose()
    {
        _queue.CompleteAdding();
        _worker.Join(TimeSpan.FromSeconds(2));
        _queue.Dispose();
    }
}
