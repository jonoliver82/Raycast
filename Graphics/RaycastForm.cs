using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace GraphicsTest
{
    public partial class RaycastForm : Form
    {
        // Simple raycaster fields
        private const double DEG_TO_RAD = Math.PI / 180.0;
        private Bitmap _textureAtlas = null;
        private Rectangle[] _textureRects = null;
        private int _texCols = 0;
        private int _texRows = 0;
        private int[][] _texturePixels = null;
        private int[] _textureWidths = null;
        private int[] _textureHeights = null;

        private Bitmap _frameBufferBitmap = null;
        private int[] _frameBufferPixels = null;
        private readonly int[,] _map = new int[,]
        {
            // 0 = empty, other integers represent wall color indices
            {1,1,1,1,1,1,1,1,1,1,1,1},
            {1,0,0,0,0,0,0,0,0,0,0,1},
            {1,0,2,0,0,0,0,3,0,0,0,1},
            {1,0,0,0,0,0,0,0,0,4,0,1},
            {1,0,0,0,5,0,0,0,0,0,0,1},
            {1,0,0,0,0,0,6,0,0,0,0,1},
            {1,0,0,7,0,0,0,0,8,0,0,1},
            {1,0,0,0,0,0,0,0,0,0,0,1},
            {1,0,0,0,0,9,0,0,0,0,0,1},
            {1,0,0,0,0,0,0,0,0,0,0,1},
            {1,0,0,0,0,0,0,0,0,0,0,1},
            {1,1,1,1,1,1,1,1,1,1,1,1},
        };

        private readonly Color[] _palette = new Color[]
        {
            Color.Black,       // 0 - unused (floor/ceiling)
            Color.FromArgb(200,200,200), // 1 - light gray
            Color.SaddleBrown, // 2
            Color.DarkBlue,    // 3
            Color.DarkOliveGreen, //4
            Color.DarkRed,     //5
            Color.Orange,      //6
            Color.Purple,      //7
            Color.Teal,        //8
            Color.Yellow,      //9
        };

        private double _playerX = 3.5; // in map units
        private double _playerY = 3.5;
        private double _playerAngle = 0.0; // radians

        private readonly double _fov = 60.0 * DEG_TO_RAD;
        private readonly double _moveSpeed = 0.08; // map units per key press
        private readonly double _rotSpeed = 5.0 * DEG_TO_RAD; // radians per key press
        private readonly int _miniMapSize = 160; // pixels for square minimap
        private Label _fpsLabel;
        private Stopwatch _fpsStopwatch = new Stopwatch();
        private int _fpsFrameCount = 0;
        private double _currentFps = 0.0;
        private bool _useBilinear = false; // toggleable

        public RaycastForm()
        {
            InitializeComponent();
            DoubleBuffered = true;
            KeyPreview = true;
            Application.Idle += (s, e) => Invalidate();

            // Try load texture atlas from executable folder named "textures.jpg"
            try
            {
                string path = System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "textures.jpg");
                if (System.IO.File.Exists(path))
                {
                    _textureAtlas = (Bitmap)Image.FromFile(path);
                    // assume 4 columns by computed rows (common atlas layout)
                    _texCols = 4;
                    _texRows = Math.Max(1, (_textureAtlas.Height * _texCols) / _textureAtlas.Width);
                    int tw = _textureAtlas.Width / _texCols;
                    int th = _textureAtlas.Height / _texRows;
                    _textureRects = new Rectangle[_texCols * _texRows + 1];
                    int idx = 1;
                    for (int ry = 0; ry < _texRows; ry++)
                    {
                        for (int rx = 0; rx < _texCols; rx++)
                        {
                            _textureRects[idx++] = new Rectangle(rx * tw, ry * th, tw, th);
                        }
                    }

                    // extract texture pixel arrays for fast sampling
                    _texturePixels = new int[_textureRects.Length][];
                    _textureWidths = new int[_textureRects.Length];
                    _textureHeights = new int[_textureRects.Length];
                    for (int i = 1; i < _textureRects.Length; i++)
                    {
                        var r = _textureRects[i];
                        _textureWidths[i] = r.Width;
                        _textureHeights[i] = r.Height;
                        _texturePixels[i] = new int[r.Width * r.Height];
                        BitmapData bd = _textureAtlas.LockBits(r, ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
                        try
                        {
                            Marshal.Copy(bd.Scan0, _texturePixels[i], 0, r.Width * r.Height);
                        }
                        finally
                        {
                            _textureAtlas.UnlockBits(bd);
                        }
                    }

                    // remove black border pixels from extracted textures (common atlas seam)
                    RemoveBlackBordersFromTextures();
                }
            }
            catch
            {
                // ignore loading errors and fall back to solid colors
                _textureAtlas = null;
                _textureRects = null;
            }

            // create fps label
            _fpsLabel = new Label();
            _fpsLabel.AutoSize = true;
            _fpsLabel.ForeColor = Color.White;
            _fpsLabel.BackColor = Color.FromArgb(160, 0, 0, 0);
            _fpsLabel.Location = new Point(8, Math.Max(8, this.ClientSize.Height - 24));
            _fpsLabel.Anchor = AnchorStyles.Left | AnchorStyles.Bottom;
            _fpsLabel.Text = "FPS: 0";
            Controls.Add(_fpsLabel);

            _fpsStopwatch.Restart();

            // create initial framebuffer
            _frameBufferBitmap = new Bitmap(this.ClientSize.Width, this.ClientSize.Height, PixelFormat.Format32bppArgb);
            _frameBufferPixels = new int[this.ClientSize.Width * this.ClientSize.Height];
        }

        private Color GetTextureRepresentativeColor(int texIndex)
        {
            if (texIndex <= 0 || _texturePixels == null || texIndex >= _texturePixels.Length) return Color.Magenta;
            if (_textureWidths == null || _textureHeights == null) return Color.Magenta;

            int w = _textureWidths[texIndex];
            int h = _textureHeights[texIndex];
            int[] pixels = _texturePixels[texIndex];

            long rsum = 0, gsum = 0, bsum = 0, asum = 0;
            int samples = 0;

            // sample a small central grid (5x5) and ignore very dark pixels (likely borders)
            int halfGrid = 2;
            int cx = w / 2;
            int cy = h / 2;
            for (int oy = -halfGrid; oy <= halfGrid; oy++)
            {
                int sy = cy + oy;
                if (sy < 0 || sy >= h) continue;
                for (int ox = -halfGrid; ox <= halfGrid; ox++)
                {
                    int sx = cx + ox;
                    if (sx < 0 || sx >= w) continue;
                    int c = pixels[sy * w + sx];
                    int a = (c >> 24) & 0xFF;
                    int r = (c >> 16) & 0xFF;
                    int g = (c >> 8) & 0xFF;
                    int b = c & 0xFF;

                    // skip nearly-black pixels (likely atlas separators)
                    if (r + g + b < 30) continue;

                    asum += a; rsum += r; gsum += g; bsum += b;
                    samples++;
                }
            }

            // fallback: if no good samples, sample the center pixel
            if (samples == 0)
            {
                int c = pixels[cy * w + cx];
                int a = (c >> 24) & 0xFF;
                int r = (c >> 16) & 0xFF;
                int g = (c >> 8) & 0xFF;
                int b = c & 0xFF;
                return Color.FromArgb(a, r, g, b);
            }

            int ar = (int)(rsum / samples);
            int ag = (int)(gsum / samples);
            int ab = (int)(bsum / samples);
            int aa = (int)(asum / samples);
            return Color.FromArgb(aa, ar, ag, ab);
        }

        private void RemoveBlackBordersFromTextures()
        {
            if (_texturePixels == null) return;
            const int blackThreshold = 30; // sum of r+g+b below this is considered black
            const double rowBlackFractionThreshold = 0.9; // fraction of pixels in row/col considered black

            for (int t = 1; t < _texturePixels.Length; t++)
            {
                int w = _textureWidths[t];
                int h = _textureHeights[t];
                int[] px = _texturePixels[t];

                // detect top
                int top = 0;
                for (int y = 0; y < h; y++)
                {
                    int blackCount = 0;
                    for (int x = 0; x < w; x++)
                    {
                        int c = px[y * w + x];
                        int r = (c >> 16) & 0xFF;
                        int g = (c >> 8) & 0xFF;
                        int b = c & 0xFF;
                        if (r + g + b < blackThreshold) blackCount++;
                    }
                    if ((double)blackCount / w >= rowBlackFractionThreshold) top++; else break;
                }

                // detect bottom
                int bottom = 0;
                for (int y = h - 1; y >= 0; y--)
                {
                    int blackCount = 0;
                    for (int x = 0; x < w; x++)
                    {
                        int c = px[y * w + x];
                        int r = (c >> 16) & 0xFF;
                        int g = (c >> 8) & 0xFF;
                        int b = c & 0xFF;
                        if (r + g + b < blackThreshold) blackCount++;
                    }
                    if ((double)blackCount / w >= rowBlackFractionThreshold) bottom++; else break;
                }

                // detect left
                int left = 0;
                for (int x = 0; x < w; x++)
                {
                    int blackCount = 0;
                    for (int y = 0; y < h; y++)
                    {
                        int c = px[y * w + x];
                        int r = (c >> 16) & 0xFF;
                        int g = (c >> 8) & 0xFF;
                        int b = c & 0xFF;
                        if (r + g + b < blackThreshold) blackCount++;
                    }
                    if ((double)blackCount / h >= rowBlackFractionThreshold) left++; else break;
                }

                // detect right
                int right = 0;
                for (int x = w - 1; x >= 0; x--)
                {
                    int blackCount = 0;
                    for (int y = 0; y < h; y++)
                    {
                        int c = px[y * w + x];
                        int r = (c >> 16) & 0xFF;
                        int g = (c >> 8) & 0xFF;
                        int b = c & 0xFF;
                        if (r + g + b < blackThreshold) blackCount++;
                    }
                    if ((double)blackCount / h >= rowBlackFractionThreshold) right++; else break;
                }

                // clamp crop to reasonable bounds
                int cropLeft = Math.Min(left, w - 1);
                int cropRight = Math.Min(right, w - 1 - cropLeft);
                int cropTop = Math.Min(top, h - 1);
                int cropBottom = Math.Min(bottom, h - 1 - cropTop);

                int newW = w - cropLeft - cropRight;
                int newH = h - cropTop - cropBottom;
                if (newW <= 0 || newH <= 0) continue;

                if (cropLeft != 0 || cropRight != 0 || cropTop != 0 || cropBottom != 0)
                {
                    int[] newPx = new int[newW * newH];
                    for (int yy = 0; yy < newH; yy++)
                    {
                        Array.Copy(px, (yy + cropTop) * w + cropLeft, newPx, yy * newW, newW);
                    }
                    _texturePixels[t] = newPx;
                    _textureWidths[t] = newW;
                    _textureHeights[t] = newH;
                }
            }
        }

        private int SampleTexture(int texIndex, double xf, double yf, bool bilinear)
        {
            int texW = _textureWidths[texIndex];
            int texH = _textureHeights[texIndex];
            var pixels = _texturePixels[texIndex];

            if (!bilinear)
            {
                int xi = Math.Max(0, Math.Min(texW - 1, (int)xf));
                int yi = Math.Max(0, Math.Min(texH - 1, (int)yf));
                return pixels[yi * texW + xi];
            }

            // bilinear
            int x0 = (int)Math.Floor(xf);
            int y0 = (int)Math.Floor(yf);
            int x1 = x0 + 1;
            int y1 = y0 + 1;

            double sx = xf - x0;
            double sy = yf - y0;

            x0 = Clamp(x0, 0, texW - 1);
            x1 = Clamp(x1, 0, texW - 1);
            y0 = Clamp(y0, 0, texH - 1);
            y1 = Clamp(y1, 0, texH - 1);

            int c00 = pixels[y0 * texW + x0];
            int c10 = pixels[y0 * texW + x1];
            int c01 = pixels[y1 * texW + x0];
            int c11 = pixels[y1 * texW + x1];

            // interpolate channels
            int a00 = (c00 >> 24) & 0xFF, r00 = (c00 >> 16) & 0xFF, g00 = (c00 >> 8) & 0xFF, b00 = c00 & 0xFF;
            int a10 = (c10 >> 24) & 0xFF, r10 = (c10 >> 16) & 0xFF, g10 = (c10 >> 8) & 0xFF, b10 = c10 & 0xFF;
            int a01 = (c01 >> 24) & 0xFF, r01 = (c01 >> 16) & 0xFF, g01 = (c01 >> 8) & 0xFF, b01 = c01 & 0xFF;
            int a11 = (c11 >> 24) & 0xFF, r11 = (c11 >> 16) & 0xFF, g11 = (c11 >> 8) & 0xFF, b11 = c11 & 0xFF;

            double a0 = a00 + (a10 - a00) * sx;
            double a1 = a01 + (a11 - a01) * sx;
            double a = a0 + (a1 - a0) * sy;

            double r0 = r00 + (r10 - r00) * sx;
            double r1 = r01 + (r11 - r01) * sx;
            double r = r0 + (r1 - r0) * sy;

            double g0 = g00 + (g10 - g00) * sx;
            double g1 = g01 + (g11 - g01) * sx;
            double gch = g0 + (g1 - g0) * sy;

            double b0 = b00 + (b10 - b00) * sx;
            double b1 = b01 + (b11 - b01) * sx;
            double bch = b0 + (b1 - b0) * sy;

            int ai = Clamp((int)(a + 0.5), 0, 255);
            int ri = Clamp((int)(r + 0.5), 0, 255);
            int gi = Clamp((int)(gch + 0.5), 0, 255);
            int bi = Clamp((int)(bch + 0.5), 0, 255);

            return (ai << 24) | (ri << 16) | (gi << 8) | bi;
        }

        private static int Clamp(int v, int lo, int hi)
        {
            if (v < lo) return lo;
            if (v > hi) return hi;
            return v;
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);

            Graphics g = e.Graphics;
            int screenW = ClientSize.Width;
            int screenH = ClientSize.Height;

            var miniMapHits = new List<Tuple<PointF, double>>();

            // ensure framebuffer size
            if (_frameBufferBitmap == null || _frameBufferBitmap.Width != screenW || _frameBufferBitmap.Height != screenH)
            {
                _frameBufferBitmap?.Dispose();
                _frameBufferBitmap = new Bitmap(screenW, screenH, PixelFormat.Format32bppArgb);
                _frameBufferPixels = new int[screenW * screenH];
            }

            // prepare ceiling/floor colors
            int ceilingCol = Color.LightSkyBlue.ToArgb();
            int floorCol = Color.DarkSlateGray.ToArgb();

            // fill framebuffer with ceiling and floor
            int halfH = screenH / 2;
            for (int y = 0; y < screenH; y++)
            {
                int baseIndex = y * screenW;
                int fillCol = (y < halfH) ? ceilingCol : floorCol;
                for (int xx = 0; xx < screenW; xx++)
                {
                    _frameBufferPixels[baseIndex + xx] = fillCol;
                }
            }

            int mapWidth = _map.GetLength(1);
            int mapHeight = _map.GetLength(0);

            for (int x = 0; x < screenW; x++)
            {
                double cameraX = 2.0 * x / (double)screenW - 1.0;
                double rayAngle = _playerAngle + Math.Atan(cameraX * Math.Tan(_fov / 2.0));
                double rayDirX = Math.Cos(rayAngle);
                double rayDirY = Math.Sin(rayAngle);

                int mapX = (int)_playerX;
                int mapY = (int)_playerY;

                double sideDistX;
                double sideDistY;
                double deltaDistX = (rayDirX == 0) ? 1e30 : Math.Abs(1.0 / rayDirX);
                double deltaDistY = (rayDirY == 0) ? 1e30 : Math.Abs(1.0 / rayDirY);
                int stepX;
                int stepY;

                if (rayDirX < 0)
                {
                    stepX = -1;
                    sideDistX = (_playerX - mapX) * deltaDistX;
                }
                else
                {
                    stepX = 1;
                    sideDistX = (mapX + 1.0 - _playerX) * deltaDistX;
                }
                if (rayDirY < 0)
                {
                    stepY = -1;
                    sideDistY = (_playerY - mapY) * deltaDistY;
                }
                else
                {
                    stepY = 1;
                    sideDistY = (mapY + 1.0 - _playerY) * deltaDistY;
                }

                bool hit = false;
                int side = 0;
                int texIndex = 0;

                while (!hit)
                {
                    if (sideDistX < sideDistY)
                    {
                        sideDistX += deltaDistX;
                        mapX += stepX;
                        side = 0;
                    }
                    else
                    {
                        sideDistY += deltaDistY;
                        mapY += stepY;
                        side = 1;
                    }

                    if (mapX < 0 || mapY < 0 || mapX >= mapWidth || mapY >= mapHeight)
                        break;

                    texIndex = _map[mapY, mapX];
                    if (texIndex != 0)
                        hit = true;
                }

                if (!hit) continue;

                double perpWallDist = (side == 0) ? (mapX - _playerX + (1 - stepX) / 2.0) / rayDirX
                                                  : (mapY - _playerY + (1 - stepY) / 2.0) / rayDirY;

                double hitX = _playerX + perpWallDist * rayDirX;
                double hitY = _playerY + perpWallDist * rayDirY;
                miniMapHits.Add(Tuple.Create(new PointF((float)hitX, (float)hitY), perpWallDist));

                int lineHeight = (int)(screenH / perpWallDist);
                if (lineHeight > screenH) lineHeight = screenH;
                int drawStart = -lineHeight / 2 + screenH / 2;
                if (drawStart < 0) drawStart = 0;
                int drawEnd = lineHeight / 2 + screenH / 2;
                if (drawEnd >= screenH) drawEnd = screenH - 1;

                if (_texturePixels != null && texIndex > 0 && texIndex < _texturePixels.Length)
                {
                    int texW = _textureWidths[texIndex];
                    int texH = _textureHeights[texIndex];
                    // compute wallX
                    double wallX = (side == 0) ? hitY - Math.Floor(hitY) : hitX - Math.Floor(hitX);
                    double texXf = wallX * texW;
                    if (side == 0 && rayDirX > 0) texXf = texW - texXf - 1.0;
                    if (side == 1 && rayDirY < 0) texXf = texW - texXf - 1.0;
                    int destHeight = drawEnd - drawStart + 1;
                    if (destHeight <= 0) continue;

                    for (int y = drawStart; y <= drawEnd; y++)
                    {
                        int d = y - drawStart;
                        double texYf = (d * texH) / (double)destHeight;
                        if (texYf < 0) texYf = 0;
                        if (texYf > texH - 1) texYf = texH - 1;

                        int color = SampleTexture(texIndex, texXf, texYf, _useBilinear);
                        if (side == 1)
                            color = DarkenArgb(color, 0.8f);

                        _frameBufferPixels[y * screenW + x] = color;
                    }
                }
                else
                {
                    Color col = _palette.Length > texIndex ? _palette[texIndex] : Color.Magenta;
                    int colorInt = col.ToArgb();
                    if (side == 1) colorInt = DarkenArgb(colorInt, 0.8f);
                    for (int y = drawStart; y <= drawEnd; y++)
                    {
                        _frameBufferPixels[y * screenW + x] = colorInt;
                    }
                }
            }

            // copy framebuffer pixels to bitmap (handle stride)
            BitmapData fbData = _frameBufferBitmap.LockBits(new Rectangle(0, 0, screenW, screenH), ImageLockMode.WriteOnly, PixelFormat.Format32bppArgb);
            try
            {
                int stride = fbData.Stride;
                int bytesPerRow = screenW * 4;
                IntPtr destPtr = fbData.Scan0;
                if (stride == bytesPerRow)
                {
                    Marshal.Copy(_frameBufferPixels, 0, destPtr, _frameBufferPixels.Length);
                }
                else
                {
                    for (int y = 0; y < screenH; y++)
                    {
                        IntPtr rowPtr = IntPtr.Add(destPtr, y * stride);
                        Marshal.Copy(_frameBufferPixels, y * screenW, rowPtr, screenW);
                    }
                }
            }
            finally
            {
                _frameBufferBitmap.UnlockBits(fbData);
            }

            // draw framebuffer to screen
            g.DrawImageUnscaled(_frameBufferBitmap, 0, 0);

            // Draw minimap on top-left and show ray hits
            DrawMiniMap(g, miniMapHits);

            // FPS counting
            _fpsFrameCount++;
            var elapsed = _fpsStopwatch.Elapsed.TotalSeconds;
            if (elapsed >= 1.0)
            {
                _currentFps = _fpsFrameCount / elapsed;
                _fpsFrameCount = 0;
                _fpsStopwatch.Restart();
                _fpsLabel.Text = string.Format("FPS: {0:0}", _currentFps);
                _fpsLabel.Location = new Point(8, this.ClientSize.Height - _fpsLabel.Height - 8);
            }
        }

        private int DarkenArgb(int colorInt, float v)
        {
            int a = (colorInt >> 24) & 0xFF;
            int r = (colorInt >> 16) & 0xFF;
            int g = (colorInt >> 8) & 0xFF;
            int b = colorInt & 0xFF;

            r = (int)(r * v);
            g = (int)(g * v);
            b = (int)(b * v);

            if (r < 0) r = 0; if (r > 255) r = 255;
            if (g < 0) g = 0; if (g > 255) g = 255;
            if (b < 0) b = 0; if (b > 255) b = 255;

            return (a << 24) | (r << 16) | (g << 8) | b;
        }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            base.OnKeyDown(e);

            switch (e.KeyCode)
            {
                case Keys.Up:
                case Keys.W:
                    {
                        double nx = _playerX + Math.Cos(_playerAngle) * _moveSpeed;
                        double ny = _playerY + Math.Sin(_playerAngle) * _moveSpeed;
                        TryMove(nx, ny);
                        break;
                    }
                case Keys.Down:
                case Keys.S:
                    {
                        double nx = _playerX - Math.Cos(_playerAngle) * _moveSpeed;
                        double ny = _playerY - Math.Sin(_playerAngle) * _moveSpeed;
                        TryMove(nx, ny);
                        break;
                    }
                case Keys.Left:
                case Keys.A:
                    {
                        _playerAngle -= _rotSpeed;
                        break;
                    }
                case Keys.B:
                    {
                        _useBilinear = !_useBilinear;
                        break;
                    }
                case Keys.Right:
                case Keys.D:
                    {
                        _playerAngle += _rotSpeed;
                        break;
                    }
            }
        }

        private void DrawMiniMap(Graphics g, List<Tuple<PointF, double>> hits)
        {
            int mapW = _map.GetLength(1);
            int mapH = _map.GetLength(0);
            int size = _miniMapSize;
            int pad = 0;
            int left = pad;
            int top = pad;

            float scaleX = (size - pad * 2f) / mapW;
            float scaleY = (size - pad * 2f) / mapH;
            float scale = Math.Min(scaleX, scaleY);

            using (Brush bg = new SolidBrush(Color.FromArgb(160, 0, 0, 0)))
            using (Pen border = new Pen(Color.White))
            {
                g.FillRectangle(bg, left - pad / 2, top - pad / 2, size, size);
                g.DrawRectangle(border, left - pad / 2, top - pad / 2, size, size);
            }

            for (int my = 0; my < mapH; my++)
            {
                for (int mx = 0; mx < mapW; mx++)
                {
                    int cell = _map[my, mx];
                    RectangleF r = new RectangleF(left + mx * scale, top + my * scale, scale, scale);
                    if (cell == 0)
                    {
                        using (Brush floor = new SolidBrush(Color.FromArgb(80, Color.Gray)))
                        {
                            g.FillRectangle(floor, r);
                        }
                    }
                    else
                    {
                        Color c;
                        // prefer a representative color from the texture if available
                        if (_texturePixels != null && cell > 0 && cell < _texturePixels.Length && _textureWidths != null)
                        {
                            c = GetTextureRepresentativeColor(cell);
                        }
                        else
                        {
                            c = _palette.Length > cell ? _palette[cell] : Color.Magenta;
                        }
                        using (Brush b = new SolidBrush(c))
                        {
                            g.FillRectangle(b, r);
                        }
                    }

                    using (Pen p = new Pen(Color.FromArgb(40, Color.Black)))
                    {
                        g.DrawRectangle(p, r.X, r.Y, r.Width, r.Height);
                    }
                }
            }

            float px = left + (float)_playerX * scale;
            float py = top + (float)_playerY * scale;
            float pr = Math.Max(2.5f, scale * 0.25f);
            using (Brush pb = new SolidBrush(Color.Red))
            {
                g.FillEllipse(pb, px - pr, py - pr, pr * 2, pr * 2);
            }

            // draw individual ray hits (scaled to minimap) only up to their hit distance
            if (hits != null)
            {
                using (Pen hitPen = new Pen(Color.FromArgb(200, Color.Cyan), 1))
                using (Brush hitBrush = new SolidBrush(Color.Cyan))
                {
                    foreach (var h in hits)
                    {
                        var pt = h.Item1;
                        float hx = left + pt.X * scale;
                        float hy = top + pt.Y * scale;

                        // draw line from player to hit point
                        g.DrawLine(hitPen, px, py, hx, hy);
                        g.FillEllipse(hitBrush, hx - 2f, hy - 2f, 4f, 4f);
                    }
                }
            }
        }

        private void TryMove(double nx, double ny)
        {
            int mapW = _map.GetLength(1);
            int mapH = _map.GetLength(0);
            int mx = (int)nx;
            int my = (int)ny;
            if (mx >= 0 && my >= 0 && mx < mapW && my < mapH && _map[my, mx] == 0)
            {
                _playerX = nx;
                _playerY = ny;
            }
        }
    }

}
