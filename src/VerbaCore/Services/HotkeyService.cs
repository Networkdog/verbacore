using System.Windows.Input;
using NHotkey;
using NHotkey.Wpf;

namespace VerbaCore.Services;

public sealed class HotkeyService : IDisposable
{
    private const string HotkeyName = "VerbaCoreActivate";

    public event EventHandler? HotkeyPressed;

    public void Register(string hotkey = "Ctrl+Alt+V")
    {
        try
        {
            Unregister();
            ParseHotkey(hotkey, out var key, out var modifiers);
            HotkeyManager.Current.AddOrReplace(HotkeyName, key, modifiers, OnHotkeyPressed);
        }
        catch (Exception)
        {
            // Hotkey might already be registered by another app
        }
    }

    public void Unregister()
    {
        try
        {
            HotkeyManager.Current.Remove(HotkeyName);
        }
        catch
        {
            // Ignore if not registered
        }
    }

    private void OnHotkeyPressed(object? sender, HotkeyEventArgs e)
    {
        HotkeyPressed?.Invoke(this, EventArgs.Empty);
        e.Handled = true;
    }

    private static void ParseHotkey(string hotkey, out Key key, out ModifierKeys modifiers)
    {
        modifiers = ModifierKeys.None;
        key = Key.V;

        var parts = hotkey.Split('+');
        foreach (var part in parts)
        {
            var trimmed = part.Trim();
            switch (trimmed.ToUpperInvariant())
            {
                case "CTRL":
                case "CONTROL":
                    modifiers |= ModifierKeys.Control;
                    break;
                case "ALT":
                    modifiers |= ModifierKeys.Alt;
                    break;
                case "SHIFT":
                    modifiers |= ModifierKeys.Shift;
                    break;
                case "WIN":
                case "WINDOWS":
                    modifiers |= ModifierKeys.Windows;
                    break;
                default:
                    if (Enum.TryParse<Key>(trimmed, ignoreCase: true, out var parsedKey))
                        key = parsedKey;
                    break;
            }
        }
    }

    public void Dispose()
    {
        Unregister();
    }
}
