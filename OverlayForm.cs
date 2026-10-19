using System;
using System.ComponentModel;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace PushToTalk
{
    // A small event-driven, per-pixel-alpha window. It owns no timer, audio device or hotkey.
    // WS_EX_LAYERED + WS_EX_TRANSPARENT passes mouse input through even to other processes.
    internal sealed class OverlayForm : Form
    {
        private const int MinimumOverlaySize = 48;
        private const int MaximumOverlaySize = 256;
        private const int GwlStyle = -16;
        private const int GwlExStyle = -20;
        private const int WsThickFrame = 0x00040000;
        private const int WsExTransparent = 0x00000020;
        private const int WsExToolWindow = 0x00000080;
        private const int WsExLayered = 0x00080000;
        private const int WsExNoActivate = 0x08000000;
        private const int WmNcCalcSize = 0x0083;
        private const int WmNcHitTest = 0x0084;
        private const int WmMouseActivate = 0x0021;
        private const int WmEnterSizeMove = 0x0231;
        private const int WmExitSizeMove = 0x0232;
        private const int WmSizing = 0x0214;
        private const int WmDisplayChange = 0x007E;
        private const int HtTransparent = -1;
        private const int HtCaption = 2;
        private const int HtLeft = 10;
        private const int HtRight = 11;
        private const int HtTop = 12;
        private const int HtTopLeft = 13;
        private const int HtTopRight = 14;
        private const int HtBottom = 15;
        private const int HtBottomLeft = 16;
        private const int HtBottomRight = 17;
        private const uint SwpNoSize = 0x0001;
        private const uint SwpNoMove = 0x0002;
        private const uint SwpNoActivate = 0x0010;
        private const uint SwpFrameChanged = 0x0020;

        private bool controlled;
        private bool muted = true;
        private bool editing;
        private int opacityPercent = 100;
        private bool rendering;
        private Rectangle dragStartBounds;
        private bool userMoving;

        public event EventHandler LayoutChanged;

        public bool Editing { get { return editing; } }
        public int OpacityPercent { get { return opacityPercent; } }

        public OverlayForm()
        {
            Text = Localization.Text("Push To Talk 屏幕覆盖", "Push To Talk Overlay");
            FormBorderStyle = FormBorderStyle.None;
            ShowInTaskbar = false;
            TopMost = true;
            StartPosition = FormStartPosition.Manual;
            AutoScaleMode = AutoScaleMode.None;
            MinimumSize = new Size(MinimumOverlaySize, MinimumOverlaySize);
            MaximumSize = new Size(MaximumOverlaySize, MaximumOverlaySize);
            Size = new Size(80, 80);
            SetStyle(ControlStyles.Selectable, false);
        }

        protected override bool ShowWithoutActivation { get { return true; } }

        protected override CreateParams CreateParams
        {
            get
            {
                CreateParams value = base.CreateParams;
                value.ExStyle |= WsExLayered | WsExToolWindow | WsExNoActivate;
                if (!editing) value.ExStyle |= WsExTransparent;
                if (editing) value.Style |= WsThickFrame;
                return value;
            }
        }

        public void SetStatus(bool isControlled, bool isMuted)
        {
            if (controlled == isControlled && muted == isMuted) return;
            controlled = isControlled;
            muted = isMuted;
            RenderLayer();
        }

        public void SetOpacityPercent(int value)
        {
            value = Math.Max(20, Math.Min(100, value));
            if (opacityPercent == value) return;
            opacityPercent = value;
            RenderLayer();
        }

        public void SetEditMode(bool value)
        {
            if (editing == value) return;
            editing = value;
            if (IsHandleCreated)
            {
                long exStyle = GetWindowLongValue(Handle, GwlExStyle).ToInt64();
                exStyle |= WsExLayered | WsExToolWindow | WsExNoActivate;
                if (editing) exStyle &= ~((long)WsExTransparent);
                else exStyle |= WsExTransparent;
                SetWindowLongValue(Handle, GwlExStyle, new IntPtr(exStyle));
                long style = GetWindowLongValue(Handle, GwlStyle).ToInt64();
                if (editing) style |= WsThickFrame;
                else style &= ~((long)WsThickFrame);
                SetWindowLongValue(Handle, GwlStyle, new IntPtr(style));
                // Keep the same bounds and focus while changing native hit-testing styles.
                SetWindowPos(Handle, new IntPtr(-1), 0, 0, 0, 0,
                    SwpNoSize | SwpNoMove | SwpNoActivate | SwpFrameChanged);
            }
            RenderLayer();
        }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            RenderLayer();
        }

        protected override void OnShown(EventArgs e)
        {
            base.OnShown(e);
            RenderLayer();
        }

        protected override void OnResize(EventArgs e)
        {
            base.OnResize(e);
            RenderLayer();
        }

        protected override void OnPaintBackground(PaintEventArgs e) { }

        protected override void OnPaint(PaintEventArgs e) { }

        protected override void WndProc(ref Message message)
        {
            if (message.Msg == WmNcCalcSize)
            {
                // WS_THICKFRAME enables native sizing without adding a visible Windows border.
                message.Result = IntPtr.Zero;
                return;
            }
            if (message.Msg == WmMouseActivate)
            {
                message.Result = new IntPtr(3); // MA_NOACTIVATE; still receive the editing click.
                return;
            }
            if (message.Msg == WmNcHitTest)
            {
                if (!editing) message.Result = new IntPtr(HtTransparent);
                else
                {
                    long position = message.LParam.ToInt64();
                    // Coordinates are signed so monitors left/above the primary work correctly.
                    Point local = PointToClient(new Point((short)(position & 0xFFFF),
                        (short)((position >> 16) & 0xFFFF)));
                    message.Result = new IntPtr(EditHitTest(local));
                }
                return;
            }
            if (message.Msg == WmEnterSizeMove)
            {
                dragStartBounds = Bounds;
                userMoving = editing;
            }
            if (message.Msg == WmSizing && editing)
            {
                NativeRect proposed = (NativeRect)Marshal.PtrToStructure(message.LParam, typeof(NativeRect));
                KeepSquare(ref proposed, message.WParam.ToInt32(),
                    userMoving && dragStartBounds.Width > 0 ? dragStartBounds.Width : Width);
                Marshal.StructureToPtr(proposed, message.LParam, false);
                message.Result = new IntPtr(1);
                return;
            }
            if (message.Msg == WmExitSizeMove)
            {
                base.WndProc(ref message);
                bool changed = userMoving && dragStartBounds != Bounds;
                userMoving = false;
                if (changed)
                {
                    EventHandler handler = LayoutChanged;
                    if (handler != null) handler(this, EventArgs.Empty);
                }
                return;
            }
            base.WndProc(ref message);
            if (message.Msg == WmDisplayChange) RenderLayer();
        }

        private int EditHitTest(Point location)
        {
            int edge = Math.Max(7, Math.Min(12, Width / 8));
            bool left = location.X < edge;
            bool right = location.X >= ClientSize.Width - edge;
            bool top = location.Y < edge;
            bool bottom = location.Y >= ClientSize.Height - edge;
            if (top && left) return HtTopLeft;
            if (top && right) return HtTopRight;
            if (bottom && left) return HtBottomLeft;
            if (bottom && right) return HtBottomRight;
            if (left) return HtLeft;
            if (right) return HtRight;
            if (top) return HtTop;
            if (bottom) return HtBottom;
            return HtCaption;
        }

        private static void KeepSquare(ref NativeRect proposed, int edge, int previousSize)
        {
            int width = proposed.Right - proposed.Left;
            int height = proposed.Bottom - proposed.Top;
            // WMSZ_LEFT/RIGHT use the width; TOP/BOTTOM use the height.
            int size = edge == 3 || edge == 6 ? height : width;
            if (edge == 4 || edge == 5 || edge == 7 || edge == 8)
            {
                // For corners, follow the pointer's dominant direction, including while shrinking.
                size = Math.Abs(width - previousSize) >= Math.Abs(height - previousSize) ? width : height;
            }
            size = Math.Max(MinimumOverlaySize, Math.Min(MaximumOverlaySize, size));
            if (edge == 1 || edge == 4 || edge == 7) proposed.Left = proposed.Right - size;
            else proposed.Right = proposed.Left + size;
            if (edge == 3 || edge == 4 || edge == 5) proposed.Top = proposed.Bottom - size;
            else proposed.Bottom = proposed.Top + size;
        }

        private void RenderLayer()
        {
            if (rendering || !IsHandleCreated || IsDisposed || Width <= 0 || Height <= 0) return;
            rendering = true;
            IntPtr screenDc = IntPtr.Zero;
            IntPtr memoryDc = IntPtr.Zero;
            IntPtr nativeBitmap = IntPtr.Zero;
            IntPtr previousBitmap = IntPtr.Zero;
            try
            {
                using (Bitmap bitmap = new Bitmap(Width, Height, PixelFormat.Format32bppPArgb))
                {
                    using (Graphics graphics = Graphics.FromImage(bitmap)) DrawOverlay(graphics, bitmap.Size);
                    screenDc = GetDC(IntPtr.Zero);
                    memoryDc = CreateCompatibleDC(screenDc);
                    nativeBitmap = bitmap.GetHbitmap(Color.FromArgb(0));
                    previousBitmap = SelectObject(memoryDc, nativeBitmap);
                    NativePoint destination = new NativePoint(Left, Top);
                    NativePoint source = new NativePoint(0, 0);
                    NativeSize size = new NativeSize(Width, Height);
                    BlendFunction blend = new BlendFunction();
                    // Keep per-pixel antialiasing while fading the entire badge. Form.Opacity
                    // uses SetLayeredWindowAttributes, which conflicts with UpdateLayeredWindow.
                    blend.SourceConstantAlpha = OpacityToAlpha(opacityPercent);
                    blend.AlphaFormat = 1; // AC_SRC_ALPHA; source uses premultiplied alpha.
                    if (!UpdateLayeredWindow(Handle, screenDc, ref destination, ref size,
                        memoryDc, ref source, 0, ref blend, 2))
                        throw new Win32Exception(Marshal.GetLastWin32Error(), Localization.Text("无法绘制屏幕覆盖。", "Cannot draw the screen overlay."));
                }
            }
            finally
            {
                if (previousBitmap != IntPtr.Zero && memoryDc != IntPtr.Zero) SelectObject(memoryDc, previousBitmap);
                if (nativeBitmap != IntPtr.Zero) DeleteObject(nativeBitmap);
                if (memoryDc != IntPtr.Zero) DeleteDC(memoryDc);
                if (screenDc != IntPtr.Zero) ReleaseDC(IntPtr.Zero, screenDc);
                rendering = false;
            }
        }

        private void DrawOverlay(Graphics graphics, Size size)
        {
            graphics.Clear(Color.Transparent);
            graphics.SmoothingMode = SmoothingMode.AntiAlias;
            graphics.PixelOffsetMode = PixelOffsetMode.HighQuality;
            graphics.ScaleTransform(size.Width / 80f, size.Height / 80f);
            Color state = !controlled ? Color.FromArgb(163, 176, 194)
                : muted ? Color.FromArgb(255, 108, 122) : Color.FromArgb(104, 235, 171);
            Color face = Color.FromArgb(235, 22, 28, 38);

            if (editing)
            {
                using (SolidBrush brush = new SolidBrush(Color.FromArgb(215, 19, 28, 42)))
                    graphics.FillRectangle(brush, 0, 0, 80, 80);
            }
            using (SolidBrush shadow = new SolidBrush(Color.FromArgb(50, 0, 0, 0)))
                graphics.FillEllipse(shadow, 3, 5, 74, 74);
            using (SolidBrush brush = new SolidBrush(face)) graphics.FillEllipse(brush, 5, 5, 70, 70);
            using (Pen rim = new Pen(Color.FromArgb(110, state), 1.3f)) graphics.DrawEllipse(rim, 5, 5, 70, 70);

            using (GraphicsPath body = RoundedRectangle(new RectangleF(33, 19, 14, 29), 7))
            using (SolidBrush brush = new SolidBrush(state)) graphics.FillPath(brush, body);
            using (Pen microphone = new Pen(state, 3.2f))
            using (GraphicsPath cradle = new GraphicsPath())
            {
                microphone.StartCap = LineCap.Round;
                microphone.EndCap = LineCap.Round;
                cradle.AddLine(27, 35, 27, 41);
                cradle.AddArc(27, 28, 26, 26, 180, -180);
                cradle.AddLine(53, 41, 53, 35);
                graphics.DrawPath(microphone, cradle);
                graphics.DrawLine(microphone, 40, 54, 40, 61);
                graphics.DrawLine(microphone, 32, 61, 48, 61);
            }
            if (controlled && muted)
            {
                using (Pen gap = new Pen(face, 8))
                using (Pen slash = new Pen(state, 3.7f))
                {
                    gap.StartCap = gap.EndCap = LineCap.Round;
                    slash.StartCap = slash.EndCap = LineCap.Round;
                    graphics.DrawLine(gap, 24, 22, 57, 57);
                    graphics.DrawLine(slash, 24, 22, 57, 57);
                }
            }
            else if (controlled)
            {
                using (Pen wave = new Pen(state, 2.2f))
                {
                    wave.StartCap = wave.EndCap = LineCap.Round;
                    graphics.DrawArc(wave, 50, 25, 11, 22, -45, 90);
                }
            }
            if (editing)
            {
                Color blue = Color.FromArgb(255, 126, 185, 255);
                float stroke = 1.5f * 80 / size.Width;
                using (Pen border = new Pen(blue, stroke)) graphics.DrawRectangle(border, 1, 1, 78, 78);
                float handle = Math.Max(3.5f, 5f * 80 / size.Width);
                float[] positions = { 1.5f, 40f, 78.5f };
                using (SolidBrush fill = new SolidBrush(blue))
                {
                    for (int x = 0; x < 3; x++)
                        for (int y = 0; y < 3; y++)
                            if (x != 1 || y != 1)
                                graphics.FillRectangle(fill, positions[x] - handle / 2,
                                    positions[y] - handle / 2, handle, handle);
                }
            }
        }

        private static byte OpacityToAlpha(int percent)
        {
            return (byte)((percent * 255 + 50) / 100);
        }

        private static GraphicsPath RoundedRectangle(RectangleF bounds, float radius)
        {
            GraphicsPath path = new GraphicsPath();
            float diameter = radius * 2;
            path.AddArc(bounds.X, bounds.Y, diameter, diameter, 180, 90);
            path.AddArc(bounds.Right - diameter, bounds.Y, diameter, diameter, 270, 90);
            path.AddArc(bounds.Right - diameter, bounds.Bottom - diameter, diameter, diameter, 0, 90);
            path.AddArc(bounds.X, bounds.Bottom - diameter, diameter, diameter, 90, 90);
            path.CloseFigure();
            return path;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct NativePoint
        {
            public int X, Y;
            public NativePoint(int x, int y) { X = x; Y = y; }
        }
        [StructLayout(LayoutKind.Sequential)]
        private struct NativeSize
        {
            public int Width, Height;
            public NativeSize(int width, int height) { Width = width; Height = height; }
        }
        [StructLayout(LayoutKind.Sequential)]
        private struct NativeRect { public int Left, Top, Right, Bottom; }
        [StructLayout(LayoutKind.Sequential, Pack = 1)]
        private struct BlendFunction
        {
            public byte BlendOp, BlendFlags, SourceConstantAlpha, AlphaFormat;
        }

        private static IntPtr GetWindowLongValue(IntPtr window, int index)
        {
            return IntPtr.Size == 8 ? GetWindowLongPtr64(window, index) : new IntPtr(GetWindowLong32(window, index));
        }
        private static void SetWindowLongValue(IntPtr window, int index, IntPtr value)
        {
            if (IntPtr.Size == 8) SetWindowLongPtr64(window, index, value);
            else SetWindowLong32(window, index, value.ToInt32());
        }

        [DllImport("user32.dll", EntryPoint = "GetWindowLong", SetLastError = true)]
        private static extern int GetWindowLong32(IntPtr window, int index);
        [DllImport("user32.dll", EntryPoint = "GetWindowLongPtr", SetLastError = true)]
        private static extern IntPtr GetWindowLongPtr64(IntPtr window, int index);
        [DllImport("user32.dll", EntryPoint = "SetWindowLong", SetLastError = true)]
        private static extern int SetWindowLong32(IntPtr window, int index, int value);
        [DllImport("user32.dll", EntryPoint = "SetWindowLongPtr", SetLastError = true)]
        private static extern IntPtr SetWindowLongPtr64(IntPtr window, int index, IntPtr value);
        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool SetWindowPos(IntPtr window, IntPtr insertAfter, int x, int y, int cx, int cy, uint flags);
        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool UpdateLayeredWindow(IntPtr window, IntPtr screenDc, ref NativePoint destination,
            ref NativeSize size, IntPtr sourceDc, ref NativePoint source, int colorKey, ref BlendFunction blend, uint flags);
        [DllImport("user32.dll")]
        private static extern IntPtr GetDC(IntPtr window);
        [DllImport("user32.dll")]
        private static extern int ReleaseDC(IntPtr window, IntPtr dc);
        [DllImport("gdi32.dll", SetLastError = true)]
        private static extern IntPtr CreateCompatibleDC(IntPtr dc);
        [DllImport("gdi32.dll")]
        private static extern bool DeleteDC(IntPtr dc);
        [DllImport("gdi32.dll")]
        private static extern IntPtr SelectObject(IntPtr dc, IntPtr obj);
        [DllImport("gdi32.dll")]
        private static extern bool DeleteObject(IntPtr obj);
    }
}
