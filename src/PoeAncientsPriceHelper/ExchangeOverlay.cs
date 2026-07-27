using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Drawing.Text;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace PoeAncientsPriceHelper;

// One picker badge: the formatted ratio, the optional volume text, and the cell's OCR'd name-text
// bounds (absolute screen px) the pill anchors to.
internal sealed record ExchangeBadge(string Ratio, string? Volume, Rectangle CellBounds);

// How trustworthy the poe.ninja snapshot behind the badges currently is (CLAUDE.md rule 4: a ratio
// from a 30-minute-old snapshot is not a live quote — surface it).
internal enum StalenessLevel { Fresh, Stale, Failing }

// Click-through, per-pixel-alpha layered overlay for the Currency Exchange helper. Shares the
// layered-window mechanics with RumourOverlayForm, with one critical addition: the badges sit INSIDE
// the panel region we keep re-OCRing, so the window is excluded from screen capture
// (WDA_EXCLUDEFROMCAPTURE, Win10 2004+ — the app's floor). Our own WGC/GDI captures never see the
// pills; the user (but not streaming software) still does.
internal sealed class ExchangeOverlayForm : Form
{
    private IReadOnlyList<ExchangeBadge> _badges = [];
    private Rectangle _panelBounds;
    private string? _mainText;
    private Rectangle _mainAnchor;
    private string _ageText = "";
    private StalenessLevel _level = StalenessLevel.Fresh;
    private bool _pickerMode;
    private string _lastSig = "";
    private const int BoundsJitterPx = 10;   // detected-bounds moves under this are "same" (no re-anchor)
    private readonly Rectangle _screenBounds;
    private Bitmap? _buffer;

    // Font per rounded pixel-height bucket, so pills scale with the cell text across resolutions.
    private readonly Dictionary<int, Font> _fonts = new();
    private readonly Font _chipFont = new("Segoe UI", 11, FontStyle.Bold);

    private const int PillPadX = 7;
    private const int PillPadY = 3;
    private const int PillGap = 10;          // gap between the cell text and its pill

    private static readonly Color PillBack = Color.FromArgb(205, 28, 30, 36);
    private static readonly Color PillBorder = Color.FromArgb(120, 90, 95, 110);
    private static readonly Color RatioColor = Color.FromArgb(255, 211, 124);   // gold, like value text
    private static readonly Color VolumeColor = Color.FromArgb(200, 200, 205);

    public ExchangeOverlayForm(Rectangle screenBounds)
    {
        _screenBounds = screenBounds;
        FormBorderStyle = FormBorderStyle.None;
        TopMost = true;
        ShowInTaskbar = false;
        StartPosition = FormStartPosition.Manual;
        AutoScaleMode = AutoScaleMode.None;
        Bounds = screenBounds;
    }

    protected override CreateParams CreateParams
    {
        get
        {
            const int WS_EX_LAYERED = 0x00080000;
            const int WS_EX_TRANSPARENT = 0x00000020;
            const int WS_EX_NOACTIVATE = 0x08000000;
            var cp = base.CreateParams;
            cp.ExStyle |= WS_EX_LAYERED | WS_EX_TRANSPARENT | WS_EX_NOACTIVATE;
            return cp;
        }
    }

