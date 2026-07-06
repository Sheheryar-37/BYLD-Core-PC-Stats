using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using System.Windows.Threading;

namespace PcStatsMonitor.Services;

/// <summary>
/// Confines the mouse cursor to the primary (or allowed) display(s) using the
/// Win32 <c>ClipCursor</c> API. This prevents the user from accidentally
/// dragging windows onto the 7-inch case display.
///
/// A <see cref="DispatcherTimer"/> periodically re-applies the clip every 500ms
/// to handle OS events that temporarily release it (window dragging, UAC prompts,
/// Alt-Tab, fullscreen transitions). Additionally, a WinEvent hook listens for
/// <c>EVENT_SYSTEM_MOVESIZEEND</c> to re-clip immediately after a window drag ends.
/// </summary>
public class MouseHookService : IDisposable
{
    [DllImport("user32.dll")]
    private static extern bool ClipCursor(ref RECT lpRect);

    [DllImport("user32.dll")]
    private static extern bool ClipCursor(IntPtr lpRect);

    [DllImport("user32.dll")]
    private static extern IntPtr SetWinEventHook(
        uint eventMin, uint eventMax, IntPtr hmodWinEventProc,
        WinEventDelegate lpfnWinEventProc, uint idProcess, uint idThread, uint dwFlags);

    [DllImport("user32.dll")]
    private static extern bool UnhookWinEvent(IntPtr hWinEventHook);

    private delegate void WinEventDelegate(
        IntPtr hWinEventHook, uint eventType, IntPtr hwnd, int idObject,
        int idChild, uint dwEventThread, uint dwmsEventTime);

    /// <summary>Fired when a window finishes being moved or sized.</summary>
    private const uint EVENT_SYSTEM_MOVESIZEEND = 0x000B;
    /// <summary>Out-of-context (no DLL needed for the hook).</summary>
    private const uint WINEVENT_OUTOFCONTEXT = 0x0000;

    [StructLayout(LayoutKind.Sequential)]
    private struct RECT
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    private bool _isEnabled;
    private Rectangle _blockedScreenBounds;
    private readonly DispatcherTimer _reClipTimer;
    private IntPtr _winEventHook = IntPtr.Zero;
    private WinEventDelegate? _winEventDelegate;

    public MouseHookService()
    {
        _reClipTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(500)
        };
        _reClipTimer.Tick += (s, e) => ApplyClip();
    }

    /// <summary>
    /// Gets or sets whether cursor confinement is active.
    /// When set to <c>true</c>, the cursor is clipped to the allowed region
    /// and a periodic re-clip timer + WinEvent hook are started.
    /// When set to <c>false</c>, the cursor is released.
    /// </summary>
    public bool IsEnabled
    {
        get => _isEnabled;
        set
        {
            _isEnabled = value;
            if (_isEnabled)
            {
                ApplyClip();
                _reClipTimer.Start();
                InstallWinEventHook();
            }
            else
            {
                _reClipTimer.Stop();
                UninstallWinEventHook();
                ReleaseClip();
            }
        }
    }

    /// <summary>
    /// The bounds of the screen that should be BLOCKED (the 7-inch case display).
    /// The cursor will be confined to the union of all OTHER screens.
    /// </summary>
    public Rectangle BlockedScreenBounds
    {
        get => _blockedScreenBounds;
        set
        {
            _blockedScreenBounds = value;
            if (_isEnabled)
                ApplyClip();
        }
    }

    /// <summary>
    /// Computes the allowed region (union of all screens EXCEPT the blocked one)
    /// and calls <c>ClipCursor</c> to confine the mouse.
    /// </summary>
    private void ApplyClip()
    {
        if (_blockedScreenBounds.IsEmpty) return;

        // Build the bounding rectangle of all non-blocked screens
        var allowedScreens = Screen.AllScreens
            .Where(s => !s.Bounds.Equals(_blockedScreenBounds))
            .ToList();

        if (allowedScreens.Count == 0) return;

        // Compute the union of all allowed screen bounds
        var union = allowedScreens[0].Bounds;
        for (int i = 1; i < allowedScreens.Count; i++)
        {
            union = Rectangle.Union(union, allowedScreens[i].Bounds);
        }

        var rect = new RECT
        {
            Left = union.Left,
            Top = union.Top,
            Right = union.Right,
            Bottom = union.Bottom
        };

        ClipCursor(ref rect);
    }

    /// <summary>
    /// Installs a WinEvent hook to re-apply the clip immediately after
    /// any window finishes being dragged/resized. This catches the case
    /// where Windows temporarily releases ClipCursor during a window drag.
    /// </summary>
    private void InstallWinEventHook()
    {
        if (_winEventHook != IntPtr.Zero) return;

        // Must keep a strong reference to the delegate to prevent GC collection
        _winEventDelegate = OnWinEvent;
        _winEventHook = SetWinEventHook(
            EVENT_SYSTEM_MOVESIZEEND, EVENT_SYSTEM_MOVESIZEEND,
            IntPtr.Zero, _winEventDelegate,
            0, 0, WINEVENT_OUTOFCONTEXT);
    }

    /// <summary>
    /// Removes the WinEvent hook.
    /// </summary>
    private void UninstallWinEventHook()
    {
        if (_winEventHook != IntPtr.Zero)
        {
            UnhookWinEvent(_winEventHook);
            _winEventHook = IntPtr.Zero;
        }
        _winEventDelegate = null;
    }

    /// <summary>
    /// WinEvent callback — re-applies the clip when a window drag/resize ends.
    /// </summary>
    private void OnWinEvent(IntPtr hWinEventHook, uint eventType, IntPtr hwnd,
        int idObject, int idChild, uint dwEventThread, uint dwmsEventTime)
    {
        if (_isEnabled)
        {
            ApplyClip();
        }
    }

    /// <summary>
    /// Releases the cursor confinement, allowing free movement across all screens.
    /// </summary>
    private void ReleaseClip()
    {
        ClipCursor(IntPtr.Zero);
    }

    public void Dispose()
    {
        _reClipTimer.Stop();
        UninstallWinEventHook();
        ReleaseClip();
    }
}
