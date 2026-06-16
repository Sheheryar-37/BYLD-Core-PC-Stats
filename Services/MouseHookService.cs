using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace PcStatsMonitor.Services;

/// <summary>
/// Confines the mouse cursor to the primary (or allowed) display(s) using the
/// Win32 <c>ClipCursor</c> API. This prevents the user from accidentally
/// dragging windows onto the 7-inch case display.
///
/// Unlike a low-level mouse hook, <c>ClipCursor</c> is enforced at the OS
/// level and works reliably regardless of DPI scaling or multi-monitor layout.
/// </summary>
public class MouseHookService : IDisposable
{
    [DllImport("user32.dll")]
    private static extern bool ClipCursor(ref RECT lpRect);

    [DllImport("user32.dll")]
    private static extern bool ClipCursor(IntPtr lpRect);

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

    /// <summary>
    /// Gets or sets whether cursor confinement is active.
    /// When set to <c>true</c>, the cursor is clipped to the allowed region.
    /// When set to <c>false</c>, the cursor is released.
    /// </summary>
    public bool IsEnabled
    {
        get => _isEnabled;
        set
        {
            _isEnabled = value;
            if (_isEnabled)
                ApplyClip();
            else
                ReleaseClip();
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
    /// Releases the cursor confinement, allowing free movement across all screens.
    /// </summary>
    private void ReleaseClip()
    {
        ClipCursor(IntPtr.Zero);
    }

    public void Dispose()
    {
        ReleaseClip();
    }
}
