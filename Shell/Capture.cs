using System.Runtime.InteropServices;
using Avalonia.Controls;
using Avalonia.Threading;
using SirWordALot.Core;

namespace SirWordALot.Shell;

/// <summary>A system-wide hotkey that pulls the clipboard's word into the app.
/// Windows only; a no-op elsewhere, and never fatal when registration fails.</summary>
public sealed class Capture(Window window, Settings settings, Action<string> onCapture) : IDisposable
{
    private const int WmHotkey = 0x0312;
    private const int HotkeyId = 0xA17;

    private nint _handle;
    private bool _registered;

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool RegisterHotKey(nint hWnd, int id, uint fsModifiers, uint vk);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool UnregisterHotKey(nint hWnd, int id);

    public bool TryRegister()
    {
        if (!OperatingSystem.IsWindows() || !settings.CaptureEnabled) return false;
        if (window.TryGetPlatformHandle()?.Handle is not { } handle || handle == 0) return false;

        var (modifiers, key) = Parse(settings.CaptureHotkey);
        if (key == 0) return false;

        _handle = handle;

        try
        {
            _registered = RegisterHotKey(_handle, HotkeyId, modifiers, key);
        }
        catch (DllNotFoundException)
        {
            return false;
        }

        if (_registered) Win32Properties.AddWndProcHookCallback(window, Hook);
        return _registered;
    }

    private nint Hook(nint hWnd, uint msg, nint wParam, nint lParam, ref bool handled)
    {
        if (msg != WmHotkey || wParam != HotkeyId) return 0;
        handled = true;

        Dispatcher.UIThread.Post(async () =>
        {
            var text = window.Clipboard is { } clipboard ? await clipboard.GetTextAsync() : null;
            var word = (text ?? "").Trim()
                .Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries)
                .FirstOrDefault() ?? "";
            if (word.Length > 0) onCapture(word);
        });

        return 0;
    }

    /// <summary>Parses strings like <c>Ctrl+Alt+W</c> into Win32 modifier and virtual-key codes.</summary>
    private static (uint Modifiers, uint Key) Parse(string hotkey)
    {
        uint modifiers = 0;
        uint key = 0;

        foreach (var part in hotkey.Split('+', StringSplitOptions.RemoveEmptyEntries))
        {
            switch (part.Trim().ToLowerInvariant())
            {
                case "ctrl" or "control": modifiers |= 0x0002; break;
                case "alt": modifiers |= 0x0001; break;
                case "shift": modifiers |= 0x0004; break;
                case "win": modifiers |= 0x0008; break;
                default:
                    var token = part.Trim().ToUpperInvariant();
                    if (token.Length == 1 && char.IsLetterOrDigit(token[0])) key = token[0];
                    break;
            }
        }

        return modifiers == 0 ? (0, 0) : (modifiers, key);
    }

    public void Dispose()
    {
        if (_registered && _handle != 0) UnregisterHotKey(_handle, HotkeyId);
        _registered = false;
    }
}
