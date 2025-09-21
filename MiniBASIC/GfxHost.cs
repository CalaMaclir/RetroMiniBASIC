// ★ GfxHost.cs (double-buffered)
using System;
using System.Collections.Concurrent;
using System.Drawing;
using System.Drawing.Imaging;
using System.Threading;
using System.Windows.Forms;

namespace MiniBasicIL
{
    /// <summary>
    /// Graphics host for Retro Mini BASIC interpreter.
    /// - Double buffered: backBuffer (draw/POINT) and frontBuffer (display-only)
    /// - Thread-safe: UI thread owns PictureBox/frontBuffer, worker uses backBuffer under lock
    /// - Temporary color helper for PSET/LINE/CIRCLE/BOX
    /// - Pen position is maintained for LINE shorthand
    /// </summary>
    sealed class GfxHost : IDisposable
    {
        public static readonly GfxHost Instance = new GfxHost();

        private Thread? uiThread;
        private Form? form;
        private PictureBox? pb;

        // Double buffers
        private Bitmap? backBuffer;   // draw target & POINT source
        private Bitmap? frontBuffer;  // UI display image (clone of backBuffer)
        private Graphics? g;          // Graphics for backBuffer

        private readonly object sync = new();
        private readonly BlockingCollection<Action> uiQueue = new();

        private int width = 640, height = 480;
        private Color penColor = Color.White;
        private SolidBrush? brush;

        // Pen position for LINE shorthand
        private int penX = 0, penY = 0;
        public void SetPen(int x, int y) { penX = x; penY = y; }
        public (int x, int y) GetPen() => (penX, penY);

        // Text drawing state
        private int textX = 0, textY = 0;
        private Font? font;

        private GfxHost() { }

        public void EnsureScreen(int w = 640, int h = 480)
        {
            if (form != null && !form.IsDisposed) return;

            width = w; height = h;
            uiThread = new Thread(() =>
            {
                ApplicationConfiguration.Initialize();
                form = new Form
                {
                    Text = "Mini BASIC Graphics",
                    ClientSize = new Size(width, height),
                    FormBorderStyle = FormBorderStyle.FixedSingle,
                    MaximizeBox = false
                };
                pb = new PictureBox { Dock = DockStyle.Fill, SizeMode = PictureBoxSizeMode.Normal };
                form.Controls.Add(pb);

                lock (sync)
                {
                    backBuffer = new Bitmap(width, height, PixelFormat.Format32bppArgb);
                    g = Graphics.FromImage(backBuffer);
                    g.Clear(Color.Black);

                    frontBuffer?.Dispose();
                    frontBuffer = (Bitmap)backBuffer.Clone();  // display clone
                    pb.Image = frontBuffer;

                    brush?.Dispose();
                    brush = new SolidBrush(penColor);

                    // Initialize default monospace font for text
                    font?.Dispose();
                    font = new Font(FontFamily.GenericMonospace, 12f, FontStyle.Regular, GraphicsUnit.Pixel);
                    textX = 0; textY = 0;
                }

                var timer = new System.Windows.Forms.Timer { Interval = 10 };
                timer.Tick += (_, __) =>
                {
                    while (uiQueue.TryTake(out var act)) act();
                };
                timer.Start();

                Application.Run(form);
            });
            uiThread.IsBackground = true;
            uiThread.SetApartmentState(ApartmentState.STA);
            uiThread.Start();

            // wait UI comes up
            SpinWait.SpinUntil(() => form != null && pb != null && backBuffer != null, 1000);
        }

        public void Cls()
        {
            if (g == null) return;
            lock (sync) { g!.Clear(Color.Black); }
        }

        public void ColorRGB(int r, int g_, int b)
        {
            r = Math.Clamp(r, 0, 255);
            g_ = Math.Clamp(g_, 0, 255);
            b = Math.Clamp(b, 0, 255);
            var newColor = Color.FromArgb(255, r, g_, b);
            lock (sync)
            {
                penColor = newColor;
                brush?.Dispose();
                brush = new SolidBrush(penColor);
            }
        }

