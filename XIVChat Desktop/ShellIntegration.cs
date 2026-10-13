using System;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using Microsoft.UI.Windowing;

namespace XIVChat_Desktop;

/// <summary>
/// Notification-area icon and the global show/hide shortcut. Both are attached to the main window's handle,
/// which keeps receiving messages while the window is hidden.
/// </summary>
public sealed class ShellIntegration : IDisposable {
    internal const uint ModAlt = 0x1, ModControl = 0x2, ModShift = 0x4, ModWin = 0x8;
    private const uint ModNoRepeat = 0x4000;
    private const uint WmHotkey = 0x0312, WmNull = 0x0000, WmLButtonUp = 0x0202, WmRButtonUp = 0x0205, WmDestroy = 0x0002;
    private const uint TrayCallback = 0x8000 + 0x51; // WM_APP + 0x51
    private const uint NimAdd = 0, NimModify = 1, NimDelete = 2;
    private const uint NifMessage = 0x1, NifIcon = 0x2, NifTip = 0x4;
    private const int HotkeyId = 0x5843, TrayId = 1, MenuShow = 1, MenuExit = 2;
    private static readonly uint TaskbarCreated = RegisterWindowMessage("TaskbarCreated");

    private readonly App app;
    private readonly SubclassProc subclass;
    private IntPtr hwnd, icon;
    private bool attached, trayVisible, hotkeyRegistered;
    private Microsoft.UI.Dispatching.DispatcherQueueTimer? tipTimer;
    private string tip = "XIVChat Next";

    /// <summary>The configured shortcut could not be registered, usually because another application owns it.</summary>
    public bool HotkeyFailed { get; private set; }
    public event Action? Changed;
    public bool TrayVisible => trayVisible;

    public ShellIntegration(App app) {
        this.app = app;
        subclass = WindowProc;
    }

    internal void Attach(MainWindow window) {
        if (attached) return;
        hwnd = WinRT.Interop.WindowNative.GetWindowHandle(window);
        if (!SetWindowSubclass(hwnd, subclass, UIntPtr.Zero, UIntPtr.Zero)) throw new Win32Exception(Marshal.GetLastWin32Error());
        attached = true;
        // Unread counts change from several places (new tells, reading, switching characters); a light poll keeps the tooltip honest.
        tipTimer = window.DispatcherQueue.CreateTimer();
        tipTimer.Interval = TimeSpan.FromSeconds(2);
        tipTimer.Tick += (_, _) => RefreshTip();
        Apply();
    }

    /// <summary>Adds or removes the tray icon and re-registers the shortcut from the current configuration.</summary>
    public void Apply() {
        if (!attached) return;
        if (app.Config.CloseToTray) ShowTrayIcon(); else HideTrayIcon();
        UnregisterShortcut();
        HotkeyFailed = false;
        if (app.Config.GlobalHotkeyEnabled && IsValidShortcut(app.Config.GlobalHotkeyModifiers, app.Config.GlobalHotkeyKey)) {
            hotkeyRegistered = RegisterHotKey(hwnd, HotkeyId, app.Config.GlobalHotkeyModifiers | ModNoRepeat, app.Config.GlobalHotkeyKey);
            HotkeyFailed = !hotkeyRegistered;
        }
        Changed?.Invoke();
    }

    /// <summary>At least Ctrl, Alt or Win, so the shortcut cannot swallow ordinary typing in the game.</summary>
    public static bool IsValidShortcut(uint modifiers, uint key) =>
        (modifiers & (ModControl | ModAlt | ModWin)) != 0 && (modifiers & ~(ModAlt | ModControl | ModShift | ModWin)) == 0
        && key is >= 0x08 and <= 0xFE && key is not (0x10 or 0x11 or 0x12 or 0x5B or 0x5C or >= 0xA0 and <= 0xA5);

