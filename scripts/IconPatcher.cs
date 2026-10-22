// IconPatcher.cs - binary/icon engine for the Turbo icon patcher (portable edition).
// Compiled at runtime by PowerShell Add-Type using the inbox .NET Framework 4 compiler.
// Language level: C# 5 (no string interpolation, no null-conditional).
// Ported from scripts/patch_icon.py (leoshone/turbo-icon-patcher).
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using System.IO.Compression;
using System.Runtime.InteropServices;
using System.Text;

namespace TurboIcon
{
    public static class IconPatcher
    {
        // ---------------- Win32 interop ----------------
        [DllImport("kernel32", CharSet = CharSet.Unicode, SetLastError = true)]
        static extern IntPtr LoadLibraryExW(string fn, IntPtr h, uint flags);
        [DllImport("kernel32", SetLastError = true)]
        static extern bool FreeLibrary(IntPtr h);
        // FindResourceW overloads: by resource NAME (string) and by resource ID (MAKEINTRESOURCE)
        [DllImport("kernel32", CharSet = CharSet.Unicode, SetLastError = true)]
        static extern IntPtr FindResourceW(IntPtr m, string name, IntPtr type);
        [DllImport("kernel32", CharSet = CharSet.Unicode, SetLastError = true)]
        static extern IntPtr FindResourceW(IntPtr m, IntPtr name, IntPtr type);
        [DllImport("kernel32", SetLastError = true)]
        static extern IntPtr LoadResource(IntPtr m, IntPtr res);
        [DllImport("kernel32", SetLastError = true)]
        static extern IntPtr LockResource(IntPtr res);
        [DllImport("kernel32", SetLastError = true)]
        static extern uint SizeofResource(IntPtr m, IntPtr res);
        [DllImport("kernel32", CharSet = CharSet.Unicode, SetLastError = true)]
        static extern IntPtr BeginUpdateResourceW(string fn, bool deleteExisting);
        [DllImport("kernel32", CharSet = CharSet.Unicode, SetLastError = true)]
        static extern bool UpdateResourceW(IntPtr h, IntPtr type, IntPtr name, ushort lang, byte[] data, uint len);
        [DllImport("kernel32", CharSet = CharSet.Unicode, SetLastError = true)]
        static extern bool EndUpdateResourceW(IntPtr h, bool discard);
        delegate bool EnumLangProc(IntPtr h, IntPtr type, IntPtr name, ushort lang, IntPtr param);
        [DllImport("kernel32", CharSet = CharSet.Unicode, SetLastError = true)]
        static extern bool EnumResourceLanguagesW(IntPtr m, IntPtr type, IntPtr name, EnumLangProc cb, IntPtr param);
        [DllImport("user32", CharSet = CharSet.Unicode)]
        static extern bool EnumWindows(EnumWindowsProc cb, IntPtr param);
        delegate bool EnumWindowsProc(IntPtr hwnd, IntPtr param);
        [DllImport("user32", CharSet = CharSet.Unicode)]
        static extern int GetWindowTextW(IntPtr hwnd, StringBuilder sb, int max);
        [DllImport("user32")]
        static extern bool IsWindowVisible(IntPtr hwnd);
        [DllImport("user32", EntryPoint = "GetClassLongPtrW")]
        static extern IntPtr GetClassLongPtrW(IntPtr hwnd, int index);
        [DllImport("user32")]
        static extern bool GetIconInfo(IntPtr hicon, ref ICONINFO info);
        [DllImport("user32")]
        static extern bool DestroyIcon(IntPtr hicon);
        [DllImport("gdi32")]
        static extern bool DeleteObject(IntPtr obj);
        struct ICONINFO { public int fIcon; public int xHotspot; public int yHotspot; public IntPtr hbmMask; public IntPtr hbmColor; }

        const uint LOAD_DATAFILE = 0x1 | 0x2; // DONT_RESOLVE_DLL_REFERENCES | LOAD_LIBRARY_AS_DATAFILE
        const int RT_ICON = 3;
        const int RT_GROUP_ICON = 14;

        // ---------------- resource reading ----------------