        /// <summary>
        /// Execute draw action with a temporary color; restores after action.
        /// If any of r,g,b is null, uses current color.
        /// </summary>
        public void DoWithTempColor(int? r, int? g_, int? b, Action draw)
        {
            if (r.HasValue && g_.HasValue && b.HasValue)
            {
                Color prev;
                lock (sync) prev = penColor;
                ColorRGB(r.Value, g_.Value, b.Value);
                try { draw(); }
                finally { ColorRGB(prev.R, prev.G, prev.B); }
            }
            else
            {
                draw();
            }
        }

        public bool GetPixelNonBlack(int x, int y)
        {
            if (backBuffer == null) return false;
            if (x < 0 || y < 0 || x >= width || y >= height) return false;
            lock (sync)
            {
                try
                {
                    var c = backBuffer.GetPixel(x, y);
                    return c.R != 0 || c.G != 0 || c.B != 0;
                }
                catch
                {
                    // rare contention: treat as not lit
                    return false;
                }
            }
        }

        public void PSet(int x, int y)
        {
            if (x < 0 || y < 0 || x >= width || y >= height) { SetPen(x, y); return; }
            if (g == null) return;
            lock (sync)
            {
                g!.FillRectangle(brush!, x, y, 1, 1);
            }
            SetPen(x, y);
        }

        // using System.Drawing;
        // using System.Drawing.Imaging;

        // using System.Drawing;
        // using System.Drawing.Imaging;

        public void Line(int x1, int y1, int x2, int y2)
        {
            if (backBuffer == null) { SetPen(x2, y2); return; }

            // ブレゼンハム直描き + 軸平行はラン塗りで高速化
            lock (sync)
            {
                var rect = new Rectangle(0, 0, width, height);
                BitmapData? data = null;
                try
                {
                    data = backBuffer.LockBits(rect, ImageLockMode.ReadWrite, PixelFormat.Format32bppArgb);
                    int stride = data.Stride;
                    IntPtr scan0 = data.Scan0;

                    unsafe
                    {
                        byte* basePtr = (byte*)scan0.ToPointer();
                        int argb = penColor.ToArgb();

                        // --- 水平線: 1行の連続ランを一気に塗る ---
                        if (y1 == y2)
                        {
                            int y = y1;
                            if ((uint)y < (uint)height)
                            {
                                if (x2 < x1) { int t = x1; x1 = x2; x2 = t; }
                                int xs = Math.Max(0, x1);
                                int xe = Math.Min(width - 1, x2);
                                if (xs <= xe)
                                    WriteArgbRunHorizontal(basePtr, stride, y, xs, xe, argb);
                            }
                        }
                        // --- 垂直線: 縦方向に stride だけ進めながら書く ---
                        else if (x1 == x2)
                        {
                            int x = x1;
                            if ((uint)x < (uint)width)
                            {
                                if (y2 < y1) { int t = y1; y1 = y2; y2 = t; }
                                int ys = Math.Max(0, y1);
                                int ye = Math.Min(height - 1, y2);
                                if (ys <= ye)
                                {
                                    byte* p = basePtr + ys * stride + x * 4;
                                    for (int y = ys; y <= ye; y++)
                                    {
                                        *(int*)p = argb;
                                        p += stride;
                                    }
                                }
                            }
                        }
                        // --- 斜め線: ブレゼンハムをそのまま（1回ロックで高速） ---
                        else
                        {
                            int dx = Math.Abs(x2 - x1), sx = x1 < x2 ? 1 : -1;
                            int dy = -Math.Abs(y2 - y1), sy = y1 < y2 ? 1 : -1;
                            int err = dx + dy;

                            int x = x1, y = y1;
                            while (true)
                            {
                                if ((uint)x < (uint)width && (uint)y < (uint)height)
                                {
                                    *(int*)(basePtr + y * stride + x * 4) = argb;
                                }
                                if (x == x2 && y == y2) break;

                                int e2 = err << 1; // 2*err
                                if (e2 >= dy) { err += dy; x += sx; }
                                if (e2 <= dx) { err += dx; y += sy; }
                            }
                        }
                    }
                }
                finally
                {
                    if (data != null) backBuffer.UnlockBits(data);
                }
            }

            // LINE の短縮記法整合のため、最終ペン位置は終点へ
            SetPen(x2, y2);
        }

