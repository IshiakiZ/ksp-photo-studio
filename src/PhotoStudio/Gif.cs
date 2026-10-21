using System;
using System.Collections.Generic;
using System.IO;

namespace PhotoStudio
{
    /// <summary>
    /// (Since 0.2.0.) A looping GIF written from pictures of the same size, for the turntable's loop (see Studio.Loop).
    ///
    /// GIF holds 256 colours. One palette for every frame, chosen from all of them (the median cut: the colours there are,
    /// split again and again where they spread widest, each part's mean kept), so that nothing changes colour from frame to
    /// frame; each pixel put to the nearest of them through a table of 32 by 32 by 32, with a fixed 4 by 4 pattern of small
    /// offsets added first (ordered dithering): a smooth backdrop's gradient keeps no bands, and the pattern stands still from
    /// frame to frame where error diffusion would crawl. Each frame compressed as the format has it (LZW, codes of 9 to 12
    /// bits). Plain arrays and arithmetic only: it runs on another thread.
    /// </summary>
    static class Gif
    {
        static readonly int[] Bayer = { 0, 8, 2, 10, 12, 4, 14, 6, 3, 11, 1, 9, 15, 7, 13, 5 };

        /// <summary>Write the frames (each width by height, rgb bytes, rows from the bottom up as Unity reads them) as a GIF that loops, so many hundredths of a second a frame.</summary>
        public static void Write(string path, List<byte[]> frames, int width, int height, int delay)
        {
            byte[] palette = Palette(frames, width * height);
            byte[] nearest = Nearest(palette);
            using (var file = new FileStream(path, FileMode.Create, FileAccess.Write))
            using (var o = new BinaryWriter(file))
            {
                o.Write(new[] { (byte)'G', (byte)'I', (byte)'F', (byte)'8', (byte)'9', (byte)'a' });
                o.Write((ushort)width); o.Write((ushort)height);
                o.Write((byte)0xF7); o.Write((byte)0); o.Write((byte)0);
                o.Write(palette);
                // (loops for ever)
                o.Write(new byte[] { 0x21, 0xFF, 0x0B });
                o.Write(new[] { (byte)'N', (byte)'E', (byte)'T', (byte)'S', (byte)'C', (byte)'A', (byte)'P', (byte)'E', (byte)'2', (byte)'.', (byte)'0' });
                o.Write(new byte[] { 0x03, 0x01, 0x00, 0x00, 0x00 });
                var indices = new byte[width * height];
                foreach (byte[] rgb in frames)
                {
                    // (rows turned the right way up: GIF begins at the top)
                    for (int y = 0; y < height; y++)
                    {
                        int from = (height - 1 - y) * width * 3, to = y * width;
                        for (int x = 0; x < width; x++)
                        {
                            int k = Bayer[(y & 3) * 4 + (x & 3)] - 8;               // (-8 to 7: about a cell of the table, spread over sixteen steps)
                            int r = Clamp(rgb[from + 3 * x] + k), g = Clamp(rgb[from + 3 * x + 1] + k), b = Clamp(rgb[from + 3 * x + 2] + k);
                            indices[to + x] = nearest[((r >> 3) << 10) | ((g >> 3) << 5) | (b >> 3)];
                        }
                    }
                    o.Write(new byte[] { 0x21, 0xF9, 0x04, 0x04 }); o.Write((ushort)delay); o.Write((byte)0); o.Write((byte)0);
                    o.Write((byte)0x2C); o.Write((ushort)0); o.Write((ushort)0); o.Write((ushort)width); o.Write((ushort)height); o.Write((byte)0);
                    o.Write((byte)8);
                    Compress(indices, o);
                    o.Write((byte)0);
                }
                o.Write((byte)0x3B);
            }
        }

        static int Clamp(int v) => v < 0 ? 0 : v > 255 ? 255 : v;

