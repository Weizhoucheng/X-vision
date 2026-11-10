using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Runtime.InteropServices;

namespace VisionInspect.Core
{
    /// <summary>
    /// 与 SDK 解耦的原始图像对象。
    /// 约定：Mono8 的 Stride = Width；RGB/BGR 的 Stride = Width * 3。
    /// 相机回调与本地图片读取统一转换成该对象，再由 VisionMaster 适配层转换为 ImageBaseData。
    /// </summary>
    public sealed class RawImage
    {
        public RawImage(byte[] data, int width, int height, ImagePixelFormat format, string source = null)
        {
            if (data == null) throw new ArgumentNullException("data");
            if (width <= 0) throw new ArgumentOutOfRangeException("width");
            if (height <= 0) throw new ArgumentOutOfRangeException("height");
            if (!Enum.IsDefined(typeof(ImagePixelFormat), format))
                throw new ArgumentOutOfRangeException("format");

            int bytesPerPixel = format == ImagePixelFormat.Mono8 ? 1 : 3;
            int stride = checked(width * bytesPerPixel);
            int required = checked(stride * height);
            if (data.Length < required)
            {
                throw new ArgumentException(
                    string.Format("图像缓冲区长度不足。需要 {0} 字节，实际 {1} 字节。",
                        required, data.Length), "data");
            }

            Data = data;
            Width = width;
            Height = height;
            Format = format;
            Stride = stride;
            Source = source ?? string.Empty;
            TimestampUtc = DateTime.UtcNow;
        }

        public byte[] Data { get; private set; }
        public int Width { get; private set; }
        public int Height { get; private set; }
        public int Stride { get; private set; }
        public ImagePixelFormat Format { get; private set; }
        public string Source { get; private set; }
        public DateTime TimestampUtc { get; private set; }

        public int BytesPerPixel
        {
            get { return Format == ImagePixelFormat.Mono8 ? 1 : 3; }
        }

        public bool IsMono
        {
            get { return Format == ImagePixelFormat.Mono8; }
        }

        /// <summary>从本地图片文件读取并转换为 Mono8 或 RGB24。支持 8 位灰度、24/32 位彩色。</summary>
        public static RawImage FromFile(string filePath, string source = null)
        {
            if (!File.Exists(filePath))
                throw new FileNotFoundException("图片文件不存在。", filePath);

            using (var bitmap = new Bitmap(filePath))
            {
                return FromBitmap(bitmap, source ?? Path.GetFileName(filePath));
            }
        }

        /// <summary>从 Bitmap 转换。非 8 位索引格式统一转成 24 位 RGB，避免相机/VM 像素格式不一致。</summary>
        public static RawImage FromBitmap(Bitmap bitmap, string source = null)
        {
            if (bitmap == null) throw new ArgumentNullException("bitmap");

            if (bitmap.PixelFormat == PixelFormat.Format8bppIndexed && HasIdentityGrayPalette(bitmap))
            {
                var rect = new Rectangle(0, 0, bitmap.Width, bitmap.Height);
                BitmapData bits = bitmap.LockBits(rect, ImageLockMode.ReadOnly, PixelFormat.Format8bppIndexed);
                try
                {
                    int stride = bitmap.Width;
                    byte[] buffer = new byte[stride * bitmap.Height];
                    for (int y = 0; y < bitmap.Height; y++)
                    {
                        IntPtr src = IntPtr.Add(bits.Scan0, y * bits.Stride);
                        Marshal.Copy(src, buffer, y * stride, stride);
                    }
                    return new RawImage(buffer, bitmap.Width, bitmap.Height,
                        ImagePixelFormat.Mono8, source);
                }
                finally
                {
                    bitmap.UnlockBits(bits);
                }
            }

            using (var rgb = new Bitmap(bitmap.Width, bitmap.Height, PixelFormat.Format24bppRgb))
            {
                using (var graphics = Graphics.FromImage(rgb))
                {
                    graphics.CompositingMode = System.Drawing.Drawing2D.CompositingMode.SourceCopy;
                    graphics.DrawImageUnscaled(bitmap, 0, 0);
                }

                var rect = new Rectangle(0, 0, rgb.Width, rgb.Height);
                BitmapData bits = rgb.LockBits(rect, ImageLockMode.ReadOnly, PixelFormat.Format24bppRgb);
                try
                {
                    int stride = rgb.Width * 3;
                    byte[] buffer = new byte[stride * rgb.Height];
                    for (int y = 0; y < rgb.Height; y++)
                    {
                        IntPtr src = IntPtr.Add(bits.Scan0, y * bits.Stride);
                        Marshal.Copy(src, buffer, y * stride, stride);
                    }
                    // GDI+ 的 Format24bppRgb 内存实际按 B,G,R 排列；VM 的 RGB24 按 R,G,B。
                    SwapRedBlue(buffer);
                    return new RawImage(buffer, rgb.Width, rgb.Height,
                        ImagePixelFormat.Rgb24, source);
                }
                finally
                {
                    rgb.UnlockBits(bits);
                }
            }
        }

