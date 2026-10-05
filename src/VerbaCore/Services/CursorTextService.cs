using System.Collections.Concurrent;
using System.Runtime.InteropServices;
using System.Text;
using VerbaCore.Helpers;

namespace VerbaCore.Services;

/// <summary>
/// Extracts selected text from a captured application window using UIA, Office, and IAccessible2.
/// </summary>
/// <remarks>
/// All selection automation calls run on a dedicated MTA thread. They are cross-process and can take
/// hundreds of milliseconds, which would blow past the 300 ms low-level keyboard hook
/// timeout if executed on the hook or UI thread. The IUIAutomation object is created on
/// that thread too, so calls stay in-apartment instead of marshalling back to the UI thread.
/// </remarks>
public sealed class CursorTextService : IDisposable
{
    public const int SelectionTimeoutMs = 2000;
    private readonly BlockingCollection<Action> _queue = new();
    private readonly Thread _worker;
    private IUIAutomation _uia = null!;
    private CancellationTokenSource? _activeRequest;
    private bool _disposed;

    public CursorTextService()
    {
        _worker = new Thread(WorkerMain)
        {
            IsBackground = true,
            Name = "VerbaCore.Uia"
        };
        _worker.SetApartmentState(ApartmentState.MTA);
        _worker.Start();
    }

    private void WorkerMain()
    {
        try
        {
            try { _uia = UIA3.CreateAutomation(); }
            catch (COMException) { }
            foreach (var work in _queue.GetConsumingEnumerable())
            {
                try { work(); }
                catch (Exception ex) { System.Diagnostics.Debug.WriteLine($"[CursorTextService] {ex.GetType().Name}"); }
            }
        }
        finally
        {
            if (_uia != null && Marshal.IsComObject(_uia)) Marshal.ReleaseComObject(_uia);
            lock (_queue) _queue.Dispose();
        }
    }

    /// <summary>
    /// Queues a selected-text lookup on the automation thread. <paramref name="foregroundWindow"/>
    /// is the window captured before the overlay stole focus; pass <see cref="IntPtr.Zero"/> to
    /// resolve it at call time.
    /// </summary>
    public async Task<string?> GetSelectedTextAsync(IntPtr foregroundWindow, CancellationToken ct)
    {
        var target = SelectionTarget.Capture(foregroundWindow);
        using var request = CancellationTokenSource.CreateLinkedTokenSource(ct);
        request.CancelAfter(SelectionTimeoutMs);
        var token = request.Token;
        var tcs = new TaskCompletionSource<string?>(TaskCreationOptions.RunContinuationsAsynchronously);
        using var registration = token.Register(() => tcs.TrySetResult(null));
        lock (_queue)
        {
            if (_disposed || token.IsCancellationRequested || !target.IsValid) return null;
            _activeRequest?.Cancel();
            _activeRequest = request;
            _queue.Add(() =>
            {
                string? text = null;
                try
                {
                    if (!token.IsCancellationRequested) text = GetSelectedText(target, token);
                }
                finally { tcs.TrySetResult(token.IsCancellationRequested ? null : text); }
            });
        }
        try { return await tcs.Task.ConfigureAwait(false); }
        finally
        {
            lock (_queue)
                if (ReferenceEquals(_activeRequest, request)) _activeRequest = null;
        }
    }

    /// <summary>
    /// Warms up the automation stack so the first real lookup after a long idle period
    /// does not pay COM/accessibility initialization cost.
    /// </summary>
    public void PreWarm()
    {
        TryEnqueue(() =>
        {
            try { _ = _uia?.GetFocusedElement(); }
            catch (COMException) { }
        });
    }

    private string? GetSelectedText(SelectionTarget target, CancellationToken ct)
    {
        try
        {
            for (var attempt = 0; attempt < 4 && !ct.IsCancellationRequested && target.IsValid; attempt++)
            {
                var text = OfficeSelectionReader.TryRead(target, ct);
                if (text != null) return text;

                text = TryReadFocusedUia(target, ct);
                if (text != null) return text;

                var remaining = 384;
                foreach (var window in SelectionInterop.CandidateWindows(target)
                             .OrderByDescending(window => SelectionInterop.ClassName(window).StartsWith("Chrome_RenderWidgetHostHWND", StringComparison.Ordinal)))
                {
                    if (ct.IsCancellationRequested) return null;
                    if (_uia == null || remaining <= 0) break;
                    if (window != target.Window && window != target.FocusWindow
                        && !SelectionInterop.ClassName(window).StartsWith("Chrome_RenderWidgetHostHWND", StringComparison.Ordinal)) continue;
                    try
                    {
                        var root = _uia.ElementFromHandle(window);
                        if (root != null)
                        {
                            text = SearchDescendants(root, 0, ref remaining, ct);
                            if (text != null) return text;
                        }
                    }
                    catch (COMException) { }
                }
                text = AccessibleSelectionReader.TryRead(target, ct);
                if (text != null) return text;
                if (attempt < 3) Task.Delay(60, ct).GetAwaiter().GetResult();
            }
        }
        catch (Exception exception) when (exception is COMException or InvalidOperationException
            or OperationCanceledException or ArgumentException) { }
        return null;
    }