        // 水平ランをまとめて塗る（xs..xe を同色で埋める）
        private unsafe void WriteArgbRunHorizontal(byte* basePtr, int stride, int y, int xs, int xe, int argb)
        {
            byte* p = basePtr + y * stride + xs * 4;
            int count = xe - xs + 1;

            // 簡易アンロール（4ピクセルずつ）
            int quads = count >> 2;         // /4
            for (int i = 0; i < quads; i++)
            {
                *(int*)p = argb; p += 4;
                *(int*)p = argb; p += 4;
                *(int*)p = argb; p += 4;
                *(int*)p = argb; p += 4;
            }
            // 端数
            int rem = count & 3;
            for (int r = 0; r < rem; r++) { *(int*)p = argb; p += 4; }
        }



        public void Circle(int cx, int cy, int r)
        {
            int x = r, y = 0, err = 0;
            while (x >= y)
            {
                PSet(cx + x, cy + y); PSet(cx + y, cy + x);
                PSet(cx - y, cy + x); PSet(cx - x, cy + y);
                PSet(cx - x, cy - y); PSet(cx - y, cy - x);
                PSet(cx + y, cy - x); PSet(cx + x, cy - y);
                y++;
                if (err <= 0) { err += 2 * y + 1; }
                if (err > 0) { x--; err -= 2 * x + 1; }
            }
            SetPen(cx + r, cy);
        }

        public void Box(int x1, int y1, int x2, int y2, bool fill)
        {
            if (g == null) return;
            int l = Math.Min(x1, x2), t = Math.Min(y1, y2);
            int w = Math.Abs(x2 - x1) + 1;
            int h = Math.Abs(y2 - y1) + 1;
            var rect = new Rectangle(l, t, w, h);
            lock (sync)
            {
                if (fill)
                {
                    using var br = new SolidBrush(penColor);
                    g!.FillRectangle(br, rect);
                }
                else
                {
                    using var pen = new Pen(penColor);
                    g!.DrawRectangle(pen, rect);
                }
            }
            SetPen(x2, y2);
        }

        /// <summary>
        /// Seed fill (flood fill) at (x,y), replacing the region of the start pixel color with current penColor.
        /// </summary>
        // GfxHost.cs
        public void PaintFill(int sx, int sy)
        {
            if (sx < 0 || sy < 0 || sx >= width || sy >= height) return;
            if (backBuffer == null) return;

            lock (sync)
            {
                // 32bpp ARGB 固定
                var rect = new Rectangle(0, 0, width, height);
                BitmapData? data = null;
                try
                {
                    data = backBuffer.LockBits(rect, ImageLockMode.ReadWrite, PixelFormat.Format32bppArgb);
                    int stride = data.Stride;
                    IntPtr scan0 = data.Scan0;

                    unsafe
                    {
                        byte* basePtr = (byte*)scan0.ToPointer();
                        // 画素読みヘルパ
                        int ReadArgb(int x, int y)
                        {
                            return *(int*)(basePtr + y * stride + x * 4);
                        }
                        // 画素書き（現在ペン色）
                        int pen = penColor.ToArgb();
                        void WriteArgbRun(int lx, int rx, int y)
                        {
                            // [lx..rx] を int 単位で一気に塗る
                            byte* p = basePtr + y * stride + lx * 4;
                            int count = rx - lx + 1;
                            int* pi = (int*)p;
                            for (int i = 0; i < count; i++) pi[i] = pen;
                        }

                        int seed = ReadArgb(sx, sy);
                        if (seed == pen) return; // すでに同色なら何もしない

                        // スキャンライン探索用スタック（x,y を積む）
                        var stack = new Stack<(int x, int y)>();
                        stack.Push((sx, sy));

                        while (stack.Count > 0)
                        {
                            var (x, y) = stack.Pop();

                            // すでに別色に変わっていたらスキップ
                            if (x < 0 || y < 0 || x >= width || y >= height) continue;
                            if (ReadArgb(x, y) != seed) continue;

                            // 左へ
                            int lx = x;
                            while (lx - 1 >= 0 && ReadArgb(lx - 1, y) == seed) lx--;

                            // 右へ
                            int rx = x;
                            while (rx + 1 < width && ReadArgb(rx + 1, y) == seed) rx++;

                            // この水平区間をまとめて塗る
                            WriteArgbRun(lx, rx, y);

                            // 上下行を走査して、seed の連続区間の先頭だけ push
                            void ScanNeighborRow(int ny)
                            {
                                if (ny < 0 || ny >= height) return;
                                int cx = lx;
                                while (cx <= rx)
                                {
                                    // seed の連続領域の先頭を見つける
                                    bool found = false;
                                    while (cx <= rx)
                                    {
                                        if (ReadArgb(cx, ny) == seed) { found = true; break; }
                                        cx++;
                                    }
                                    if (!found) break;

                                    // 先頭を push
                                    stack.Push((cx, ny));

                                    // この連続領域をスキップ
                                    while (cx <= rx && ReadArgb(cx, ny) == seed) cx++;
                                }
                            }

                            ScanNeighborRow(y - 1);
                            ScanNeighborRow(y + 1);
                        }
                    }
                }
                finally
                {
                    if (data != null) backBuffer.UnlockBits(data);
                }
            }

            // 最後にペン座標は触らない（従来仕様に合わせるなら SetPen(sx, sy) してもOK）
        }