        // Raw resource bytes by id, from a data-file loaded module.
        static byte[] GetResById(IntPtr m, int id, int type)
        {
            IntPtr res = FindResourceW(m, (IntPtr)id, (IntPtr)type);
            if (res == IntPtr.Zero) return null;
            int size = (int)SizeofResource(m, res);
            IntPtr p = LockResource(LoadResource(m, res));
            if (p == IntPtr.Zero) return null;
            byte[] b = new byte[size];
            Marshal.Copy(p, b, 0, size);
            return b;
        }

        // First language id of a named/id resource (python used the first lang entry too).
        static ushort FirstLang(IntPtr m, IntPtr type, IntPtr name)
        {
            ushort lang = 0;
            EnumLangProc cb = delegate(IntPtr h, IntPtr t, IntPtr n, ushort l, IntPtr p)
            { lang = l; return false; };
            EnumResourceLanguagesW(m, type, name, cb, IntPtr.Zero);
            return lang;
        }

        // Ids referenced by the RT_GROUP_ICON group named IDR_MAINFRAME (main app icon).
        // Since 154.0.8037.101 the exe also carries APP_LIST/INCOGNITO/HTML_DOC/PDF_DOC/
        // ISOLATED groups - those frames must NOT be touched (parity with mainframe_icon_ids()).
        public static int[] GetMainFrameIds(string exe)
        {
            IntPtr m = LoadLibraryExW(exe, IntPtr.Zero, LOAD_DATAFILE);
            if (m == IntPtr.Zero) throw new Exception("LoadLibraryEx failed err=" + Marshal.GetLastWin32Error());
            try
            {
                IntPtr grp = FindResourceW(m, "IDR_MAINFRAME", (IntPtr)RT_GROUP_ICON);
                if (grp == IntPtr.Zero) return new int[0];
                byte[] d = GetResById(m, 0, 0); // not used; read via handles below
                IntPtr loaded = LoadResource(m, grp);
                IntPtr p = LockResource(loaded);
                int size = (int)SizeofResource(m, grp);
                d = new byte[size];
                Marshal.Copy(p, d, 0, size);
                int cnt = BitConverter.ToUInt16(d, 4);
                var ids = new List<int>();
                for (int i = 0; i < cnt; i++)
                    ids.Add(BitConverter.ToUInt16(d, 6 + 14 * i + 12)); // GRPICONDIRENTRY wResId
                return ids.ToArray();
            }
            finally { FreeLibrary(m); }
        }

        // Raw RT_ICON frames for the given ids (parity with collect_frames()).
        public static Dictionary<int, byte[]> CollectFrames(string exe, int[] ids)
        {
            IntPtr m = LoadLibraryExW(exe, IntPtr.Zero, LOAD_DATAFILE);
            if (m == IntPtr.Zero) throw new Exception("LoadLibraryEx failed err=" + Marshal.GetLastWin32Error());
            try
            {
                var dict = new Dictionary<int, byte[]>();
                foreach (int id in ids)
                {
                    byte[] b = GetResById(m, id, RT_ICON);
                    if (b != null) dict[id] = b;
                }
                return dict;
            }
            finally { FreeLibrary(m); }
        }

        public static byte[] GetFrame(string exe, int id)
        {
            var d = CollectFrames(exe, new int[] { id });
            byte[] b;
            return d.TryGetValue(id, out b) ? b : null;
        }

        // Replace RT_ICON frames in place (parity with patch_exe/UpdateResource path).
        public static void UpdateExeIcons(string exe, int[] ids, byte[][] frames)
        {
            // resolve per-id language from the current exe first
            IntPtr m = LoadLibraryExW(exe, IntPtr.Zero, LOAD_DATAFILE);
            if (m == IntPtr.Zero) throw new Exception("LoadLibraryEx failed err=" + Marshal.GetLastWin32Error());
            ushort[] langs = new ushort[ids.Length];
            try
            {
                for (int i = 0; i < ids.Length; i++)
                    langs[i] = FirstLang(m, (IntPtr)RT_ICON, (IntPtr)ids[i]);
            }
            finally { FreeLibrary(m); }

            IntPtr u = BeginUpdateResourceW(exe, false);
            if (u == IntPtr.Zero)
                throw new Exception("BeginUpdateResource failed (process still running?) err=" + Marshal.GetLastWin32Error());
            for (int i = 0; i < ids.Length; i++)
            {
                if (!UpdateResourceW(u, (IntPtr)RT_ICON, (IntPtr)ids[i], langs[i], frames[i], (uint)frames[i].Length))
                {
                    EndUpdateResourceW(u, true); // discard
                    throw new Exception("UpdateResource id=" + ids[i] + " failed err=" + Marshal.GetLastWin32Error());
                }
            }
            if (!EndUpdateResourceW(u, false))
                throw new Exception("EndUpdateResource commit failed err=" + Marshal.GetLastWin32Error());
            Console.WriteLine("  [exe] updated " + ids.Length + " RT_ICON frames");
        }

