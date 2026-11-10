using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using VisionInspect.Core;

namespace VisionInspect.Core.Algorithms
{
    /// <summary>
    /// 全项目通用图像中间格式。相机、VM、Halcon 之间只交换该对象，
    /// 避免任何上层代码依赖 MVS/VM/Halcon 的图像类型。
    ///
    /// 内存约定：
    /// - Data 是托管字节数组，生命周期由本对象负责；
    /// - DataPointer 在首次访问时固定托管数组，Dispose 时必须释放 GCHandle；
    /// - Stride 为一行字节数，Mono8 通常等于 Width，彩色通常等于 Width*3。
    /// </summary>
    public sealed class UniversalImageData : IDisposable
    {
        private GCHandle _pin;
        private bool _disposed;

        public UniversalImageData(byte[] data, int width, int height, int stride, ImagePixelFormat format,
            string source = null)
        {
            if (data == null) throw new ArgumentNullException("data");
            if (width <= 0) throw new ArgumentOutOfRangeException("width");
            if (height <= 0) throw new ArgumentOutOfRangeException("height");
            if (stride <= 0) throw new ArgumentOutOfRangeException("stride");

            int minimumStride = checked(width * (format == ImagePixelFormat.Mono8 ? 1 : 3));
            if (stride < minimumStride)
                throw new ArgumentException("Stride 小于图像一行的最小字节数。", "stride");
            if (data.Length < checked(stride * height))
                throw new ArgumentException("图像缓冲区长度不足。", "data");

            Data = data;
            Width = width;
            Height = height;
            Stride = stride;
            Format = format;
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

        /// <summary>固定后的首地址，仅在本对象 Dispose 前有效。</summary>
        public IntPtr DataPointer
        {
            get
            {
                ThrowIfDisposed();
                if (!_pin.IsAllocated)
                    _pin = GCHandle.Alloc(Data, GCHandleType.Pinned);
                return _pin.AddrOfPinnedObject();
            }
        }

        public int BytesPerPixel
        {
            get { return Format == ImagePixelFormat.Mono8 ? 1 : 3; }
        }

        public static UniversalImageData FromRawImage(RawImage image)
        {
            if (image == null) throw new ArgumentNullException("image");
            // 直接复用 RawImage 的托管缓冲区，避免相机高帧率下额外整帧复制。
            return new UniversalImageData(image.Data, image.Width, image.Height,
                image.Stride, image.Format, image.Source);
        }

        /// <summary>转为项目既有 RawImage；带行填充时重新紧凑排列。</summary>
        public RawImage ToRawImage()
        {
            ThrowIfDisposed();
            int packedStride = checked(Width * BytesPerPixel);
            if (Stride == packedStride)
                return new RawImage(Data, Width, Height, Format, Source);

            byte[] packed = new byte[checked(packedStride * Height)];
            for (int y = 0; y < Height; y++)
                Buffer.BlockCopy(Data, y * Stride, packed, y * packedStride, packedStride);
            return new RawImage(packed, Width, Height, Format, Source);
        }

        /// <summary>转 GDI+ Bitmap，供归档/非算法预览使用；调用方负责 Dispose。</summary>
        public Bitmap ToBitmap()
        {
            RawImage raw = ToRawImage();
            return raw.ToBitmap();
        }

        public UniversalImageData Clone()
        {
            ThrowIfDisposed();
            byte[] copy = new byte[Data.Length];
            Buffer.BlockCopy(Data, 0, copy, 0, Data.Length);
            return new UniversalImageData(copy, Width, Height, Stride, Format, Source);
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            if (_pin.IsAllocated) _pin.Free();
            Data = null;
        }

        private void ThrowIfDisposed()
        {
            if (_disposed) throw new ObjectDisposedException(GetType().Name);
        }
    }
}
