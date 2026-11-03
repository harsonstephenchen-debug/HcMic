// 生成 HcMic 图标：麦克风矢量图形（256/64/32/16 多尺寸 ICO）
// 用 System.Drawing 逐尺寸绘制：渐变底 + 白色麦克风
using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;

class IconGen
{
    static void Main()
    {
        string outPath = @"E:\Software\WO-Mic-Clone\pc\installer\icon.ico";
        int[] sizes = { 256, 64, 48, 32, 16 };
        using var msList = new MemoryStream();
        var pngs = new MemoryStream[sizes.Length];
        for (int i = 0; i < sizes.Length; i++)
        {
            int s = sizes[i];
            using var bmp = new Bitmap(s, s);
            using (var g = Graphics.FromImage(bmp))
            {
                g.SmoothingMode = SmoothingMode.AntiAlias;
                // 圆角方形背景（紫色渐变）
                using var path = RoundRect(0, 0, s, s, s * 0.18f);
                using var brush = new LinearGradientBrush(new Rectangle(0, 0, s, s),
                    Color.FromArgb(99, 102, 241), Color.FromArgb(67, 56, 202), 90f);
                g.FillPath(brush, path);

                float u = s / 64f; // 单位
                using var white = new SolidBrush(Color.White);
                // 麦克风胶囊
                using var caps = RoundRect(24 * u, 10 * u, 16 * u, 26 * u, 8 * u);
                g.FillPath(white, caps);
                // 支架弧线
                using var pen = new Pen(Color.White, 3.2f * u) { StartCap = LineCap.Round, EndCap = LineCap.Round };
                g.DrawArc(pen, 17 * u, 18 * u, 30 * u, 30 * u, -20, 220);
                // 支杆
                g.DrawLine(pen, 32 * u, 48 * u, 32 * u, 56 * u);
                // 底座
                g.DrawLine(pen, 22 * u, 56 * u, 42 * u, 56 * u);
            }
            var ms = new MemoryStream();
            bmp.Save(ms, System.Drawing.Imaging.ImageFormat.Png);
            pngs[i] = ms;
        }

        // 写 ICO 容器（PNG 压缩条目）
        using var fs = new FileStream(outPath, FileMode.Create);
        using var bw = new BinaryWriter(fs);
        bw.Write((ushort)0); bw.Write((ushort)1); bw.Write((ushort)sizes.Length);
        uint offset = 6 + (uint)(16 * sizes.Length);
        for (int i = 0; i < sizes.Length; i++)
        {
            int s = sizes[i];
            byte w = s >= 256 ? (byte)0 : (byte)s;
            bw.Write(w); bw.Write(w); bw.Write((byte)0); bw.Write((byte)0);
            bw.Write((ushort)1); bw.Write((ushort)32);
            bw.Write((uint)pngs[i].Length); bw.Write(offset);
            offset += (uint)pngs[i].Length;
        }
        foreach (var ms in pngs) bw.Write(ms.ToArray());
        bw.Flush();
        Console.WriteLine("icon written: " + outPath);
    }

    static GraphicsPath RoundRect(float x, float y, float w, float h, float r)
    {
        var p = new GraphicsPath();
        float d = r * 2;
        p.AddArc(x, y, d, d, 180, 90);
        p.AddArc(x + w - d, y, d, d, 270, 90);
        p.AddArc(x + w - d, y + h - d, d, d, 0, 90);
        p.AddArc(x, y + h - d, d, d, 90, 90);
        p.CloseFigure();
        return p;
    }
}