        // ---------------- PNG / CRC / Adler helpers ----------------

        static uint[] _crcTable;
        static uint Crc32(byte[] d, int off, int len)
        {
            if (_crcTable == null)
            {
                _crcTable = new uint[256];
                for (uint n = 0; n < 256; n++)
                {
                    uint c = n;
                    for (int k = 0; k < 8; k++) c = ((c & 1) == 1) ? 0xEDB88320 ^ (c >> 1) : c >> 1;
                    _crcTable[n] = c;
                }
            }
            uint crc = 0xFFFFFFFF;
            for (int i = 0; i < len; i++) crc = _crcTable[(crc ^ d[off + i]) & 0xFF] ^ (crc >> 8);
            return crc ^ 0xFFFFFFFF;
        }

        static uint Adler32(byte[] d, int off, int len)
        {
            uint a = 1, b = 0;
            for (int i = 0; i < len; i++) { a = (a + d[off + i]) % 65521; b = (b + a) % 65521; }
            return (b << 16) | a;
        }

        static byte[] BE32(uint v) { return new byte[] { (byte)(v >> 24), (byte)(v >> 16), (byte)(v >> 8), (byte)v }; }

        static byte[] Chunk(string type, byte[] data)
        {
            byte[] t = Encoding.ASCII.GetBytes(type);
            byte[] head = BE32((uint)data.Length);
            byte[] outp = new byte[12 + data.Length];
            Array.Copy(head, 0, outp, 0, 4);
            Array.Copy(t, 0, outp, 4, 4);
            Array.Copy(data, 0, outp, 8, data.Length);
            byte[] crcSrc = new byte[4 + data.Length];
            Array.Copy(t, 0, crcSrc, 0, 4);
            Array.Copy(data, 0, crcSrc, 4, data.Length);
            uint crc = Crc32(crcSrc, 0, crcSrc.Length);
            Array.Copy(BE32(crc), 0, outp, 8 + data.Length, 4);
            return outp;
        }

        static int LastIndexOf(byte[] hay, byte[] needle)
        {
            for (int i = hay.Length - needle.Length; i >= 0; i--)
            {
                bool ok = true;
                for (int j = 0; j < needle.Length; j++)
                    if (hay[i + j] != needle[j]) { ok = false; break; }
                if (ok) return i;
            }
            return -1;
        }

        public static int[] PngSize(byte[] png)
        {
            using (var ms = new MemoryStream(png))
            using (var b = new Bitmap(ms))
                return new int[] { b.Width, b.Height };
        }

        // LANCZOS is not available in GDI+; HighQualityBicubic is the closest quality preset.
        public static byte[] ResizePng(byte[] masterPng, int w, int h)
        {
            using (var ms = new MemoryStream(masterPng))
            using (var src = new Bitmap(ms))
            using (var dst = new Bitmap(w, h, PixelFormat.Format32bppArgb))
            using (var g = Graphics.FromImage(dst))
            {
                g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                g.SmoothingMode = SmoothingMode.HighQuality;
                g.PixelOffsetMode = PixelOffsetMode.HighQuality;
                g.DrawImage(src, new Rectangle(0, 0, w, h));
                using (var o = new MemoryStream()) { dst.Save(o, ImageFormat.Png); return o.ToArray(); }
            }
        }