        /// <summary>转 Bitmap 用于预览和图片归档。调用方负责 Dispose。</summary>
        public Bitmap ToBitmap()
        {
            if (Format == ImagePixelFormat.Mono8)
            {
                var bitmap = new Bitmap(Width, Height, PixelFormat.Format8bppIndexed);
                ColorPalette palette = bitmap.Palette;
                for (int i = 0; i < 256; i++)
                    palette.Entries[i] = Color.FromArgb(255, i, i, i);
                bitmap.Palette = palette;

                var rect = new Rectangle(0, 0, Width, Height);
                BitmapData bits = bitmap.LockBits(rect, ImageLockMode.WriteOnly, PixelFormat.Format8bppIndexed);
                try
                {
                    for (int y = 0; y < Height; y++)
                    {
                        IntPtr dst = IntPtr.Add(bits.Scan0, y * bits.Stride);
                        Marshal.Copy(Data, y * Stride, dst, Width);
                    }
                }
                finally
                {
                    bitmap.UnlockBits(bits);
                }
                return bitmap;
            }

            var rgbBitmap = new Bitmap(Width, Height, PixelFormat.Format24bppRgb);
            var rgbRect = new Rectangle(0, 0, Width, Height);
            BitmapData rgbBits = rgbBitmap.LockBits(rgbRect, ImageLockMode.WriteOnly, PixelFormat.Format24bppRgb);
            try
            {
                if (Format == ImagePixelFormat.Bgr24)
                {
                    for (int y = 0; y < Height; y++)
                    {
                        IntPtr dst = IntPtr.Add(rgbBits.Scan0, y * rgbBits.Stride);
                        Marshal.Copy(Data, y * Stride, dst, Width * 3);
                    }
                }
                else
                {
                    // RGB -> GDI+ 的 BGR 内存，逐行转换，避免修改共享原始帧。
                    byte[] row = new byte[Width * 3];
                    for (int y = 0; y < Height; y++)
                    {
                        int offset = y * Stride;
                        for (int x = 0; x < Width; x++)
                        {
                            row[x * 3] = Data[offset + x * 3 + 2];
                            row[x * 3 + 1] = Data[offset + x * 3 + 1];
                            row[x * 3 + 2] = Data[offset + x * 3];
                        }
                        IntPtr dst = IntPtr.Add(rgbBits.Scan0, y * rgbBits.Stride);
                        Marshal.Copy(row, 0, dst, row.Length);
                    }
                }
            }
            finally
            {
                rgbBitmap.UnlockBits(rgbBits);
            }
            return rgbBitmap;
        }

        private static bool HasIdentityGrayPalette(Bitmap bitmap)
        {
            Color[] entries = bitmap.Palette.Entries;
            if (entries.Length != 256) return false;
            for (int i = 0; i < entries.Length; i++)
            {
                Color color = entries[i];
                if (color.R != i || color.G != i || color.B != i || color.A != 255) return false;
            }
            return true;
        }

        private static void SwapRedBlue(byte[] buffer)
        {
            for (int offset = 0; offset < buffer.Length; offset += 3)
            {
                byte red = buffer[offset + 2];
                buffer[offset + 2] = buffer[offset];
                buffer[offset] = red;
            }
        }

        /// <summary>返回按当前像素格式有效的字节数（去掉 stride 填充，当前实现无填充）。</summary>
        public int DataLength
        {
            get { return Width * Height * BytesPerPixel; }
        }
    }
}
