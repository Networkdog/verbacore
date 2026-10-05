using System.Globalization;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using VerbaCore.Helpers;

namespace VerbaCore.Services;

internal static class OfficeSelectionReader
{
    internal const int MaxTextLength = 2000;

    internal static string? TryRead(SelectionTarget target, CancellationToken ct)
    {
        foreach (var window in SelectionInterop.CandidateWindows(target))
        {
            if (ct.IsCancellationRequested || !target.IsValid) return null;
            var className = SelectionInterop.ClassName(window);
            if (className is not ("_WwG" or "EXCEL7" or "paneClassDC")) continue;
            if (target.FocusWindow != target.Window && !SelectionInterop.ContainsWindow(window, target.FocusWindow)) continue;
            if (className == "EXCEL7" && target.CaretWindow != IntPtr.Zero
                && SelectionInterop.ClassName(target.CaretWindow) != "EXCEL7") continue;
            try
            {
                if (SelectionInterop.AccessibleObjectFromWindow(window, SelectionInterop.NativeObjectModel,
                        SelectionInterop.DispatchId, out var native) < 0 || native == null) continue;
                using var scope = new ComScope();
                scope.Own(native);
                var text = className switch
                {
                    "_WwG" => ReadWord(native, scope),
                    "paneClassDC" => ReadPowerPoint(native, scope),
                    "EXCEL7" => ReadExcel(native, target, scope, ct),
                    _ => null
                };
                text = Normalize(text);
                if (text != null) return text;
            }
            catch (Exception exception) when (exception is COMException or TargetInvocationException
                or MissingMemberException or InvalidCastException or ArgumentException or InvalidOperationException) { }
        }
        return null;
    }

    private static string? ReadWord(object window, ComScope scope)
    {
        var selection = scope.Get(window, "Selection");
        if (scope.Number(selection, "Start") >= scope.Number(selection, "End")) return null;
        return scope.Get(selection, "Text") as string;
    }

    private static string? ReadPowerPoint(object window, ComScope scope)
    {
        var selection = scope.Get(window, "Selection");
        if (scope.Number(selection, "Type") != 3) return null;
        return scope.Get(scope.Get(selection, "TextRange"), "Text") as string;
    }

    private static string? ReadExcel(object window, SelectionTarget target, ComScope scope, CancellationToken ct)
    {
        var application = scope.Get(window, "Application");
        var active = scope.Get(application, "ActiveWindow");
        var handle = new IntPtr(unchecked((long)(uint)scope.Number(active, "Hwnd")));
        if (handle != target.Window) return null;
        var selection = scope.Get(application, "Selection");
        var areas = scope.Get(selection, "Areas");
        var text = new StringBuilder();
        var remainingCells = 256;
        var areaCount = Math.Min(scope.Number(areas, "Count"), 16);
        for (var areaIndex = 1; areaIndex <= areaCount && text.Length < MaxTextLength && remainingCells > 0; areaIndex++)
        {
            if (ct.IsCancellationRequested) return null;
            var area = scope.Get(areas, "Item", areaIndex);
            var rows = scope.Number(scope.Get(area, "Rows"), "Count");
            var columns = scope.Number(scope.Get(area, "Columns"), "Count");
            var cells = scope.Get(area, "Cells");
            if (text.Length > 0) text.AppendLine();
            for (var row = 1; row <= rows && remainingCells > 0 && text.Length < MaxTextLength; row++)
            {
                if (row > 1) text.AppendLine();
                for (var column = 1; column <= columns && remainingCells-- > 0 && text.Length < MaxTextLength; column++)
                {
                    if (ct.IsCancellationRequested) return null;
                    if (column > 1) text.Append('\t');
                    var cell = scope.Get(cells, "Item", row, column);
                    var value = scope.Get(cell, "Text") as string;
                    if (value != null) text.Append(value.AsSpan(0, Math.Min(value.Length, MaxTextLength - text.Length)));
                }
            }
        }
        return text.ToString();
    }

    internal static string? Normalize(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;
        text = text.Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n').Replace('\a', '\t').Trim();
        var length = Math.Min(text.Length, MaxTextLength);
        if (length > 0 && char.IsHighSurrogate(text[length - 1])) length--;
        return length == 0 ? null : text[..length];
    }

    private sealed class ComScope : IDisposable
    {
        private readonly List<object> _objects = [];

        internal void Own(object value)
        {
            if (Marshal.IsComObject(value)) _objects.Add(value);
        }

        internal object Get(object target, string name, params object[] arguments)
        {
            var value = target.GetType().InvokeMember(name, BindingFlags.GetProperty | BindingFlags.OptionalParamBinding,
                null, target, arguments, CultureInfo.InvariantCulture) ?? throw new InvalidOperationException("Missing Office selection object.");
            Own(value);
            return value;
        }

        internal int Number(object target, string name) => Convert.ToInt32(Get(target, name), CultureInfo.InvariantCulture);

        public void Dispose()
        {
            for (var index = _objects.Count - 1; index >= 0; index--)
                Marshal.ReleaseComObject(_objects[index]);
        }
    }
}