        // Rebuild a PNG from raw pixels with filter=0 (parity with manual() in python).
        static byte[] ManualRebuild(byte[] masterPng)
        {
            using (var ms = new MemoryStream(masterPng))
            using (var bmp = new Bitmap(ms))
            {
                int w = bmp.Width, h = bmp.Height, stride = w * 4;
                byte[] raw = new byte[h * (1 + stride)];
                var bd = bmp.LockBits(new Rectangle(0, 0, w, h), ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
                byte[] row = new byte[stride];
                try
                {
                    for (int y = 0; y < h; y++)
                    {
                        Marshal.Copy(bd.Scan0 + y * bd.Stride, row, 0, stride);
                        int o = y * (1 + stride);
                        raw[o] = 0; // filter none
                        for (int x = 0; x < w; x++)
                        { // BGRA -> RGBA
                            raw[o + 1 + x * 4] = row[x * 4 + 2];
                            raw[o + 2 + x * 4] = row[x * 4 + 1];
                            raw[o + 3 + x * 4] = row[x * 4];
                            raw[o + 4 + x * 4] = row[x * 4 + 3];
                        }
                    }
                }
                finally { bmp.UnlockBits(bd); }
                byte[] z;
                using (var src = new MemoryStream())
                {
                    using (var dz = new DeflateStream(src, CompressionLevel.Optimal, true)) dz.Write(raw, 0, raw.Length);
                    z = src.ToArray();
                }
                byte[] zlib = new byte[2 + z.Length + 4];
                zlib[0] = 0x78; zlib[1] = 0x9C; // zlib header
                Array.Copy(z, 0, zlib, 2, z.Length);
                Array.Copy(BE32(Adler32(raw, 0, raw.Length)), 0, zlib, 2 + z.Length, 4);
                byte[] sig = new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A };
                byte[] ihdr = new byte[13];
                Array.Copy(BE32((uint)w), 0, ihdr, 0, 4);
                Array.Copy(BE32((uint)h), 0, ihdr, 4, 4);
                ihdr[8] = 8; ihdr[9] = 6; // 8-bit RGBA
                byte[] outp = new byte[sig.Length + 25 + (12 + zlib.Length) + 12];
                int p = 0;
                Array.Copy(sig, 0, outp, p, sig.Length); p += sig.Length;
                byte[] c = Chunk("IHDR", ihdr); Array.Copy(c, 0, outp, p, c.Length); p += c.Length;
                c = Chunk("IDAT", zlib); Array.Copy(c, 0, outp, p, c.Length); p += c.Length;
                c = Chunk("IEND", new byte[0]); Array.Copy(c, 0, outp, p, c.Length); p += c.Length;
                Array.Resize(ref outp, p);
                return outp;
            }
        }

        // Pad a PNG with a legal tEXt chunk so total length == target (parity with text_pad()).
        static byte[] TextPad(byte[] png, int target)
        {
            if (png.Length > target - 12) return null;
            int payloadLen = target - png.Length - 12;
            byte[] payload = new byte[payloadLen];
            payload[0] = (byte)'x'; payload[1] = 0;
            for (int i = 2; i < payloadLen; i++) payload[i] = 0x20;
            byte[] tchunk = Chunk("tEXt", payload);
            byte[] iendTag = new byte[] { 0x49, 0x45, 0x4E, 0x44 }; // "IEND"
            int iend = LastIndexOf(png, iendTag) - 4; // include IEND length field
            byte[] outp = new byte[target];
            Array.Copy(png, 0, outp, 0, iend);
            Array.Copy(tchunk, 0, outp, iend, tchunk.Length);
            Array.Copy(png, iend, outp, iend + tchunk.Length, png.Length - iend);
            return outp.Length == target ? outp : null;
        }

        // Append zero bytes after IEND; decoders ignore trailing data (parity with iend_pad()).
        static byte[] IendPad(byte[] png, int target)
        {
            if (png.Length > target) return null;
            byte[] outp = new byte[target];
            Array.Copy(png, outp, png.Length);
            return outp;
        }

        static bool DecodesOk(byte[] png, int w, int h)
        {
            try
            {
                using (var ms = new MemoryStream(png))
                using (var b = new Bitmap(ms))
                    return b.Width == w && b.Height == h;
            }
            catch { return false; }
        }

