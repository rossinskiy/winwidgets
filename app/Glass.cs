using System.Runtime.CompilerServices;

namespace WinWidgets;

/// <summary>
/// CPU liquid-glass renderer. Takes the desktop pixels behind a window and produces
/// a premultiplied BGRA image: frosted interior, lens refraction with chromatic split
/// near the rim, accent tint, specular edge light.
/// </summary>
static class Glass
{
    // scratch buffers are shared by all windows (rendering is sequential on the UI thread)
    static byte[] tmp = [], strong = [], light = [];

    /// <summary>Blur the captured pixels into a frosted and a lightly softened layer.</summary>
    public static void Prepare(byte[] src, int w, int h, float scale, byte[] strongOut, byte[] lightOut, byte[] scratch)
    {
        Blur(src, strongOut, scratch, w, h, Math.Max(2, (int)(9 * scale)), 2);
        Blur(src, lightOut, scratch, w, h, Math.Max(1, (int)(1.5f * scale)), 1);
    }

    public static void Render(byte[] src, int w, int h, float scale, byte[] dst)
    {
        int n = w * h * 4;
        if (tmp.Length < n) { tmp = new byte[n]; strong = new byte[n]; light = new byte[n]; }
        Prepare(src, w, h, scale, strong, light, tmp);
        Draw(strong, light, w, h, 0, 0, w, h, scale, dst);
    }

    public static void ReleaseScratch() { tmp = []; strong = []; light = []; }