    public static string FormatShortcut(uint modifiers, uint key) {
        var parts = new System.Collections.Generic.List<string>();
        if ((modifiers & ModControl) != 0) parts.Add("Ctrl");
        if ((modifiers & ModAlt) != 0) parts.Add("Alt");
        if ((modifiers & ModShift) != 0) parts.Add("Shift");
        if ((modifiers & ModWin) != 0) parts.Add("Win");
        parts.Add(key switch {
            >= 0x30 and <= 0x39 or >= 0x41 and <= 0x5A => ((char)key).ToString(),
            >= 0x70 and <= 0x87 => "F" + (key - 0x6F),
            0x20 => "Space",
            0xC0 => "`",
            _ => Enum.IsDefined(typeof(Windows.System.VirtualKey), (int)key) ? ((Windows.System.VirtualKey)key).ToString() : "0x" + key.ToString("X2"),
        });
        return string.Join("+", parts);
    }

    /// <summary>Shortcut action: bring the main window forward with the composer focused, or put it away when it is already in front.</summary>
    internal void ToggleMain(bool fromShortcut) {
        var window = app.Window;
        if (window == null || app.Workspace.Stopping) return;
        var visible = app.Workspace.MainVisible && window.AppWindow.IsVisible
            && window.AppWindow.Presenter is not OverlappedPresenter { State: OverlappedPresenterState.Minimized };
        if (visible && (!fromShortcut || GetForegroundWindow() == hwnd)) {
            if (app.Config.CloseToTray) _ = app.Workspace.CloseMainAsync();
            else if (window.AppWindow.Presenter is OverlappedPresenter presenter) presenter.Minimize();
            return;
        }
        app.Workspace.ShowMain();
        SetForegroundWindow(hwnd);
        if (fromShortcut) window.FocusComposer();
    }

    private void RefreshTip() {
        if (!trayVisible) return;
        var unread = app.Workbench.Conversations.Sum(c => c.Unread);
        var next = unread > 0 ? "XIVChat Next — " + string.Format(LocalizationHelper.GetString("Tray.Unread"), unread) : "XIVChat Next";
        if (next == tip) return;
        tip = next;
        var data = IconData(NifTip);
        Shell_NotifyIcon(NimModify, ref data);
    }

    private void ShowTrayIcon() {
        if (trayVisible) return;
        if (icon == IntPtr.Zero) {
            var path = Path.Combine(AppContext.BaseDirectory, "Resources", "logo-c1.ico");
            icon = LoadImage(IntPtr.Zero, path, 1, GetSystemMetrics(49), GetSystemMetrics(50), 0x10);
        }
        var data = IconData(NifMessage | NifIcon | NifTip);
        trayVisible = Shell_NotifyIcon(NimAdd, ref data);
        tip = ""; RefreshTip();
        if (trayVisible) tipTimer?.Start();
    }

    private void HideTrayIcon() {
        if (!trayVisible) return;
        var data = IconData(0);
        Shell_NotifyIcon(NimDelete, ref data);
        trayVisible = false;
        tipTimer?.Stop();
    }

    private void UnregisterShortcut() {
        if (hotkeyRegistered) UnregisterHotKey(hwnd, HotkeyId);
        hotkeyRegistered = false;
    }

    private NotifyIconData IconData(uint flags) => new() {
        cbSize = Marshal.SizeOf<NotifyIconData>(), hWnd = hwnd, uID = TrayId, uFlags = flags,
        uCallbackMessage = TrayCallback, hIcon = icon, szTip = tip.Length > 127 ? tip[..127] : tip,
    };

    private void ShowMenu() {
        var menu = CreatePopupMenu();
        try {
            AppendMenu(menu, 0, MenuShow, LocalizationHelper.GetString("Tray.Show"));
            AppendMenu(menu, 0, MenuExit, LocalizationHelper.GetString("Tray.Exit"));
            GetCursorPos(out var point);
            // Required so the menu closes when the user clicks elsewhere.
            SetForegroundWindow(hwnd);
            var command = TrackPopupMenuEx(menu, 0x0100 | 0x0002, point.X, point.Y, hwnd, IntPtr.Zero); // TPM_RETURNCMD | TPM_RIGHTBUTTON
            PostMessage(hwnd, WmNull, IntPtr.Zero, IntPtr.Zero);
            if (command == MenuShow) ToggleMain(false);
            else if (command == MenuExit) _ = app.Workspace.ShutdownAsync();
        } finally { DestroyMenu(menu); }
    }