        // Build a PNG of EXACTLY target bytes (parity with build_png_exact()).
        // The equal length is required so chrome.dll keeps its size and PE layout.
        public static byte[] BuildPngExact(byte[] masterPng, int target)
        {
            int[] dim = PngSize(masterPng);
            byte[] manual = ManualRebuild(masterPng);
            byte[][] candidates = new byte[][] {
                TextPad(masterPng, target), IendPad(masterPng, target),
                TextPad(manual, target), IendPad(manual, target) };
            foreach (byte[] cand in candidates)
                if (cand != null && DecodesOk(cand, dim[0], dim[1])) return cand;
            throw new Exception("cannot fit PNG into " + target + " bytes");
        }

        // ---------------- Google Profile.ico rebuild ----------------

        // Parse sizes from an existing ico and rebuild it from master (parity with patch_profile_ico).
        // ICO entries are written as PNG payloads (Vista+ taskbar supports PNG-in-ICO).
        public static void PatchIco(string icoPath, byte[] masterPng, bool apply)
        {
            byte[] oldIco = File.ReadAllBytes(icoPath);
            int cnt = BitConverter.ToUInt16(oldIco, 4);
            var sizes = new SortedList<int, int[]>();
            for (int i = 0; i < cnt; i++)
            {
                int o = 6 + 16 * i;
                int w = oldIco[o] == 0 ? 256 : oldIco[o];
                int h = oldIco[o + 1] == 0 ? 256 : oldIco[o + 1];
                sizes[w * 65536 + h] = new int[] { w, h };
            }
            string dirName = Path.GetFileName(Path.GetDirectoryName(icoPath));
            if (!apply)
            {
                Console.WriteLine("  [ico] " + dirName + ": would rebuild " + sizes.Count + " sizes");
                return;
            }
            var payloads = new List<byte[]>();
            foreach (var kv in sizes)
            {
                int[] s = kv.Value;
                payloads.Add((s[0] == 256 && s[1] == 256) ? masterPng : ResizePng(masterPng, s[0], s[1]));
            }
            int n = payloads.Count;
            int offset = 6 + 16 * n;
            byte[] outp = new byte[offset];
            outp[2] = 1; // type icon
            outp[4] = (byte)(n & 0xFF); outp[5] = (byte)(n >> 8);
            var entries = new List<byte[]>();
            for (int i = 0; i < n; i++)
            {
                int[] s = sizes.Values[i];
                byte[] e = new byte[16];
                e[0] = s[0] == 256 ? (byte)0 : (byte)s[0];
                e[1] = s[1] == 256 ? (byte)0 : (byte)s[1];
                e[4] = 1; e[5] = 0;  // planes
                e[6] = 32; e[7] = 0; // bitcount
                uint len = (uint)payloads[i].Length;
                e[8] = (byte)len; e[9] = (byte)(len >> 8); e[10] = (byte)(len >> 16); e[11] = (byte)(len >> 24);
                uint off = (uint)offset;
                e[12] = (byte)off; e[13] = (byte)(off >> 8); e[14] = (byte)(off >> 16); e[15] = (byte)(off >> 24);
                entries.Add(e);
                offset += payloads[i].Length;
            }
            using (var ms = new MemoryStream())
            {
                ms.Write(outp, 0, 6);
                foreach (byte[] e in entries) ms.Write(e, 0, 16);
                foreach (byte[] p in payloads) ms.Write(p, 0, p.Length);
                File.WriteAllBytes(icoPath, ms.ToArray());
            }
            Console.WriteLine("  [ico] " + dirName + ": rebuilt " + n + " sizes");
        }

        // ---------------- chrome.dll equal-size patch ----------------

