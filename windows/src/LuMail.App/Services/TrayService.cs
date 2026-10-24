using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.Runtime.InteropServices;
using CommunityToolkit.Mvvm.Input;
using H.NotifyIcon;
using LuMail.App.ViewModels;
using Microsoft.UI.Xaml.Controls;

namespace LuMail.App.Services;

/// <summary>
/// Symbol im Infobereich (Tooltip mit Zahl, Menü mit den 5 neuesten ungelesenen Mails und „Lu-Mail öffnen“)
/// und Zähler als Taskleisten-Overlay (FUNKTIONEN §2).
/// </summary>
public sealed class TrayService : IDisposable
{
    private readonly TaskbarIcon? _icon;
    private readonly Action _openApp;
    private readonly Action<MessageItem> _openMessage;
    private readonly TaskbarOverlay _overlay;
    private int _lastCount = -1;

    public TrayService(IntPtr hwnd, Action openApp, Action<MessageItem> openMessage)
    {
        _openApp = openApp;
        _openMessage = openMessage;
        _overlay = new TaskbarOverlay(hwnd);
        try
        {
            var iconPath = Path.Combine(AppContext.BaseDirectory, "Assets", "LuMail.ico");
            _icon = new TaskbarIcon
            {
                ToolTipText = "Lu-Mail",
                ContextMenuMode = ContextMenuMode.PopupMenu,
                LeftClickCommand = new RelayCommand(openApp),
                NoLeftClickDelay = true,
            };
            if (File.Exists(iconPath)) _icon.Icon = new Icon(iconPath);
            _icon.ContextFlyout = BuildMenu(Array.Empty<MessageItem>());
            _icon.ForceCreate(false);
        }
        catch (Exception)
        {
            _icon = null; // Infobereich nicht verfügbar – App läuft ohne weiter.
        }
    }

    public void Update(int unread, IReadOnlyList<MessageItem> newest)
    {
        if (_icon is not null)
        {
            try
            {
                _icon.ToolTipText = unread == 0 ? "Lu-Mail – keine ungelesenen Nachrichten"
                    : unread == 1 ? "Lu-Mail – 1 ungelesene Nachricht"
                    : $"Lu-Mail – {unread} ungelesene Nachrichten";
                _icon.ContextFlyout = BuildMenu(newest);
            }
            catch (Exception)
            {
            }
        }
        if (unread != _lastCount)
        {
            _lastCount = unread;
            _overlay.SetCount(unread);
        }
    }

    private MenuFlyout BuildMenu(IReadOnlyList<MessageItem> newest)
    {
        var menu = new MenuFlyout();
        foreach (var item in newest.Take(5))
        {
            var text = $"{item.Sender} – {item.Subject}";
            if (text.Length > 60) text = text[..57] + "…";
            var captured = item;
            menu.Items.Add(new MenuFlyoutItem { Text = text, Command = new RelayCommand(() => _openMessage(captured)) });
        }
        if (newest.Count > 0) menu.Items.Add(new MenuFlyoutSeparator());
        menu.Items.Add(new MenuFlyoutItem { Text = "Lu-Mail öffnen", Command = new RelayCommand(_openApp) });
        menu.Items.Add(new MenuFlyoutItem { Text = "Beenden", Command = new RelayCommand(() => Microsoft.UI.Xaml.Application.Current.Exit()) });
        return menu;
    }

    public void Dispose()
    {
        try { _icon?.Dispose(); } catch (Exception) { }
        _overlay.Dispose();
    }
}

/// <summary>Zahl als Overlay-Symbol auf dem Taskleisten-Eintrag (ITaskbarList3::SetOverlayIcon).</summary>
internal sealed class TaskbarOverlay : IDisposable
{
    private readonly IntPtr _hwnd;
    private ITaskbarList3? _taskbar;
    private IntPtr _currentIcon;

    public TaskbarOverlay(IntPtr hwnd)
    {
        _hwnd = hwnd;
        try
        {
            _taskbar = (ITaskbarList3)new TaskbarListComObject();
            _taskbar.HrInit();
        }
        catch (Exception)
        {
            _taskbar = null;
        }
    }

    public void SetCount(int count)
    {
        if (_taskbar is null) return;
        try
        {
            var icon = count > 0 ? CreateBadge(count) : IntPtr.Zero;
            _taskbar.SetOverlayIcon(_hwnd, icon, count > 0 ? $"{count} ungelesen" : "");
            if (_currentIcon != IntPtr.Zero) DestroyIcon(_currentIcon);
            _currentIcon = icon;
        }
        catch (Exception)
        {
        }
    }

    private static IntPtr CreateBadge(int count)
    {
        var text = count > 99 ? "99+" : count.ToString(System.Globalization.CultureInfo.InvariantCulture);
        using var bitmap = new Bitmap(32, 32);
        using (var g = Graphics.FromImage(bitmap))
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
            g.Clear(Color.Transparent);
            using var fill = new SolidBrush(Color.FromArgb(0x8B, 0x7C, 0xFF));
            g.FillEllipse(fill, 0, 0, 31, 31);
            using var font = new Font("Segoe UI", text.Length > 2 ? 10f : 14f, FontStyle.Bold, GraphicsUnit.Pixel);
            using var format = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center };
            g.DrawString(text, font, Brushes.White, new RectangleF(0, 0, 32, 32), format);
        }
        return bitmap.GetHicon();
    }

    public void Dispose()
    {
        if (_currentIcon != IntPtr.Zero) DestroyIcon(_currentIcon);
        _currentIcon = IntPtr.Zero;
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool DestroyIcon(IntPtr hIcon);

    [ComImport, Guid("56FDF344-FD6D-11d0-958A-006097C9A090"), ClassInterface(ClassInterfaceType.None)]
    private class TaskbarListComObject { }

    /// <summary>Vollständige VTable-Reihenfolge von ITaskbarList, ITaskbarList2 und ITaskbarList3.</summary>
    [ComImport, Guid("ea1afb91-9e28-4b86-90e9-9e9f8a5eefaf"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface ITaskbarList3
    {
        // ITaskbarList
        void HrInit();
        void AddTab(IntPtr hwnd);
        void DeleteTab(IntPtr hwnd);
        void ActivateTab(IntPtr hwnd);
        void SetActiveAlt(IntPtr hwnd);
        // ITaskbarList2
        void MarkFullscreenWindow(IntPtr hwnd, [MarshalAs(UnmanagedType.Bool)] bool fullscreen);
        // ITaskbarList3
        void SetProgressValue(IntPtr hwnd, ulong completed, ulong total);
        void SetProgressState(IntPtr hwnd, int flags);
        void RegisterTab(IntPtr hwndTab, IntPtr hwndMdi);
        void UnregisterTab(IntPtr hwndTab);
        void SetTabOrder(IntPtr hwndTab, IntPtr hwndInsertBefore);
        void SetTabActive(IntPtr hwndTab, IntPtr hwndMdi, uint reserved);
        void ThumbBarAddButtons(IntPtr hwnd, uint count, IntPtr buttons);
        void ThumbBarUpdateButtons(IntPtr hwnd, uint count, IntPtr buttons);
        void ThumbBarSetImageList(IntPtr hwnd, IntPtr himl);
        void SetOverlayIcon(IntPtr hwnd, IntPtr hIcon, [MarshalAs(UnmanagedType.LPWStr)] string description);
        void SetThumbnailTooltip(IntPtr hwnd, [MarshalAs(UnmanagedType.LPWStr)] string tip);
        void SetThumbnailClip(IntPtr hwnd, IntPtr clip);
    }
}
