// ★ GfxHost.cs (double-buffered)
using System;
using System.Buffers;                  // ArrayPool
using System.Collections.Concurrent;
using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Forms;
// using 追加
using System.Drawing.Drawing2D;


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

        // GfxHost.cs のフィールド付近に追加
        private const PixelFormat Pix = PixelFormat.Format32bppArgb;

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
            if ((uint)x >= (uint)width || (uint)y >= (uint)height) return false;

            lock (sync)
            {
                // backBuffer を短時間 LockBits → ピクセル直読み（32bpp）
                var rect = new Rectangle(x, y, 1, 1);
                var data = backBuffer.LockBits(rect, ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
                try
                {
                    unsafe
                    {
                        int argb = *(int*)data.Scan0.ToPointer();
                        // Aは無視しRGBだけチェック
                        return (argb & 0x00FFFFFF) != 0;
                    }
                }
                finally { backBuffer.UnlockBits(data); }
            }
        }


        public void PSet(int x, int y)
        {
            SetPen(x, y);
            if ((uint)x >= (uint)width || (uint)y >= (uint)height) return;
            if (backBuffer == null) return;

            lock (sync)
            {
                var rect = new Rectangle(0, 0, width, height);
                BitmapData? data = null;
                try
                {
                    data = backBuffer.LockBits(rect, ImageLockMode.ReadWrite, PixelFormat.Format32bppArgb);
                    unsafe
                    {
                        byte* basePtr = (byte*)data.Scan0.ToPointer();
                        *(int*)(basePtr + y * data.Stride + x * 4) = penColor.ToArgb();
                    }
                }
                finally { if (data != null) backBuffer.UnlockBits(data); }
            }
        }


        public void PSetArray(int[] xyPairs, bool optimizeRuns = true)
        {
            if (backBuffer == null || xyPairs == null || xyPairs.Length < 2) return;
            if ((xyPairs.Length & 1) != 0) throw new ArgumentException("xyPairs length must be even.");

            // ==== 自動並列のしきい値（環境に合わせて調整可）====
            const int POINTS_PARALLEL_THRESHOLD = 40_000; // 全体点数がこれ以上なら並列検討
            const int AVGROW_PARALLEL_THRESHOLD = 24;      // 1行あたり平均点数がこれ以上なら並列有利とみなす

            lock (sync)
            {
                var rect = new Rectangle(0, 0, width, height);
                BitmapData? data = null;
                // rowsByY: 各 y 行に対応する「x の一時バッファ」
                // List<int> の代わりに「プール借用の int[] + 使用数 count」で管理して、GC/再確保を避ける
                int[][] rowsByY = new int[height][];
                int[] counts = new int[height]; // 各行の有効要素数

                try
                {
                    // ====== 前処理：行バケツ分け（単スレ） ======
                    // 初回レンタルサイズの目安（平均 8 点/行から開始。足りなければ倍々で再レンタル）
                    const int INITIAL_BUCKET = 8;

                    int totalValid = 0;
                    for (int i = 0; i < xyPairs.Length; i += 2)
                    {
                        int x = xyPairs[i];
                        int y = xyPairs[i + 1];
                        if ((uint)x >= (uint)width || (uint)y >= (uint)height) continue;
                        totalValid++;

                        var buf = rowsByY[y];
                        int cnt = counts[y];

                        if (buf == null)
                        {
                            buf = ArrayPool<int>.Shared.Rent(INITIAL_BUCKET);
                            rowsByY[y] = buf;
                        }
                        else if (cnt >= buf.Length)
                        {
                            // 足りないので倍サイズをレンタル → 旧バッファを返却
                            int newSize = buf.Length << 1;
                            var bigger = ArrayPool<int>.Shared.Rent(newSize);
                            Array.Copy(buf, bigger, buf.Length);
                            ArrayPool<int>.Shared.Return(buf);
                            buf = bigger;
                            rowsByY[y] = buf;
                        }

                        buf[cnt] = x;
                        counts[y] = cnt + 1;
                    }

                    // 早期終了（有効点なし）
                    if (totalValid == 0) return;

                    // ====== LockBits で 1 回の書き込みセッション ======
                    data = backBuffer.LockBits(rect, ImageLockMode.ReadWrite, PixelFormat.Format32bppArgb);
                    int stride = data.Stride;
                    IntPtr scan0 = data.Scan0;
                    int argb = penColor.ToArgb();

                    unsafe
                    {
                        byte* basePtr = (byte*)scan0.ToPointer();

                        if (!optimizeRuns)
                        {
                            // 最適化なし：LockBits 1回 + 逐次ドット（でも十分速いことが多い）
                            // 並列化の旨味が薄いので単スレでOK
                            for (int y = 0; y < height; y++)
                            {
                                int cnt = counts[y];
                                if (cnt == 0) continue;
                                var xs = rowsByY[y];
                                for (int k = 0; k < cnt; k++)
                                {
                                    int x = xs[k];
                                    *(int*)(basePtr + y * stride + x * 4) = argb;
                                }
                            }
                        }
                        else
                        {
                            // ====== 自動並列判定 ======
                            // アクティブ行数と平均点数を見て決定
                            int activeRows = 0;
                            for (int y = 0; y < height; y++) if (counts[y] > 0) activeRows++;
                            int avgPerRow = activeRows > 0 ? totalValid / activeRows : 0;
                            bool parallel = (totalValid >= POINTS_PARALLEL_THRESHOLD) && (avgPerRow >= AVGROW_PARALLEL_THRESHOLD);

                            // ====== ソート＋ラン塗り ======
                            if (!parallel)
                            {
                                for (int y = 0; y < height; y++)
                                {
                                    int cnt = counts[y];
                                    if (cnt <= 0) continue;

                                    var xs = rowsByY[y];
                                    // xs[0..cnt) を昇順ソート
                                    Array.Sort(xs, 0, cnt);

                                    int runStart = xs[0];
                                    int prev = xs[0];
                                    for (int i = 1; i < cnt; i++)
                                    {
                                        int x = xs[i];
                                        if (x == prev || x == prev + 1)
                                        {
                                            prev = x; // 連続または重複 → ラン継続
                                        }
                                        else
                                        {
                                            WriteArgbRunHorizontal(basePtr, stride, y, runStart, prev, argb);
                                            runStart = prev = x;
                                        }
                                    }
                                    WriteArgbRunHorizontal(basePtr, stride, y, runStart, prev, argb);
                                }
                            }
                            else
                            {
                                // 行単位で独立 → 安全に並列化可能
                                System.Threading.Tasks.Parallel.For(0, height, y =>
                                {
                                    int cnt = counts[y];
                                    if (cnt <= 0) return;

                                    var xs = rowsByY[y];
                                    Array.Sort(xs, 0, cnt);

                                    int runStart = xs[0];
                                    int prev = xs[0];
                                    for (int i = 1; i < cnt; i++)
                                    {
                                        int x = xs[i];
                                        if (x == prev || x == prev + 1)
                                        {
                                            prev = x;
                                        }
                                        else
                                        {
                                            WriteArgbRunHorizontal(basePtr, stride, y, runStart, prev, argb);
                                            runStart = prev = x;
                                        }
                                    }
                                    WriteArgbRunHorizontal(basePtr, stride, y, runStart, prev, argb);
                                });
                            }
                        }
                    }
                }
                finally
                {
                    if (data != null) backBuffer.UnlockBits(data);
                    // 借りた配列は必ず返却（掃除）
                    for (int y = 0; y < rowsByY.Length; y++)
                    {
                        var buf = rowsByY[y];
                        if (buf != null) ArrayPool<int>.Shared.Return(buf);
                        rowsByY[y] = null!;
                    }
                }
            }
        }


        /// <summary>
        /// IEnumerable&lt;Point&gt; 版（利便性用）
        /// </summary>
        public void PSetArray(IEnumerable<Point> points, bool optimizeRuns = true)
        {
            if (points == null) return;

            // ① まず概算で小さめにレンタル（足りなければ倍々拡張）
            var buf = ArrayPool<int>.Shared.Rent(1024);
            int n = 0;

            try
            {
                foreach (var p in points)
                {
                    if (n + 2 > buf.Length)
                    {
                        var bigger = ArrayPool<int>.Shared.Rent(buf.Length << 1);
                        Array.Copy(buf, bigger, n);
                        ArrayPool<int>.Shared.Return(buf);
                        buf = bigger;
                    }
                    buf[n++] = p.X;
                    buf[n++] = p.Y;
                }

                // ② n だけを使って int[] なしでコアへ
                PSetArray(buf.AsSpan(0, n).ToArray(), optimizeRuns); // ★ さらに攻めるなら PSetArray(Span<int>) オーバーロードを新設
            }
            finally
            {
                ArrayPool<int>.Shared.Return(buf);
            }
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
        /// <summary>
        /// 水平ラン [xs..xe] を 32bppARGB 固定色で一括塗り（SIMD/Fill最適化）
        /// xs/xe はクランプ済み前提
        /// </summary>
        private unsafe void WriteArgbRunHorizontal(byte* basePtr, int stride, int y, int xs, int xe, int argb)
        {
            int len = xe - xs + 1;
            if (len <= 0) return;
            // 対象行の先頭ポインタ（int* 単位）
            int* dst = (int*)(basePtr + y * stride + xs * 4);
            // 行ランを Span<int> 化して Fill
            var span = new Span<int>(dst, len);
            span.Fill(argb);
        }




        public void Circle(int cx, int cy, int r)
        {
            if (r < 0) return;

            // 8対称の点を蓄積
            // 典型的に点数は O(r)。配列を事前に十分めに確保して push
            var pts = new List<int>(r * 16); // 安全側に多め
            int x = r, y = 0, err = 0;

            void Add8(int px, int py)
            {
                // [x,y]を順に push（偶数長）
                pts.Add(cx + px); pts.Add(cy + py);
                pts.Add(cx + py); pts.Add(cy + px);
                pts.Add(cx - py); pts.Add(cy + px);
                pts.Add(cx - px); pts.Add(cy + py);
                pts.Add(cx - px); pts.Add(cy - py);
                pts.Add(cx - py); pts.Add(cy - px);
                pts.Add(cx + py); pts.Add(cy - px);
                pts.Add(cx + px); pts.Add(cy - py);
            }

            while (x >= y)
            {
                Add8(x, y);
                y++;
                if (err <= 0) { err += 2 * y + 1; }
                if (err > 0) { x--; err -= 2 * x + 1; }
            }

            // 一括描画（行最適化あり）
            PSetArray(pts.ToArray(), optimizeRuns: true);

            // ペン位置は従来互換として円の右端へ
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
            if (backBuffer == null) return;
            if ((uint)sx >= (uint)width || (uint)sy >= (uint)height) return;

            lock (sync)
            {
                var rect = new Rectangle(0, 0, width, height);
                BitmapData? data = null;
                try
                {
                    data = backBuffer.LockBits(rect, ImageLockMode.ReadWrite, PixelFormat.Format32bppArgb);
                    int stride = data.Stride;
                    IntPtr scan0 = data.Scan0;
                    int fillArgb = penColor.ToArgb();

                    unsafe
                    {
                        byte* basePtr = (byte*)scan0.ToPointer();
                        // 参照色（ターゲット色）取得
                        int* pSeed = (int*)(basePtr + sy * stride + sx * 4);
                        int targetArgb = *pSeed;
                        if (targetArgb == fillArgb) return; // すでに同色なら何もしない

                        // スタックに行ランを積む
                        var stack = new Stack<(int x, int y)>(512);
                        stack.Push((sx, sy));

                        while (stack.Count > 0)
                        {
                            var (x, y) = stack.Pop();

                            // 左へ伸長
                            int xl = x;
                            int* p = (int*)(basePtr + y * stride + x * 4);
                            while (xl >= 0 && *p == targetArgb)
                            {
                                xl--;
                                p--;
                            }
                            xl++; p++; // 一つ戻す

                            // 右へ伸長
                            int xr = x;
                            int* pr = (int*)(basePtr + y * stride + x * 4);
                            while (xr < width && *pr == targetArgb)
                            {
                                xr++;
                                pr++;
                            }
                            xr--; // 一つ戻す

                            // この行の [xl..xr] を一気に塗る（SIMD Fill）
                            WriteArgbRunHorizontal(basePtr, stride, y, xl, xr, fillArgb);

                            // 上下行を走査して、未塗り（= targetArgb）の連結ランの種を積む
                            // y-1 行
                            if (y > 0)
                            {
                                int y2 = y - 1;
                                int xs = xl, xe = xr;
                                int* row = (int*)(basePtr + y2 * stride + xs * 4);
                                bool inRun = false;
                                for (int xx = xs; xx <= xe; xx++, row++)
                                {
                                    if (*row == targetArgb)
                                    {
                                        if (!inRun) { stack.Push((xx, y2)); inRun = true; }
                                    }
                                    else inRun = false;
                                }
                            }
                            // y+1 行
                            if (y + 1 < height)
                            {
                                int y2 = y + 1;
                                int xs = xl, xe = xr;
                                int* row = (int*)(basePtr + y2 * stride + xs * 4);
                                bool inRun = false;
                                for (int xx = xs; xx <= xe; xx++, row++)
                                {
                                    if (*row == targetArgb)
                                    {
                                        if (!inRun) { stack.Push((xx, y2)); inRun = true; }
                                    }
                                    else inRun = false;
                                }
                            }
                        }
                    }
                }
                finally
                {
                    if (data != null) backBuffer.UnlockBits(data);
                }
            }
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
        // GfxHost.cs にユーティリティを追加
        private void EnsureFrontBuffer()
        {
            if (backBuffer == null) return;

            if (frontBuffer == null ||
                frontBuffer.Width  != backBuffer.Width ||
                frontBuffer.Height != backBuffer.Height ||
                frontBuffer.PixelFormat != Pix)
            {
                frontBuffer?.Dispose();
                frontBuffer = new Bitmap(backBuffer.Width, backBuffer.Height, Pix);
            }
        }

        private static unsafe void CopyBackToFrontViaLockBits(Bitmap src, Bitmap dst)
        {
            var rect = new Rectangle(0, 0, src.Width, src.Height);
            BitmapData? s = null, d = null;
            try
            {
                s = src.LockBits(rect, ImageLockMode.ReadOnly, Pix);
                d = dst.LockBits(rect, ImageLockMode.WriteOnly, Pix);

                int bytesPerRow = Math.Abs(s.Stride) < Math.Abs(d.Stride) ? Math.Abs(s.Stride) : Math.Abs(d.Stride);
                byte* sp = (byte*)s.Scan0.ToPointer();
                byte* dp = (byte*)d.Scan0.ToPointer();

                // 行ごとにコピー（Stride考慮）
                for (int y = 0; y < rect.Height; y++)
                {
                    Buffer.MemoryCopy(sp, dp, bytesPerRow, bytesPerRow);
                    sp += s.Stride;
                    dp += d.Stride;
                }
            }
            finally
            {
                if (s != null) src.UnlockBits(s);
                if (d != null) dst.UnlockBits(d);
            }
        }

        /// <summary>
        /// Clone backBuffer and swap into PictureBox on UI thread.
        /// </summary>
        // 置き換え版 Flush()
        // - Clone() を使わず、frontBuffer を使い回し
        // - 行コピーで back→front を転送
        public void Flush()
        {
            if (pb == null || backBuffer == null) return;

            void DoCopyOnUi()
            {
                lock (sync)
                {
                    EnsureFrontBuffer();
                    if (frontBuffer == null) return;

                    // front を LockBits せずに安全にコピー
                    using (var g = Graphics.FromImage(frontBuffer))
                    {
                        g.CompositingMode = CompositingMode.SourceCopy;   // そのまま転写
                        g.InterpolationMode = InterpolationMode.NearestNeighbor;
                        g.PixelOffsetMode   = PixelOffsetMode.Half;
                        g.DrawImageUnscaled(backBuffer, 0, 0);
                    }

                    if (!ReferenceEquals(pb.Image, frontBuffer))
                        pb.Image = frontBuffer;

                    pb.Invalidate();
                }
            }

            // UIスレッドへディスパッチ（ここが重要）
            if (pb.InvokeRequired)
                pb.BeginInvoke((Action)DoCopyOnUi);
            else
                DoCopyOnUi();
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