    /// <summary>Draws a w*h glass pane whose backdrop is the (ox, oy) window of the prepared bw*bh layers.</summary>
    public static void Draw(byte[] strong, byte[] light, int bw, int bh, int offX, int offY, int w, int h, float scale, byte[] dst)
    {
        float radius = 20 * scale, bevel = 30 * scale, strength = 36 * scale;
        float hw = w / 2f, hh = h / 2f, ix = hw - radius, iy = hh - radius;
        bool lightTheme = Theme.Light;
        float baseK = lightTheme ? 0.30f : 0.22f;
        float baseR = lightTheme ? 255 : 14, baseG = lightTheme ? 255 : 16, baseB = lightTheme ? 255 : 24;
        float ar = Theme.AR, ag = Theme.AG, ab = Theme.AB, tint = Theme.Tint;
        const float lx = -0.6f, ly = -0.8f;
        Span<float> chrom = [1.0f, 1.05f, 1.11f]; // B, G, R

        for (int y = 0; y < h; y++)
        {
            for (int x = 0; x < w; x++)
            {
                int o = (y * w + x) * 4;
                float px = x + 0.5f - hw, py = y + 0.5f - hh;
                float ax = MathF.Abs(px) - ix, ay = MathF.Abs(py) - iy;
                float ox = MathF.Max(ax, 0), oy = MathF.Max(ay, 0);
                float len = MathF.Sqrt(ox * ox + oy * oy);
                float depth = -(len + MathF.Min(MathF.Max(ax, ay), 0) - radius);
                if (depth <= -1f) { dst[o] = dst[o + 1] = dst[o + 2] = dst[o + 3] = 0; continue; }
                float cov = Math.Clamp(depth + 0.5f, 0, 1);

                float nx, ny;
                if (ax > 0 && ay > 0) { nx = ox / len; ny = oy / len; }
                else if (ax > ay) { nx = 1; ny = 0; }
                else { nx = 0; ny = 1; }
                if (px < 0) nx = -nx;
                if (py < 0) ny = -ny;

                float t = depth >= bevel ? 0 : 1 - depth / bevel;
                float c0, c1, c2;
                if (t <= 0.01f)
                {
                    int bo = ((y + offY) * bw + x + offX) * 4;
                    c0 = strong[bo]; c1 = strong[bo + 1]; c2 = strong[bo + 2];
                }
                else
                {
                    float mag = strength * (1 - MathF.Sqrt(MathF.Max(0, 1 - t * t)));
                    float f = t * t * 0.85f;
                    Span<float> res = stackalloc float[3];
                    for (int c = 0; c < 3; c++)
                    {
                        float m = mag * chrom[c];
                        float sx = x + 0.5f - nx * m, sy = y + 0.5f - ny * m;
                        float a = Bilinear(strong, bw, bh, sx + offX, sy + offY, c);
                        float b = Bilinear(light, bw, bh, sx + offX, sy + offY, c);
                        res[c] = a + (b - a) * f;
                    }
                    c0 = res[0]; c1 = res[1]; c2 = res[2];
                }

                // base shade keeps text readable on busy wallpapers
                c0 += (baseB - c0) * baseK; c1 += (baseG - c1) * baseK; c2 += (baseR - c2) * baseK;

                // accent tint, stronger toward the bottom right
                float diag = (x / (float)w + y / (float)h) * 0.5f;
                float ta = Math.Clamp(tint * (0.65f + 0.7f * diag) * 1.25f, 0, 0.9f);
                c0 += (ab - c0) * ta; c1 += (ag - c1) * ta; c2 += (ar - c2) * ta;

                // specular rim light from the top left, soft counter light, thin edge line, top sheen
                float dl = nx * lx + ny * ly;
                float rim = t * t * t;
                float spec = rim * (MathF.Max(0, dl) * 0.95f + MathF.Max(0, -dl) * 0.38f);
                float ring = depth < 2f ? (1 - depth / 2f) * (0.2f + 0.5f * MathF.Max(0, dl)) : 0;
                float sheen = y < h * 0.5f ? (1 - y / (h * 0.5f)) * 0.09f : 0;
                float hi = Math.Clamp(spec * 0.9f + ring + sheen, 0, 1);
                c0 += (255 - c0) * hi; c1 += (255 - c1) * hi; c2 += (255 - c2) * hi;

                dst[o] = (byte)(Math.Clamp(c0, 0, 255) * cov);
                dst[o + 1] = (byte)(Math.Clamp(c1, 0, 255) * cov);
                dst[o + 2] = (byte)(Math.Clamp(c2, 0, 255) * cov);
                dst[o + 3] = (byte)(255 * cov);
            }
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    static float Bilinear(byte[] b, int w, int h, float fx, float fy, int c)
    {
        fx -= 0.5f; fy -= 0.5f;
        int x0 = (int)MathF.Floor(fx), y0 = (int)MathF.Floor(fy);
        float tx = fx - x0, ty = fy - y0;
        int x1 = Math.Clamp(x0 + 1, 0, w - 1), y1 = Math.Clamp(y0 + 1, 0, h - 1);
        x0 = Math.Clamp(x0, 0, w - 1); y0 = Math.Clamp(y0, 0, h - 1);
        float a = b[(y0 * w + x0) * 4 + c], bb = b[(y0 * w + x1) * 4 + c];
        float d = b[(y1 * w + x0) * 4 + c], e = b[(y1 * w + x1) * 4 + c];
        float top = a + (bb - a) * tx, bot = d + (e - d) * tx;
        return top + (bot - top) * ty;
    }

    static void Blur(byte[] src, byte[] dst, byte[] scratch, int w, int h, int r, int iterations)
    {
        Buffer.BlockCopy(src, 0, dst, 0, w * h * 4);
        for (int i = 0; i < iterations; i++)
        {
            BoxH(dst, scratch, w, h, r);
            BoxV(scratch, dst, w, h, r);
        }
    }

    static void BoxH(byte[] s, byte[] d, int w, int h, int r)
    {
        int win = 2 * r + 1;
        for (int y = 0; y < h; y++)
        {
            int row = y * w * 4;
            for (int c = 0; c < 3; c++)
            {
                int sum = 0;
                for (int i = -r; i <= r; i++) sum += s[row + Math.Clamp(i, 0, w - 1) * 4 + c];
                for (int x = 0; x < w; x++)
                {
                    d[row + x * 4 + c] = (byte)(sum / win);
                    sum += s[row + Math.Min(x + r + 1, w - 1) * 4 + c] - s[row + Math.Max(x - r, 0) * 4 + c];
                }
            }
        }
    }

    static void BoxV(byte[] s, byte[] d, int w, int h, int r)
    {
        int win = 2 * r + 1, stride = w * 4;
        for (int x = 0; x < w; x++)
        {
            for (int c = 0; c < 3; c++)
            {
                int col = x * 4 + c, sum = 0;
                for (int i = -r; i <= r; i++) sum += s[Math.Clamp(i, 0, h - 1) * stride + col];
                for (int y = 0; y < h; y++)
                {
                    d[y * stride + col] = (byte)(sum / win);
                    sum += s[Math.Min(y + r + 1, h - 1) * stride + col] - s[Math.Max(y - r, 0) * stride + col];
                }
            }
        }
    }
}
