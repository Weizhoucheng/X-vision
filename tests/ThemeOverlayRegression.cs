using System;
using System.Drawing;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using VisionInspect.App.UI;
using VisionInspect.App.UI.Controls;

internal static class ThemeOverlayRegression
{
    [STAThread] static int Main(string[] args)
    {
        try {
            Application.EnableVisualStyles(); Application.SetCompatibleTextRenderingDefault(false);
            Directory.CreateDirectory(args[0]); Theme.SetDark(true);
            using (var owner = new Form { Size = new Size(640,400), ShowInTaskbar = false, Opacity = 0 })
            using (var transparent = new Form { Size = new Size(50,50), ShowInTaskbar = false, Opacity = 0, TransparencyKey = Color.Lime, BackColor = Color.Lime })
            using (var overlay = new CrosshairOverlayWindow { PaintTopCover = false, ShowCrosshair = true }) {
                owner.Show(); transparent.Show(); overlay.SetScreenBounds(new Rectangle(owner.Location, new Size(600,340))); overlay.SetResult("OK", true); overlay.Show(owner);
                IntPtr handle = overlay.Handle; Color key = overlay.TransparencyKey;
                for(int i=0;i<20;i++) {
                    var transition = Theme.TransitionAsync(i%2!=0); Pump(transition);
                    if (overlay.BackColor.ToArgb() != key.ToArgb()) throw new Exception("Theme changed overlay transparent background to " + overlay.BackColor + "; opaque overlay covers the VM image.");
                    if (overlay.Handle != handle) throw new Exception("Theme recreated overlay handle.");
                    if (transparent.BackColor.ToArgb() != Color.Lime.ToArgb()) throw new Exception("Generic transparent forms must be excluded from theme transitions.");
                    using (var bitmap = new Bitmap(overlay.Width,overlay.Height)) {
                        overlay.DrawToBitmap(bitmap,new Rectangle(Point.Empty,bitmap.Size));
                        if(bitmap.GetPixel(5,5).ToArgb()!=key.ToArgb()) throw new Exception("Overlay background pixels no longer match TransparencyKey.");
                        if(i<2)bitmap.Save(Path.Combine(args[0],i==0?"overlay-day.png":"overlay-night.png"));
                    }
                }
                // 旧版直接应用主题的入口同样不得改写分层窗口的透明键颜色。
                Theme.SetDark(false); Theme.ApplyTheme(overlay);
                Theme.ApplyTheme(transparent);
                if(transparent.BackColor.ToArgb()!=Color.Lime.ToArgb())throw new Exception("Immediate theme path changed generic transparency.");
                if(overlay.BackColor.ToArgb()!=key.ToArgb())throw new Exception("Immediate theme path changed overlay transparency.");
                overlay.BackColor=Theme.Background;
                if(overlay.BackColor.ToArgb()!=key.ToArgb())throw new Exception("Overlay did not protect its transparency key from general styling.");
                overlay.Close(); owner.Close();
            }
            Console.WriteLine("PASS 20 day/night transitions preserve overlay transparency, background pixels, and native handle; direct theme apply also preserves key.");return 0;
        } catch(Exception ex) { Console.Error.WriteLine(ex); return 1; }
    }
    static void Pump(Task task) { var timer=System.Diagnostics.Stopwatch.StartNew();while(!task.IsCompleted){Application.DoEvents();Thread.Sleep(2);if(timer.ElapsedMilliseconds>10000)throw new TimeoutException();}task.GetAwaiter().GetResult();Application.DoEvents(); }
}
