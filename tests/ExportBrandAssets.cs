using System;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using VisionInspect.App.UI;
internal static class ExportBrandAssets
{
    [STAThread] private static void Main(string[] args)
    {
        string folder=args[0];Directory.CreateDirectory(folder);
        using(var image=BrandAssets.Render(256,false))image.Save(Path.Combine(folder,"logo-mark.png"),ImageFormat.Png);
        using(var image=BrandAssets.Render(256,true))image.Save(Path.Combine(folder,"logo-mark-dark.png"),ImageFormat.Png);
        int[] sizes={16,24,32,48,64,128,256};
        byte[][] images=sizes.Select(size=>{using(var image=BrandAssets.Render(size,false))using(var memory=new MemoryStream()){image.Save(memory,ImageFormat.Png);return memory.ToArray();}}).ToArray();
        using(var writer=new BinaryWriter(File.Create(Path.Combine(folder,"VisionInspect.ico")))){
            writer.Write((short)0);writer.Write((short)1);writer.Write((short)sizes.Length);
            int offset=6+16*sizes.Length;
            for(int i=0;i<sizes.Length;i++){
                writer.Write((byte)(sizes[i]==256?0:sizes[i]));writer.Write((byte)(sizes[i]==256?0:sizes[i]));writer.Write((byte)0);writer.Write((byte)0);
                writer.Write((short)1);writer.Write((short)32);writer.Write(images[i].Length);writer.Write(offset);offset+=images[i].Length;
            }
            foreach(var bytes in images)writer.Write(bytes);
        }
        Console.WriteLine("Exported X Vision day/night logos and seven-size Windows icon: "+folder);
    }
}
