using System;
using VisionInspect.Core;
using VM.PlatformSDKCS;
using CoreImagePixelFormat = VisionInspect.Core.ImagePixelFormat;

namespace VisionInspect.VisionMaster.Interop
{
    /// <summary>
    /// RawImage -> VisionMaster ImageBaseData。
    /// VM 4.3.0 支持 VM_PIXEL_MONO_08 与 VM_PIXEL_RGB24_C3；BGR 输入在这一层转换成 RGB。
    /// </summary>
    public static class VmImageConverter
    {
        public static ImageBaseData ToImageBaseData(RawImage image)
        {
            if (image == null) throw new ArgumentNullException("image");

            if (image.Format == CoreImagePixelFormat.Mono8)
            {
                return new ImageBaseData(
                    image.Data,
                    (uint)image.DataLength,
                    image.Width,
                    image.Height,
                    (int)VMPixelFormat.VM_PIXEL_MONO_08);
            }

            byte[] rgb = image.Data;
            if (image.Format == CoreImagePixelFormat.Bgr24)
            {
                rgb = new byte[image.DataLength];
                for (int i = 0; i < rgb.Length; i += 3)
                {
                    rgb[i] = image.Data[i + 2];
                    rgb[i + 1] = image.Data[i + 1];
                    rgb[i + 2] = image.Data[i];
                }
            }

            return new ImageBaseData(
                rgb,
                (uint)image.DataLength,
                image.Width,
                image.Height,
                (int)VMPixelFormat.VM_PIXEL_RGB24_C3);
        }
    }
}