        // Locate each old frame by a 32-byte marker at offset +40 and replace it with an
        // equal-length new PNG (parity with patch_chrome_dll). DLL size must stay identical.
        public static void PatchChromeDll(string dllPath, byte[] masterPng, int[] ids,
            byte[][] oldFrames, int[][] sizes, bool apply)
        {
            byte[] data = File.ReadAllBytes(dllPath);
            int origLen = data.Length;
            var planStart = new List<long>();
            var planOld = new List<byte[]>();
            var planNew = new List<byte[]>();
            var planId = new List<int>();
            for (int i = 0; i < ids.Length; i++)
            {
                byte[] old = oldFrames[i];
                byte[] marker = new byte[32];
                Array.Copy(old, 40, marker, 0, 32);
                long idx = IndexOfBytes(data, marker);
                if (idx < 0)
                {
                    Console.WriteLine("  [dll] frame id=" + ids[i] + ": old frame not found (may be patched), skip");
                    continue;
                }
                long start = idx - 40;
                bool full = start >= 0 && start + old.Length <= data.Length;
                if (full)
                    for (int j = 0; j < old.Length && full; j++)
                        if (data[start + j] != old[j]) full = false;
                if (!full)
                {
                    Console.WriteLine("  [dll] frame id=" + ids[i] + ": partial match only, skip for safety");
                    continue;
                }
                int[] s = sizes[i];
                if (s == null) s = (ids[i] == 8) ? new[] { 256, 256 } : new[] { new[] { 16, 24, 32, 48, 64, 96, 128 }[ids[i] - 1], 0 };
                byte[] srcPng = (s[0] == 256 && s[1] == 256) ? masterPng : ResizePng(masterPng, s[0], s[1]);
                planStart.Add(start); planOld.Add(old);
                planNew.Add(BuildPngExact(srcPng, old.Length)); planId.Add(ids[i]);
            }
            if (!apply)
            {
                Console.WriteLine("  [dll] dry-run: " + planStart.Count + " frames locatable and size-matched");
                return;
            }
            for (int k = 0; k < planStart.Count; k++)
            {
                Array.Copy(planNew[k], 0, data, planStart[k], planOld[k].Length);
                Console.WriteLine("  [dll] frame id=" + planId[k] + " patched @ 0x" + planStart[k].ToString("x"));
            }
            if (data.Length != origLen) throw new Exception("dll size must stay identical");
            File.WriteAllBytes(dllPath, data);
            Console.WriteLine("  [dll] written " + dllPath + " (" + origLen + "B, size preserved)");
        }

        static long IndexOfBytes(byte[] hay, byte[] needle)
        {
            int n = needle.Length;
            long lim = hay.Length - n;
            byte first = needle[0];
            for (long i = 0; i <= lim; i++)
            {
                if (hay[i] != first) continue;
                bool ok = true;
                for (int j = 1; j < n; j++)
                    if (hay[i + j] != needle[j]) { ok = false; break; }
                if (ok) return i;
            }
            return -1;
        }

        // ---------------- live window icon verification ----------------

        // Extract the running window's GCLP_HICON and score the legacy yellow bar
        // (right-top zone, max yellow pixel column; >=8 means OLD icon).
        // Returns -1 when no matching visible window is found.
        public static int LiveIconScore(string titlePart, string snapshotPath)
        {
            IntPtr found = IntPtr.Zero;
            EnumWindows(delegate(IntPtr hwnd, IntPtr param)
            {
                if (!IsWindowVisible(hwnd)) return true;
                var sb = new StringBuilder(512);
                GetWindowTextW(hwnd, sb, 512);
                if (sb.ToString().IndexOf(titlePart, StringComparison.OrdinalIgnoreCase) >= 0)
                { found = hwnd; return false; }
                return true;
            }, IntPtr.Zero);
            if (found == IntPtr.Zero) return -1;
            IntPtr hicon = GetClassLongPtrW(found, -14); // GCLP_HICON
            if (hicon == IntPtr.Zero) return -1;
            var info = new ICONINFO();
            if (!GetIconInfo(hicon, ref info)) return -1;
            int score = -1;
            if (info.hbmColor != IntPtr.Zero)
            {
                using (Bitmap b = Bitmap.FromHbitmap(info.hbmColor))
                {
                    int w = b.Width, h = b.Height;
                    int x0 = w * 84 / 100, y0 = h * 33 / 100;
                    int best = 0;
                    for (int x = x0; x < w; x++)
                    {
                        int col = 0;
                        for (int y = 0; y < y0; y++)
                        {
                            // GDI copy has no alpha; judge by color only
                            Color c = b.GetPixel(x, y);
                            if (c.R > 190 && c.G > 120 && c.B < 110) col++;
                        }
                        if (col > best) best = col;
                    }
                    score = best;
                    if (snapshotPath != null && snapshotPath.Length > 0)
                        b.Save(snapshotPath, ImageFormat.Png);
                }
                DeleteObject(info.hbmColor);
            }
            if (info.hbmMask != IntPtr.Zero) DeleteObject(info.hbmMask);
            DestroyIcon(hicon);
            return score;
        }
    }
}
