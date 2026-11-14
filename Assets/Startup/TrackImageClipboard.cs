using System;
using System.Runtime.InteropServices;
using UnityEngine;

internal static class TrackImageClipboard
{
    public static TrackLayoutPoint[] Import(TrackDefinition track)
    {
#if UNITY_EDITOR_WIN || UNITY_STANDALONE_WIN
        if (!OpenClipboard(IntPtr.Zero))
            throw new ArgumentException("The clipboard is busy. Try Paste Image again.");

        try
        {
            uint pngFormat = RegisterClipboardFormat("PNG");
            IntPtr png = pngFormat == 0 ? IntPtr.Zero : GetClipboardData(pngFormat);
            if (png != IntPtr.Zero)
            {
                ulong length = GlobalSize(png).ToUInt64();
                if (length > 20 * 1024 * 1024)
                    throw new ArgumentException("The copied image exceeds 20 MB.");
                IntPtr data = GlobalLock(png);
                if (data != IntPtr.Zero)
                {
                    byte[] bytes;
                    try
                    {
                        bytes = new byte[(int)length];
                        Marshal.Copy(data, bytes, 0, bytes.Length);
                    }
                    finally
                    {
                        GlobalUnlock(png);
                    }
                    return TrackImageTrace.Import(bytes, track);
                }
            }

            const uint bitmapFormat = 2;
            IntPtr bitmap = GetClipboardData(bitmapFormat);
            if (bitmap == IntPtr.Zero)
                throw new ArgumentException("Copy an image to the clipboard, then choose Paste Image.");

            if (GetObject(bitmap, Marshal.SizeOf<Bitmap>(), out Bitmap dimensions) == 0)
                throw new ArgumentException("The copied bitmap could not be read.");
            int width = dimensions.width;
            int height = dimensions.height;
            if (width <= 0 || height <= 0 || (long)width * height > 16_000_000)
                throw new ArgumentException("Choose a copied image smaller than 16 megapixels.");

            var info = new BitmapInfo
            {
                size = 40,
                width = width,
                height = height,
                planes = 1,
                bitsPerPixel = 32
            };
            var bytesBgra = new byte[width * height * 4];
            IntPtr dc = GetDC(IntPtr.Zero);
            try
            {
                if (dc == IntPtr.Zero || GetDIBits(dc, bitmap, 0, (uint)height, bytesBgra, ref info, 0) != height)
                    throw new ArgumentException("The copied bitmap could not be decoded.");
            }
            finally
            {
                if (dc != IntPtr.Zero)
                    ReleaseDC(IntPtr.Zero, dc);
            }

            var pixels = new Color32[width * height];
            for (int i = 0; i < pixels.Length; i++)
            {
                int offset = i * 4;
                pixels[i] = new Color32(bytesBgra[offset + 2], bytesBgra[offset + 1], bytesBgra[offset], 255);
            }
            return TrackImageTrace.Trace(pixels, width, height, track);
        }
        finally
        {
            CloseClipboard();
        }
#else
        throw new ArgumentException("Paste Image is available on Windows. Use Choose Image to open a PNG or JPEG.");
#endif
    }

#if UNITY_EDITOR_WIN || UNITY_STANDALONE_WIN
    [StructLayout(LayoutKind.Sequential)]
    private struct Bitmap
    {
        public int type;
        public int width;
        public int height;
        public int rowBytes;
        public ushort planes;
        public ushort bitsPerPixel;
        public IntPtr bits;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct BitmapInfo
    {
        public uint size;
        public int width;
        public int height;
        public ushort planes;
        public ushort bitsPerPixel;
        public uint compression;
        public uint imageSize;
        public int xPixelsPerMeter;
        public int yPixelsPerMeter;
        public uint colorsUsed;
        public uint importantColors;
        public uint firstColor;
    }

    [DllImport("user32.dll")]
    private static extern bool OpenClipboard(IntPtr owner);

    [DllImport("user32.dll")]
    private static extern bool CloseClipboard();

    [DllImport("user32.dll", CharSet = CharSet.Unicode, EntryPoint = "RegisterClipboardFormatW")]
    private static extern uint RegisterClipboardFormat(string format);

    [DllImport("user32.dll")]
    private static extern IntPtr GetClipboardData(uint format);

    [DllImport("kernel32.dll")]
    private static extern UIntPtr GlobalSize(IntPtr handle);

    [DllImport("kernel32.dll")]
    private static extern IntPtr GlobalLock(IntPtr handle);

    [DllImport("kernel32.dll")]
    private static extern bool GlobalUnlock(IntPtr handle);

    [DllImport("gdi32.dll", EntryPoint = "GetObjectW")]
    private static extern int GetObject(IntPtr handle, int size, out Bitmap bitmap);

    [DllImport("user32.dll")]
    private static extern IntPtr GetDC(IntPtr window);

    [DllImport("user32.dll")]
    private static extern int ReleaseDC(IntPtr window, IntPtr dc);

    [DllImport("gdi32.dll")]
    private static extern int GetDIBits(IntPtr dc, IntPtr bitmap, uint start, uint lines,
        [Out] byte[] pixels, ref BitmapInfo info, uint usage);
#endif
}