    private string? TryReadFocusedUia(SelectionTarget target, CancellationToken ct)
    {
        if (_uia == null || ct.IsCancellationRequested) return null;
        try
        {
            var root = _uia.ElementFromHandle(target.Window);
            return root == null ? null : TryFocusedAndAncestors(root, ct);
        }
        catch (COMException) { return null; }
    }

    private string? TryFocusedAndAncestors(IUIAutomationElement root, CancellationToken ct)
    {
        try
        {
            var focused = _uia.GetFocusedElement();
            if (focused == null) return null;

            var ancestors = new List<IUIAutomationElement>();
            var walker = _uia.RawViewWalker;
            for (var element = focused; element != null && ancestors.Count < 48 && !ct.IsCancellationRequested; element = walker.GetParentElement(element))
            {
                ancestors.Add(element);
                if (_uia.CompareElements(element, root) == 0) continue;
                for (var index = ancestors.Count - 1; index >= 0; index--)
                {
                    var text = TryGetSelectionText(ancestors[index]);
                    if (text != null) return text;
                }
                return null;
            }
            return null;
        }
        catch (COMException) { return null; }
        catch (InvalidOperationException) { return null; }
    }

    private string? SearchDescendants(IUIAutomationElement element, int depth, ref int remaining, CancellationToken ct)
    {
        if (ct.IsCancellationRequested || remaining-- <= 0) return null;
        try
        {
            var text = TryGetSelectionText(element);
            if (text != null) return text;
            if (depth >= 32) return null;

            var walker = _uia.RawViewWalker;
            var child = walker.GetFirstChildElement(element);
            while (child != null && remaining > 0 && !ct.IsCancellationRequested)
            {
                text = SearchDescendants(child, depth + 1, ref remaining, ct);
                if (text != null) return text;
                child = walker.GetNextSiblingElement(child);
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
            if (element.GetCurrentPropertyValue(30019) is true) return null;
            var iid = typeof(IUIAutomationTextPattern).GUID;
            var ptr = element.GetCurrentPatternAs(UIA3.UIA_TextPatternId, ref iid);
            if (ptr == IntPtr.Zero) return null;

            var tp = (IUIAutomationTextPattern)Marshal.GetObjectForIUnknown(ptr);
            Marshal.Release(ptr);

            var ranges = tp.GetSelection();
            if (ranges == null || ranges.Length == 0) return null;

            var result = new StringBuilder();
            for (var index = 0; index < Math.Min(ranges.Length, 32) && result.Length < OfficeSelectionReader.MaxTextLength; index++)
            {
                var range = ranges.GetElement(index);
                if (range == null || range.CompareEndpoints(0, range, 1) == 0) continue;
                if (result.Length > 0) result.Append('\n');
                var remaining = OfficeSelectionReader.MaxTextLength - result.Length;
                if (remaining > 0) result.Append(range.GetText(remaining));
            }
            return OfficeSelectionReader.Normalize(result.ToString());
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
        var result = new TaskCompletionSource<string?>(TaskCreationOptions.RunContinuationsAsynchronously);
        if (!TryEnqueue(() => result.TrySetResult(ReadTextUnderCursor()))) return null;
        return result.Task.Wait(SelectionTimeoutMs) ? result.Task.Result : null;
    }

    private string? ReadTextUnderCursor()
    {
        if (_uia == null) return null;
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

    private bool TryEnqueue(Action work)
    {
        lock (_queue)
        {
            if (_disposed) return false;
            _queue.Add(work);
            return true;
        }
    }

    public void Dispose()
    {
        lock (_queue)
        {
            if (_disposed) return;
            _disposed = true;
            _activeRequest?.Cancel();
            _queue.CompleteAdding();
        }
    }
}