    // Exclude this window from every screen-capture path (BitBlt, WGC monitor capture) so the scan
    // loop's own captures never contain our pills — a captured pill would append stray tokens to the
    // cell text on the next OCR pass. Best-effort: on failure we log and draw anyway (a captured
    // badge sits in dead cell space and only costs that pass's read; state never locks on it).
    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        if (!SetWindowDisplayAffinity(Handle, WDA_EXCLUDEFROMCAPTURE))
            Console.Error.WriteLine("[ExchangeOverlay] SetWindowDisplayAffinity failed — badges will appear in captures");
    }

    public void ShowPicker(IReadOnlyList<ExchangeBadge> badges, Rectangle panelBounds, string ageText, StalenessLevel level)
    {
        if (IsDisposed) return;
        if (InvokeRequired) { BeginInvoke(() => ShowPicker(badges, panelBounds, ageText, level)); return; }
        if (!Visible || RectShift(_panelBounds, panelBounds) > BoundsJitterPx)
            _panelBounds = panelBounds;
        _badges = badges;
        _pickerMode = true;
        _mainText = null;
        _ageText = ageText;
        _level = level;
        Render();
    }

    public void ShowMain(string text, Rectangle anchor, StalenessLevel level)
    {
        if (IsDisposed) return;
        if (InvokeRequired) { BeginInvoke(() => ShowMain(text, anchor, level)); return; }
        if (!Visible || RectShift(_mainAnchor, anchor) > BoundsJitterPx)
            _mainAnchor = anchor;
        _mainText = text;
        _pickerMode = false;
        _badges = [];
        _level = level;
        Render();
    }

    private void Render()
    {
        bool wasVisible = Visible;
        if (!wasVisible) Show();
        ForceTopmost();
        var sig = Signature();
        if (wasVisible && sig == _lastSig) return;   // nothing the user sees changed — skip repaint
        _lastSig = sig;
        RenderLayered();
    }

    private string Signature()
    {
        var sb = new System.Text.StringBuilder();
        sb.Append(_pickerMode).Append(';').Append(_panelBounds).Append(';').Append(_mainAnchor).Append(';')
          .Append(_mainText).Append(';').Append(_ageText).Append(';').Append(_level).Append(';');
        foreach (var b in _badges)
            sb.Append(b.Ratio).Append('|').Append(b.Volume).Append('|').Append(b.CellBounds).Append('\n');
        return sb.ToString();
    }

    private static int RectShift(Rectangle a, Rectangle b) =>
        Math.Max(Math.Max(Math.Abs(a.Left - b.Left), Math.Abs(a.Top - b.Top)),
                 Math.Max(Math.Abs(a.Right - b.Right), Math.Abs(a.Bottom - b.Bottom)));

    public void HideNow()
    {
        if (IsDisposed) return;
        if (InvokeRequired) { BeginInvoke(HideNow); return; }
        _badges = [];
        _mainText = null;
        _lastSig = "";
        if (Visible) Hide();
    }

    protected override void OnPaintBackground(PaintEventArgs e) { }
    protected override void OnPaint(PaintEventArgs e) { }
    protected override void OnShown(EventArgs e) { base.OnShown(e); ForceTopmost(); RenderLayered(); }

    private void RenderLayered()
    {
        if (!IsHandleCreated || IsDisposed || !Visible) return;
        int w = Bounds.Width, h = Bounds.Height;
        if (w <= 0 || h <= 0) return;

        if (_buffer is null || _buffer.Width != w || _buffer.Height != h)
        {
            _buffer?.Dispose();
            _buffer = new Bitmap(w, h, PixelFormat.Format32bppArgb);
        }
        using (var g = Graphics.FromImage(_buffer))
        {
            g.Clear(Color.FromArgb(0));
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
            PaintScene(g);
        }

        IntPtr screenDc = GetDC(IntPtr.Zero);
        IntPtr memDc = CreateCompatibleDC(screenDc);
        IntPtr hBitmap = IntPtr.Zero, oldBitmap = IntPtr.Zero;
        try
        {
            hBitmap = _buffer.GetHbitmap(Color.FromArgb(0));
            oldBitmap = SelectObject(memDc, hBitmap);
            var size = new SIZE { cx = w, cy = h };
            var src = new POINT { x = 0, y = 0 };
            var dst = new POINT { x = Bounds.Left, y = Bounds.Top };
            var blend = new BLENDFUNCTION
            {
                BlendOp = AC_SRC_OVER, BlendFlags = 0, SourceConstantAlpha = 255, AlphaFormat = AC_SRC_ALPHA,
            };
            UpdateLayeredWindow(Handle, screenDc, ref dst, ref size, memDc, ref src, 0, ref blend, ULW_ALPHA);
        }
        finally
        {
            if (oldBitmap != IntPtr.Zero) SelectObject(memDc, oldBitmap);
            if (hBitmap != IntPtr.Zero) DeleteObject(hBitmap);
            if (memDc != IntPtr.Zero) DeleteDC(memDc);
            if (screenDc != IntPtr.Zero) ReleaseDC(IntPtr.Zero, screenDc);
        }
    }

    private void PaintScene(Graphics g)
    {
        g.TranslateTransform(-Bounds.Left, -Bounds.Top);
        if (_pickerMode)
        {
            // One font and one set of column boundaries for the whole panel — see ExchangeBadgeLayout
            // for why per-cell sizing/anchoring produced mismatched pills that covered the next column.
            var cells = new List<Rectangle>(_badges.Count);
            foreach (var b in _badges) cells.Add(b.CellBounds);
            var font = FontForPx(ExchangeBadgeLayout.UniformFontPx(cells));
            var edges = ExchangeBadgeLayout.ColumnRightEdges(cells, _panelBounds);
            foreach (var badge in _badges) PaintPill(g, badge, font, edges);
            PaintStalenessChip(g);
        }
        else if (_mainText is { } text)
        {
            // Single pill beneath the game's "Market Ratio" header.
            var font = FontFor(Math.Max(16, _mainAnchor.Height));
            var size = g.MeasureString(text, font);
            int x = _mainAnchor.Left + (_mainAnchor.Width - (int)size.Width) / 2 - PillPadX;
            int y = _mainAnchor.Bottom + 6;
            PaintTextPill(g, text, null, font, new Point(Math.Max(_screenBounds.Left, x), y));
        }
    }

    // Pills are right-aligned inside their own grid column, so every pill on the panel shares an x for
    // its column and none can reach the next column's label. Vertically centred on the cell, which
    // keeps wrapped two-line names centred across both lines without changing their pill size.
    private void PaintPill(Graphics g, ExchangeBadge badge, Font font, IReadOnlyList<int> columnRightEdges)
    {
        int columnRight = ExchangeBadgeLayout.ColumnRightFor(badge.CellBounds, columnRightEdges, _panelBounds);
        int limit = Math.Min(columnRight, _screenBounds.Right) - 4;

        string? volume = badge.Volume;
        var size = g.MeasureString(Compose(badge.Ratio, volume), font);
        // Doesn't fit in the column: drop the volume tail before letting it spill.
        if (columnRight - (size.Width + PillPadX * 2) < badge.CellBounds.Left && volume is not null)
        {
            volume = null;
            size = g.MeasureString(badge.Ratio, font);
        }

        int x = limit - (int)size.Width - PillPadX * 2;
        x = Math.Max(x, _screenBounds.Left);
        int y = badge.CellBounds.Top + (badge.CellBounds.Height - (int)size.Height) / 2 - PillPadY;
        PaintTextPill(g, badge.Ratio, volume, font, new Point(x, y));
    }

    private static string Compose(string ratio, string? volume) =>
        volume is { } v ? $"{ratio} · {v}" : ratio;

    // A rounded pill with the ratio in gold and the optional " · volume" tail in grey.
    private void PaintTextPill(Graphics g, string ratio, string? volume, Font font, Point at)
    {
        string full = volume is { } v ? $"{ratio} · {v}" : ratio;
        var fullSize = g.MeasureString(full, font);
        var rect = new Rectangle(at.X, at.Y, (int)fullSize.Width + PillPadX * 2, (int)fullSize.Height + PillPadY * 2);
        using (var path = RoundedRect(rect, 6))
        using (var bg = new SolidBrush(Premultiply(PillBack)))
        using (var border = new Pen(PillBorder, 1))
        {
            g.FillPath(bg, path);
            g.DrawPath(border, path);
        }
        using var ratioBrush = new SolidBrush(RatioColor);
        g.DrawString(ratio, font, ratioBrush, rect.X + PillPadX, rect.Y + PillPadY);
        if (volume is { } tail)
        {
            var ratioW = g.MeasureString(ratio, font).Width;
            using var volBrush = new SolidBrush(VolumeColor);
            g.DrawString($" · {tail}", font, volBrush, rect.X + PillPadX + ratioW - 4, rect.Y + PillPadY);
        }
    }

    // Small "ninja 12m" chip at the panel's top-right: the ratios are aggregates from a snapshot,
    // not live quotes — amber past 30 min, red while fetches fail (CLAUDE.md rules 4/5).
    private void PaintStalenessChip(Graphics g)
    {
        if (_ageText.Length == 0) return;
        var color = _level switch
        {
            StalenessLevel.Failing => Color.FromArgb(255, 85, 85),
            StalenessLevel.Stale => Color.FromArgb(224, 176, 96),
            _ => Color.FromArgb(160, 165, 175),
        };
        string text = $"ninja {_ageText}";
        var size = g.MeasureString(text, _chipFont);
        var rect = new Rectangle(_panelBounds.Right - (int)size.Width - PillPadX * 2,
            Math.Max(_screenBounds.Top, _panelBounds.Top - (int)size.Height - PillPadY * 2 - 4),
            (int)size.Width + PillPadX * 2, (int)size.Height + PillPadY * 2);
        using (var path = RoundedRect(rect, 6))
        using (var bg = new SolidBrush(Premultiply(PillBack)))
        {
            g.FillPath(bg, path);
        }
        using var brush = new SolidBrush(color);
        g.DrawString(text, _chipFont, brush, rect.X + PillPadX, rect.Y + PillPadY);
    }

    // Pill text tracks the cell height (cells shrink at lower resolutions / UI scales). Bucketed so
    // we cache a handful of Font objects, not one per unique pixel height. The picker resolves ONE
    // size for the whole panel (ExchangeBadgeLayout.UniformFontPx); the main-view pill still sizes off
    // its own anchor, which is correct there — it's a single pill, not a grid.
    private Font FontFor(int cellHeightPx) => FontForPx(ExchangeBadgeLayout.FontPxForHeight(cellHeightPx));

    private Font FontForPx(int px)
    {
        if (_fonts.TryGetValue(px, out var f)) return f;
        f = new Font("Segoe UI", px, FontStyle.Bold, GraphicsUnit.Pixel);
        _fonts[px] = f;
        return f;
    }

    private static Color Premultiply(Color c) =>
        Color.FromArgb(c.A, c.R * c.A / 255, c.G * c.A / 255, c.B * c.A / 255);

    private static GraphicsPath RoundedRect(Rectangle r, int radius)
    {
        int d = radius * 2;
        var path = new GraphicsPath();
        path.AddArc(r.X, r.Y, d, d, 180, 90);
        path.AddArc(r.Right - d, r.Y, d, d, 270, 90);
        path.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
        path.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
        path.CloseFigure();
        return path;
    }

    public void ForceTopmost()
    {
        if (IsDisposed || !IsHandleCreated || !Visible) return;
        if (InvokeRequired) { BeginInvoke(ForceTopmost); return; }
        SetWindowPos(Handle, new IntPtr(-1), 0, 0, 0, 0, 0x0002 | 0x0001 | 0x0010);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            foreach (var f in _fonts.Values) f.Dispose();
            _chipFont.Dispose();
            _buffer?.Dispose();
        }
        base.Dispose(disposing);
    }

    private const uint WDA_EXCLUDEFROMCAPTURE = 0x00000011;
    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetWindowDisplayAffinity(IntPtr hwnd, uint dwAffinity);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int X, int Y, int cx, int cy, uint uFlags);

    private const int ULW_ALPHA = 0x02;
    private const byte AC_SRC_OVER = 0x00;
    private const byte AC_SRC_ALPHA = 0x01;

    [StructLayout(LayoutKind.Sequential)] private struct POINT { public int x, y; }
    [StructLayout(LayoutKind.Sequential)] private struct SIZE { public int cx, cy; }
    [StructLayout(LayoutKind.Sequential)] private struct BLENDFUNCTION
    {
        public byte BlendOp, BlendFlags, SourceConstantAlpha, AlphaFormat;
    }

    [DllImport("user32.dll")] private static extern IntPtr GetDC(IntPtr hWnd);
    [DllImport("user32.dll")] private static extern int ReleaseDC(IntPtr hWnd, IntPtr hDC);
    [DllImport("gdi32.dll")] private static extern IntPtr CreateCompatibleDC(IntPtr hDC);
    [DllImport("gdi32.dll")] private static extern bool DeleteDC(IntPtr hDC);
    [DllImport("gdi32.dll")] private static extern IntPtr SelectObject(IntPtr hDC, IntPtr hObject);
    [DllImport("gdi32.dll")] private static extern bool DeleteObject(IntPtr hObject);
    [DllImport("user32.dll")] private static extern bool UpdateLayeredWindow(
        IntPtr hwnd, IntPtr hdcDst, ref POINT pptDst, ref SIZE psize, IntPtr hdcSrc,
        ref POINT pptSrc, int crKey, ref BLENDFUNCTION pblend, int dwFlags);
}

