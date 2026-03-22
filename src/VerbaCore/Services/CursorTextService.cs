using System.Windows;
using System.Windows.Automation;
using VerbaCore.Helpers;

namespace VerbaCore.Services;

public sealed class CursorTextService
{
    /// <summary>
    /// Gets the currently selected (highlighted) text from the focused application
    /// using UI Automation. Does not use the clipboard.
    /// </summary>
    public string? GetSelectedText()
    {
        try
        {
            var focused = AutomationElement.FocusedElement;
            if (focused == null) return null;

            // Try TextPattern (richest — works in browsers, editors, Office)
            if (focused.TryGetCurrentPattern(TextPattern.Pattern, out var tpObj)
                && tpObj is TextPattern tp)
            {
                var selection = tp.GetSelection();
                if (selection.Length > 0)
                {
                    var text = selection[0].GetText(2000).Trim();
                    if (!string.IsNullOrEmpty(text))
                        return text;
                }
            }

            return null;
        }
        catch
        {
            return null;
        }
    }

    public string? GetTextUnderCursor()
    {
        try
        {
            NativeMethods.GetCursorPos(out var point);

            var element = AutomationElement.FromPoint(new Point(point.X, point.Y));
            if (element == null) return null;

            // Try TextPattern first (richest text extraction)
            if (element.TryGetCurrentPattern(TextPattern.Pattern, out var textPatternObj)
                && textPatternObj is TextPattern textPattern)
            {
                var selection = textPattern.GetSelection();
                if (selection.Length > 0)
                {
                    var selectedText = selection[0].GetText(-1).Trim();
                    if (!string.IsNullOrEmpty(selectedText))
                        return selectedText;
                }

                // If no selection, get all visible text
                var visibleRanges = textPattern.GetVisibleRanges();
                if (visibleRanges.Length > 0)
                {
                    var visibleText = visibleRanges[0].GetText(200).Trim();
                    if (!string.IsNullOrEmpty(visibleText))
                        return ExtractWordAtPosition(visibleText);
                }
            }

            // Fallback: try ValuePattern
            if (element.TryGetCurrentPattern(ValuePattern.Pattern, out var valuePatternObj)
                && valuePatternObj is ValuePattern valuePattern)
            {
                var value = valuePattern.Current.Value?.Trim();
                if (!string.IsNullOrEmpty(value))
                    return value;
            }

            // Fallback: try Name property
            var name = element.Current.Name?.Trim();
            return string.IsNullOrEmpty(name) ? null : name;
        }
        catch
        {
            return null;
        }
    }

    private static string? ExtractWordAtPosition(string text)
    {
        // Return the first meaningful word or short phrase
        if (text.Length <= 50)
            return text;

        // Find first word boundary
        var spaceIndex = text.IndexOf(' ', 50);
        return spaceIndex > 0 ? text[..spaceIndex] : text[..50];
    }
}
