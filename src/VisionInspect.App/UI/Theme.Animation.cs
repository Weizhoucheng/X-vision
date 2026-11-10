using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace VisionInspect.App.UI
{
    public static partial class Theme
    {
        private static Task _transition=Task.CompletedTask;
        public static double LastTransitionMs { get; private set; }
        public static double LastFrameMaxMs { get; private set; }
        private sealed class ColorSlot { public Action<Color> Set; public Color Start; public Color End; }
        public static Task TransitionAsync(bool dark)
        {
            if (!_transition.IsCompleted) return _transition;
            if (dark==IsDark) return Task.CompletedTask;
            Color[] before=Palette(IsDark),after=Palette(dark);
            // 分层窗口的背景色是透明色键，属于渲染机制，不能当作界面底色改写。
            var windows=Application.OpenForms.Cast<Form>().Where(f=>!f.IsDisposed&&f.TransparencyKey.IsEmpty).ToArray();
            var slots=new List<ColorSlot>();var buttons=new List<Button>();var redraw=new List<Control>();
            foreach(var form in windows)Capture(form,before,after,slots,buttons,redraw);
            var completion=new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            _transition=completion.Task;bool previous=IsDark;
            SetDark(dark);SetPalette(before);
            var timer=new Timer{Interval=16};var watch=Stopwatch.StartNew();LastFrameMaxMs=0;
            timer.Tick+=(s,e)=>{
                var frame=Stopwatch.StartNew();
                try {
                    double progress=Math.Min(1,watch.Elapsed.TotalMilliseconds/190.0);
                    double eased=1-Math.Pow(1-progress,3);
                    SetPalette(before.Zip(after,(a,b)=>Blend(a,b,eased)).ToArray());
                    foreach(var slot in slots)slot.Set(Blend(slot.Start,slot.End,eased));
                    foreach(var button in buttons)if(!button.IsDisposed){
                        ButtonBinding binding;
                        ApplyButtonColors(button,ButtonKinds.TryGetValue(button,out binding)?binding.Kind:ButtonKind.Default);
                    }
                    // BackColor/ForeColor 本身会触发重绘；避免每帧重复刷新整个控件树。
                    foreach(var control in redraw)if(control is ListView&&!control.IsDisposed)control.Invalidate(true);
                    if(progress>=1){
                        timer.Stop();timer.Dispose();SetDark(dark);
                        foreach(var form in windows)if(!form.IsDisposed)ApplyWindowChrome(form);
                        LastTransitionMs=watch.Elapsed.TotalMilliseconds;completion.TrySetResult(true);
                    }
                } catch(Exception error) {
                    timer.Stop();timer.Dispose();SetDark(previous);
                    foreach(var slot in slots)try{slot.Set(slot.Start);}catch{}
                    completion.TrySetException(error);
                } finally {LastFrameMaxMs=Math.Max(LastFrameMaxMs,frame.Elapsed.TotalMilliseconds);}
            };
            timer.Start();return _transition;
        }
        private static Color Blend(Color a,Color b,double t)
        {return Color.FromArgb((int)(a.A+(b.A-a.A)*t),(int)(a.R+(b.R-a.R)*t),(int)(a.G+(b.G-a.G)*t),(int)(a.B+(b.B-a.B)*t));}
        private static void SetPalette(Color[] c)
        {
            Background=c[0];Surface=c[1];SurfaceAlt=c[2];Field=c[3];Border=c[4];Text=c[5];TextMuted=c[6];
            Accent=c[7];AccentDark=c[8];AccentSoft=c[9];Ok=c[10];Ng=c[11];Warning=c[12];Idle=c[13];
            BrandSurface=c[14];BrandSurfaceAlt=c[15];BrandText=c[16];BrandMuted=c[17];
        }
        private static void Add(List<ColorSlot> slots,Color value,Action<Color> set,Color[] old,Color[] next,bool foreground=false,int role=-1)
        {
            if(value.IsEmpty||value.A==0)return;
            if(role<0)for(int i=foreground?5:0;i<old.Length;i++)if(value.ToArgb()==old[i].ToArgb()){role=i;break;}
            if(role>=0)slots.Add(new ColorSlot{Set=set,Start=value,End=next[role]});
        }
        private static void Capture(Control c,Color[] old,Color[] next,List<ColorSlot> slots,List<Button> buttons,List<Control> redraw)
        {
            if(c is Form transparentForm&&!transparentForm.TransparencyKey.IsEmpty)return;
            string ns=c.GetType().Namespace??"";
            if(ns.StartsWith("VMControls",StringComparison.Ordinal)||ns.StartsWith("System.Windows.Forms.Integration",StringComparison.Ordinal))return;
            if(c is Button)buttons.Add((Button)c);
            else{
                int back=c is Form?0:c is TextBox||c is ComboBox||c is NumericUpDown?3:-1;
                Add(slots,c.BackColor,v=>{if(!c.IsDisposed)c.BackColor=v;},old,next,false,back);
                Add(slots,c.ForeColor,v=>{if(!c.IsDisposed)c.ForeColor=v;},old,next,true,c is Form?5:-1);
            }
            var grid=c as DataGridView;
            if(grid!=null){
                Add(slots,grid.BackgroundColor,v=>grid.BackgroundColor=v,old,next,false,1);
                foreach(var style in new[]{grid.DefaultCellStyle,grid.ColumnHeadersDefaultCellStyle,grid.AlternatingRowsDefaultCellStyle}){
                    var captured=style;
                    Add(slots,style.BackColor,v=>captured.BackColor=v,old,next);
                    Add(slots,style.ForeColor,v=>captured.ForeColor=v,old,next,true);
                    Add(slots,style.SelectionBackColor,v=>captured.SelectionBackColor=v,old,next);
                    Add(slots,style.SelectionForeColor,v=>captured.SelectionForeColor=v,old,next,true);
                }
                Add(slots,grid.GridColor,v=>grid.GridColor=v,old,next,false,4);
            }
            redraw.Add(c);
            foreach(Control child in c.Controls)Capture(child,old,next,slots,buttons,redraw);
        }
    }
}