        public void Save(string path)
        {
            lock (sync)
            {
                backBuffer?.Save(path, ImageFormat.Png);
            }
        }

        /// <summary>
        /// Set text cursor in pixel coordinates.
        /// </summary>
        public void TextLocate(int x, int y)
        {
            lock (sync)
            {
                textX = x;
                textY = y;
            }
        }

        /// <summary>
        /// Draw string at current text cursor; advances cursor.
        /// '\n' causes line break (x=0, y += line height).
        /// </summary>
        public void TextPrint(string s)
        {
            if (g == null || brush == null || font == null) return;
            s = s.Replace("\r\n", "\n").Replace("\r", "\n");
            lock (sync)
            {
                var lines = s.Split('\n');
                for (int i = 0; i < lines.Length; i++)
                {
                    string line = lines[i];
                    if (line.Length > 0)
                    {
                        g.DrawString(line, font, brush, textX, textY);
                        var size = g.MeasureString(line, font);
                        if (i < lines.Length - 1)
                        {
                            textX = 0;
                            textY += (int)Math.Ceiling(size.Height);
                        }
                        else
                        {
                            textX += (int)Math.Ceiling(size.Width);
                        }
                    }
                    else
                    {
                        // empty segment on break: move to next line
                        var sizeH = g.MeasureString("A", font).Height;
                        textX = 0;
                        textY += (int)Math.Ceiling(sizeH);
                    }
                }
            }
        }

        /// <summary>
        /// Clone backBuffer and swap into PictureBox on UI thread.
        /// </summary>
        public void Flush()
        {
            if (pb == null) return;
            uiQueue.Add(() =>
            {
                Bitmap? clone = null;
                Bitmap? oldFront = null;
                lock (sync)
                {
                    if (backBuffer != null)
                        clone = (Bitmap)backBuffer.Clone();
                    oldFront = frontBuffer;
                    frontBuffer = clone;
                }
                pb.Image = frontBuffer;
                oldFront?.Dispose();
                pb.Invalidate();
            });
        }

        public void Dispose()
        {
            if (form == null) return;
            uiQueue.Add(() => form!.Close());
            lock (sync)
            {
                brush?.Dispose(); brush = null;
                font?.Dispose();  font = null;
                g?.Dispose(); g = null;
                backBuffer?.Dispose(); backBuffer = null;
                frontBuffer?.Dispose(); frontBuffer = null;
            }
            uiThread = null;
        }
    }
}