// Hosts the exchange overlay on its own STA thread, mirroring RumourOverlayManager (Per-Monitor-V2
// pinned before the handle exists so the layered bitmap stays physical-pixel).
internal static class ExchangeOverlayManager
{
    private static ExchangeOverlayForm? _form;
    private static Thread? _thread;
    private static readonly object _lock = new();

    public static void ShowPicker(IReadOnlyList<ExchangeBadge> badges, Rectangle panelBounds, string ageText, StalenessLevel level)
    {
        EnsureForm(panelBounds);
        WithForm(f => f.ShowPicker(badges, panelBounds, ageText, level));
    }

    public static void ShowMain(string text, Rectangle anchor, StalenessLevel level)
    {
        EnsureForm(anchor);
        WithForm(f => f.ShowMain(text, anchor, level));
    }

    public static void HideNow() => WithForm(f => f.HideNow());

    public static void Close()
    {
        WithForm(f => f.Invoke(() => { if (!f.IsDisposed) f.Close(); }));
    }

    private static void EnsureForm(Rectangle anchorBounds)
    {
        lock (_lock)
        {
            if (_form is not null && !_form.IsDisposed) return;

            using var ready = new ManualResetEventSlim(false);
            _thread = new Thread(() =>
            {
                SetThreadDpiAwarenessContext(DPI_AWARENESS_CONTEXT_PER_MONITOR_AWARE_V2);
                var screen = Screen.FromRectangle(anchorBounds).Bounds;
                var f = new ExchangeOverlayForm(screen);
                f.Shown += (_, _) => ready.Set();
                _form = f;
                System.Windows.Forms.Application.Run(f);
                lock (_lock) _form = null;
            }) { IsBackground = true, Name = "ExchangeOverlay-STA" };
            _thread.SetApartmentState(ApartmentState.STA);
            _thread.Start();
            ready.Wait(TimeSpan.FromSeconds(2));
        }
    }

    private static void WithForm(Action<ExchangeOverlayForm> action)
    {
        ExchangeOverlayForm? f;
        lock (_lock) { f = _form; }
        if (f is null || f.IsDisposed) return;
        try { action(f); }
        catch (ObjectDisposedException) { }
        catch (InvalidOperationException) { }
    }

    private static readonly IntPtr DPI_AWARENESS_CONTEXT_PER_MONITOR_AWARE_V2 = new(-4);

    [DllImport("user32.dll")]
    private static extern IntPtr SetThreadDpiAwarenessContext(IntPtr dpiContext);
}