        /// <summary>256 colours for all the frames, by the median cut over a sample of their pixels.</summary>
        static byte[] Palette(List<byte[]> frames, int pixels)
        {
            int every = Math.Max(1, frames.Count * pixels / 240000);
            var samples = new List<int>(240000 + 16);
            long seen = 0;
            foreach (byte[] rgb in frames)
                for (int p = 0; p < pixels; p++, seen++)
                    if (seen % every == 0) samples.Add((rgb[3 * p] << 16) | (rgb[3 * p + 1] << 8) | rgb[3 * p + 2]);
            int[] c = samples.ToArray();
            // (each box: where its colours lie in the sample, the one of red, green and blue they spread widest along, and how wide)
            var boxes = new List<int[]> { Box(c, 0, c.Length) };
            while (boxes.Count < 256)
            {
                int best = -1, bestSpread = 0;
                for (int i = 0; i < boxes.Count; i++) if (boxes[i][1] - boxes[i][0] >= 2 && boxes[i][3] > bestSpread) { bestSpread = boxes[i][3]; best = i; }
                if (best < 0) break;
                int bs = boxes[best][0], be = boxes[best][1], sh = 16 - 8 * boxes[best][2], count = be - bs;
                var keys = new int[count]; var part = new int[count];
                for (int n = 0; n < count; n++) { part[n] = c[bs + n]; keys[n] = (part[n] >> sh) & 255; }
                Array.Sort(keys, part);
                Array.Copy(part, 0, c, bs, count);
                int mid = bs + count / 2;
                boxes[best] = Box(c, bs, mid);
                boxes.Add(Box(c, mid, be));
            }
            var palette = new byte[256 * 3];
            for (int i = 0; i < boxes.Count; i++)
            {
                long r = 0, g = 0, b = 0;
                int s = boxes[i][0], e = boxes[i][1], count = Math.Max(1, e - s);
                for (int n = s; n < e; n++) { r += (c[n] >> 16) & 255; g += (c[n] >> 8) & 255; b += c[n] & 255; }
                palette[3 * i] = (byte)(r / count); palette[3 * i + 1] = (byte)(g / count); palette[3 * i + 2] = (byte)(b / count);
            }
            return palette;
        }

        /// <summary>A box of the sample's colours from s to e: { s, e, the axis they spread widest along (0 red, 1 green, 2 blue), that spread }.</summary>
        static int[] Box(int[] c, int s, int e)
        {
            int axisBest = 0, spreadBest = -1;
            for (int axis = 0; axis < 3; axis++)
            {
                int shift = 16 - 8 * axis, lo = 255, hi = 0;
                for (int n = s; n < e; n++) { int v = (c[n] >> shift) & 255; if (v < lo) lo = v; if (v > hi) hi = v; }
                if (hi - lo > spreadBest) { spreadBest = hi - lo; axisBest = axis; }
            }
            return new[] { s, e, axisBest, Math.Max(spreadBest, 0) };
        }

        /// <summary>For every cell of a 32 by 32 by 32 table of colours, the nearest of the palette's.</summary>
        static byte[] Nearest(byte[] palette)
        {
            var table = new byte[32 * 32 * 32];
            for (int r = 0; r < 32; r++)
                for (int g = 0; g < 32; g++)
                    for (int b = 0; b < 32; b++)
                    {
                        int R = r * 8 + 4, G = g * 8 + 4, B = b * 8 + 4, best = 0, bestD = int.MaxValue;
                        for (int i = 0; i < 256; i++)
                        {
                            int dr = R - palette[3 * i], dg = G - palette[3 * i + 1], db = B - palette[3 * i + 2];
                            int d = 2 * dr * dr + 4 * dg * dg + 3 * db * db;
                            if (d < bestD) { bestD = d; best = i; }
                        }
                        table[(r << 10) | (g << 5) | b] = (byte)best;
                    }
            return table;
        }

        /// <summary>One frame's indices compressed as GIF's LZW has it (codes from 9 bits, a fresh table at 4096), in blocks of up to 255 bytes.</summary>
        static void Compress(byte[] data, BinaryWriter o)
        {
            const int Clear = 256, End = 257, Max = 4096;
            var codes = new Dictionary<int, int>(Max);
            int next = 258, size = 9;
            var block = new byte[255];
            int filled = 0, bits = 0, held = 0;
            void Put(int code)
            {
                held |= code << bits; bits += size;
                while (bits >= 8)
                {
                    block[filled++] = (byte)(held & 255); held >>= 8; bits -= 8;
                    if (filled == 255) { o.Write((byte)255); o.Write(block, 0, 255); filled = 0; }
                }
            }
            Put(Clear);
            int prefix = data.Length > 0 ? data[0] : 0;
            for (int i = 1; i < data.Length; i++)
            {
                int key = (prefix << 8) | data[i];
                if (codes.TryGetValue(key, out int code)) { prefix = code; continue; }
                Put(prefix);
                if (next < Max)
                {
                    codes[key] = next++;
                    if (next > (1 << size) && size < 12) size++;
                }
                else
                {
                    Put(Clear);
                    codes.Clear(); next = 258; size = 9;
                }
                prefix = data[i];
            }
            Put(prefix);
            Put(End);
            if (bits > 0) { block[filled++] = (byte)(held & 255); if (filled == 255) { o.Write((byte)255); o.Write(block, 0, 255); filled = 0; } }
            if (filled > 0) { o.Write((byte)filled); o.Write(block, 0, filled); }
        }
    }
}
