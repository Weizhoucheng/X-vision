using System;
using System.Drawing;
using System.IO;
using System.Reflection;
using System.Threading;
using System.Windows.Forms;
using VisionInspect.App.UI;
internal static class RuntimeEnvironmentPreview
{
    [STAThread]static int Main(string[] args)
    {
        try{Application.EnableVisualStyles();Application.SetCompatibleTextRenderingDefault(false);Directory.CreateDirectory(args[1]);
            foreach(bool dark in new[]{false,true}){Theme.SetDark(dark);using(var form=new RuntimeEnvironmentForm(args[0]){Opacity=0,ShowInTaskbar=false}){
                form.Show();var text=(TextBox)typeof(RuntimeEnvironmentForm).GetField("_details",BindingFlags.Instance|BindingFlags.NonPublic).GetValue(form);
                var timer=System.Diagnostics.Stopwatch.StartNew();while(!text.Text.Contains("检查时间")){Application.DoEvents();Thread.Sleep(2);if(timer.ElapsedMilliseconds>10000)throw new TimeoutException("Environment check UI timed out");}
                if(!text.Text.Contains("未验证"))throw new Exception("UI must not claim a valid license from configuration alone");
                using(var bitmap=new Bitmap(form.Width,form.Height)){form.DrawToBitmap(bitmap,new Rectangle(Point.Empty,bitmap.Size));bitmap.Save(Path.Combine(args[1],dark?"runtime-night.png":"runtime-day.png"));}form.Close();
            }}Console.WriteLine("PASS runtime diagnostics UI in both themes, no SDK preparation required");return 0;
        }catch(Exception ex){Console.Error.WriteLine(ex);return 1;}
    }
}