    private IntPtr WindowProc(IntPtr window, uint message, IntPtr wParam, IntPtr lParam, UIntPtr id, UIntPtr data) {
        if (message == WmHotkey && wParam == HotkeyId) { ToggleMain(true); return IntPtr.Zero; }
        if (message == TrayCallback) {
            var mouse = (uint)(lParam.ToInt64() & 0xFFFF);
            if (mouse == WmLButtonUp) ToggleMain(false);
            else if (mouse == WmRButtonUp) ShowMenu();
            return IntPtr.Zero;
        }
        if (message == TaskbarCreated && trayVisible) {
            // Explorer restarted and lost every icon.
            trayVisible = false; ShowTrayIcon();
        }
        if (message == WmDestroy) Dispose();
        return DefSubclassProc(window, message, wParam, lParam);
    }

    public void Dispose() {
        if (!attached) return;
        HideTrayIcon();
        UnregisterShortcut();
        tipTimer?.Stop();
        RemoveWindowSubclass(hwnd, subclass, UIntPtr.Zero);
        if (icon != IntPtr.Zero) { DestroyIcon(icon); icon = IntPtr.Zero; }
        attached = false;
    }

    private delegate IntPtr SubclassProc(IntPtr hwnd, uint message, IntPtr wParam, IntPtr lParam, UIntPtr id, UIntPtr data);
    [StructLayout(LayoutKind.Sequential)] private struct Point { public int X, Y; }
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct NotifyIconData {
        public int cbSize; public IntPtr hWnd; public uint uID, uFlags, uCallbackMessage; public IntPtr hIcon;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string szTip;
        public uint dwState, dwStateMask;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)] public string szInfo;
        public uint uVersionOrTimeout;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 64)] public string szInfoTitle;
        public uint dwInfoFlags; public Guid guidItem; public IntPtr hBalloonIcon;
    }
    [DllImport("comctl32.dll", SetLastError = true)] private static extern bool SetWindowSubclass(IntPtr hwnd, SubclassProc proc, UIntPtr id, UIntPtr data);
    [DllImport("comctl32.dll")] private static extern bool RemoveWindowSubclass(IntPtr hwnd, SubclassProc proc, UIntPtr id);
    [DllImport("comctl32.dll")] private static extern IntPtr DefSubclassProc(IntPtr hwnd, uint message, IntPtr wParam, IntPtr lParam);
    [DllImport("shell32.dll", CharSet = CharSet.Unicode, EntryPoint = "Shell_NotifyIconW")] private static extern bool Shell_NotifyIcon(uint message, ref NotifyIconData data);
    [DllImport("user32.dll", SetLastError = true)] private static extern bool RegisterHotKey(IntPtr hwnd, int id, uint modifiers, uint key);
    [DllImport("user32.dll")] private static extern bool UnregisterHotKey(IntPtr hwnd, int id);
    [DllImport("user32.dll", CharSet = CharSet.Unicode, EntryPoint = "RegisterWindowMessageW")] private static extern uint RegisterWindowMessage(string name);
    [DllImport("user32.dll", CharSet = CharSet.Unicode, EntryPoint = "LoadImageW")] private static extern IntPtr LoadImage(IntPtr instance, string name, uint type, int cx, int cy, uint load);
    [DllImport("user32.dll")] private static extern bool DestroyIcon(IntPtr icon);
    [DllImport("user32.dll")] private static extern int GetSystemMetrics(int index);
    [DllImport("user32.dll")] private static extern IntPtr CreatePopupMenu();
    [DllImport("user32.dll", CharSet = CharSet.Unicode, EntryPoint = "AppendMenuW")] private static extern bool AppendMenu(IntPtr menu, uint flags, int id, string text);
    [DllImport("user32.dll")] private static extern int TrackPopupMenuEx(IntPtr menu, uint flags, int x, int y, IntPtr hwnd, IntPtr parameters);
    [DllImport("user32.dll")] private static extern bool DestroyMenu(IntPtr menu);
    [DllImport("user32.dll")] private static extern bool GetCursorPos(out Point point);
    [DllImport("user32.dll")] private static extern bool SetForegroundWindow(IntPtr hwnd);
    [DllImport("user32.dll")] private static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] private static extern bool PostMessage(IntPtr hwnd, uint message, IntPtr wParam, IntPtr lParam);
}
