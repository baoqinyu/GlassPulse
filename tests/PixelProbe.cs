using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using System.Drawing.Imaging;

class PixelProbe
{
    [StructLayout(LayoutKind.Sequential)] struct R { public int L,T,Right,Bottom; }
    [DllImport("user32.dll")] static extern bool GetWindowRect(IntPtr h,out R r);
    [DllImport("user32.dll")] static extern int GetWindowRgn(IntPtr h,IntPtr r);
    [DllImport("gdi32.dll")] static extern IntPtr CreateRoundRectRgn(int left,int top,int right,int bottom,int width,int height);
    [DllImport("gdi32.dll")] static extern IntPtr CreateRectRgn(int a,int b,int c,int d);
    [DllImport("gdi32.dll")] static extern bool DeleteObject(IntPtr r);
    [DllImport("gdi32.dll")] static extern bool PtInRegion(IntPtr r,int x,int y);
    [DllImport("gdi32.dll")] static extern int GetRgnBox(IntPtr r,out R b);
    [DllImport("dwmapi.dll")] static extern int DwmFlush();
    static void Wait(int ms) {
        var frame=new DispatcherFrame();var timer=new DispatcherTimer(DispatcherPriority.Background){Interval=TimeSpan.FromMilliseconds(ms)};
        timer.Tick+=delegate{timer.Stop();frame.Continue=false;};timer.Start();Dispatcher.PushFrame(frame);DwmFlush();
    }
    static byte[] Pixels(System.Drawing.Bitmap b,out int stride) {
        var data=b.LockBits(new System.Drawing.Rectangle(0,0,b.Width,b.Height),ImageLockMode.ReadOnly,System.Drawing.Imaging.PixelFormat.Format24bppRgb);
        stride=data.Stride;byte[] pixels=new byte[stride*b.Height];Marshal.Copy(data.Scan0,pixels,0,pixels.Length);b.UnlockBits(data);return pixels;
    }
    static System.Drawing.Bitmap Capture(int x,int y,int width,int height) {
        var b=new System.Drawing.Bitmap(width,height,System.Drawing.Imaging.PixelFormat.Format24bppRgb);
        using(var g=System.Drawing.Graphics.FromImage(b))g.CopyFromScreen(x,y,0,0,new System.Drawing.Size(width,height),System.Drawing.CopyPixelOperation.SourceCopy);
        return b;
    }
    static void Set(object value,string name,object setting){value.GetType().GetProperty(name).SetValue(value,setting,null);}
    static DrawingBrush Pattern() {
        var group=new DrawingGroup();
        Color[] colors={Color.FromRgb(26,202,216),Color.FromRgb(245,214,74),Color.FromRgb(220,103,170),Color.FromRgb(66,104,198)};
        for(int i=0;i<4;i++)group.Children.Add(new GeometryDrawing(new SolidColorBrush(colors[i]),null,new RectangleGeometry(new Rect((i%2)*16,(i/2)*16,16,16))));
        return new DrawingBrush(group){TileMode=TileMode.Tile,ViewportUnits=BrushMappingMode.Absolute,Viewport=new Rect(0,0,32,32),Stretch=Stretch.Fill};
    }
    [STAThread]static int Main(string[] args) { try { return Run(args); } catch(CaptureUnavailable) { Console.WriteLine("PIXEL CHECK UNAVAILABLE: no visible synthetic desktop"); return 77; } catch(Exception ex) { for(int i=0;ex!=null&&i<5;i++,ex=ex.InnerException) { Console.WriteLine("ERROR TYPE "+ex.GetType().FullName); try {Console.WriteLine("MESSAGE "+ex.Message);}catch{} try{Console.WriteLine("STACK "+ex.StackTrace);}catch{} } return 2; } }
    sealed class CaptureUnavailable : Exception { public CaptureUnavailable() : base("The synthetic desktop backdrop was not visible") {} }
    static int Run(string[] args) {
        string exe=Path.GetFullPath(args[0]), output=Path.GetFullPath(args[1]);Directory.CreateDirectory(output);

        var app=new Application{ShutdownMode=ShutdownMode.OnExplicitShutdown};
        var wa=SystemParameters.WorkArea;
        var backdrop=new Window{Title="GlassPulse synthetic pixel verification",WindowStyle=WindowStyle.None,ResizeMode=ResizeMode.NoResize,ShowInTaskbar=false,ShowActivated=false,Topmost=true,
            Left=wa.Left,Top=wa.Top,Width=wa.Width,Height=wa.Height,Background=Pattern()};
        backdrop.Show();Wait(150);

        var assembly=Assembly.LoadFrom(exe);
        var prefs=Activator.CreateInstance(assembly.GetType("GlassPulse.Settings",true));
        Set(prefs,"AlwaysOnTop",false);Set(prefs,"CodexBudget",100m);Set(prefs,"ClaudeBudget",100m);Set(prefs,"CodexRoot","[synthetic]");Set(prefs,"ClaudeRoot","[synthetic]");Set(prefs,"Left",wa.Left+32);Set(prefs,"Top",wa.Top+20);
        var type=assembly.GetType("GlassPulse.WidgetWindow",true);
        var native=assembly.GetType("GlassPulse.NativeGlass",true);
        var apply=type.GetMethod("ApplyAppearance",BindingFlags.Instance|BindingFlags.NonPublic);
        var quit=type.GetMethod("Quit",BindingFlags.Instance|BindingFlags.NonPublic);
        var window=(Window)Activator.CreateInstance(type,BindingFlags.Instance|BindingFlags.NonPublic|BindingFlags.Public,null,new object[]{prefs,output,Path.Combine(Path.GetDirectoryName(exe),"data","pricing.json"),true},null);
        window.Topmost=true;window.ShowActivated=false;window.IsHitTestVisible=false;
        var view=(FrameworkElement)window.Content;ToolTipService.SetIsEnabled(view,false);

        window.Show();Wait(150);
        native.GetMethod("Attach").Invoke(null,new object[]{window,(Action)delegate{}});
        var reports=new List<object>();int failed=0,total=0;
        try {
          foreach(string theme in new[]{"dark","light"})foreach(string provider in new[]{"all","codex","claude"})foreach(string mode in new[]{"compact","dashboard","mini","mini-moved"}){
            total++;Set(prefs,"Theme",theme);Set(prefs,"Providers",provider);Set(prefs,"Expanded",mode=="dashboard");Set(prefs,"Mini",mode.StartsWith("mini",StringComparison.Ordinal));
            apply.Invoke(window,null);window.Topmost=true;
            window.Left=wa.Left+32+(mode=="mini-moved"?18:0);window.Top=wa.Top+20+(mode=="mini-moved"?12:0);
            var scroll=(ScrollViewer)view.FindName("DashboardScroll");scroll.MaxHeight=Math.Max(100,wa.Height-280);
            window.UpdateLayout();native.GetMethod("ApplyBackdrop").Invoke(null,new object[]{window,mode=="dashboard",theme=="light"});Wait(130);
            IntPtr handle=new WindowInteropHelper(window).Handle;R bounds;GetWindowRect(handle,out bounds);
            int margin=12,x=bounds.L-margin,y=bounds.T-margin,width=bounds.Right-bounds.L+2*margin,height=bounds.Bottom-bounds.T+2*margin;
            R bg;GetWindowRect(new WindowInteropHelper(backdrop).Handle,out bg);
            if(x<bg.L||y<bg.T||x+width>bg.Right||y+height>bg.Bottom)throw new InvalidOperationException("Fixture does not fully cover capture region");
            window.Hide();Wait(180);
            using(var before=Capture(x,y,width,height)){
              window.Show();window.Topmost=true;native.GetMethod("ApplyBackdrop").Invoke(null,new object[]{window,mode=="dashboard",theme=="light"});Wait(220);
              using(var after=Capture(x,y,width,height)){
                R shown;GetWindowRect(handle,out shown);if(shown.L!=bounds.L||shown.T!=bounds.T||shown.Right!=bounds.Right||shown.Bottom!=bounds.Bottom)throw new InvalidOperationException("Window moved during capture");
                var card=(Border)view.FindName("Frame");var cardBounds=card.TransformToAncestor(window).TransformBounds(new Rect(card.RenderSize));var dpi=PresentationSource.FromVisual(window).CompositionTarget.TransformToDevice;
                IntPtr region=CreateRoundRectRgn((int)Math.Round(cardBounds.Left*dpi.M11),(int)Math.Round(cardBounds.Top*dpi.M22),(int)Math.Ceiling(cardBounds.Right*dpi.M11),(int)Math.Ceiling(cardBounds.Bottom*dpi.M22),(int)Math.Round(card.CornerRadius.TopLeft*2*dpi.M11),(int)Math.Round(card.CornerRadius.TopLeft*2*dpi.M22));
                int kind=region==IntPtr.Zero?0:1;R box;GetRgnBox(region,out box);
                if(kind==0)throw new InvalidOperationException("Native rounded region is missing");
                int s1,s2;var a=Pixels(before,out s1);var b=Pixels(after,out s2);
                int[,] palette={{216,202,26},{74,214,245},{170,103,220},{198,104,66}};
                for(int yy=0;yy<height;yy++)for(int xx=0;xx<width;xx++){
                    int offset=yy*s1+xx*3;bool synthetic=false;
                    for(int color=0;color<4;color++)if(Math.Abs(a[offset]-palette[color,0])<=1&&Math.Abs(a[offset+1]-palette[color,1])<=1&&Math.Abs(a[offset+2]-palette[color,2])<=1){synthetic=true;break;}
                    if(!synthetic)throw new CaptureUnavailable();
                }long outside=0,changed=0,farOutside=0,farChanged=0,inside=0,insideChanged=0;int maximum=0;
                for(int py=0;py<height;py++)for(int px=0;px<width;px++){
                  int q1=py*s1+px*3,q2=py*s2+px*3;
                  int delta=Math.Max(Math.Abs(a[q1]-b[q2]),Math.Max(Math.Abs(a[q1+1]-b[q2+1]),Math.Abs(a[q1+2]-b[q2+2])));
                  int rx=px-margin,ry=py-margin;
                  bool inRegion=rx>=box.L&&rx<box.Right&&ry>=box.T&&ry<box.Bottom&&PtInRegion(region,rx,ry);
                  if(inRegion){inside++;if(delta>5)insideChanged++;continue;}
                  outside++;if(delta>5)changed++;
                  bool near=false;
                  if(rx>=box.L-2&&rx<=box.Right+2&&ry>=box.T-2&&ry<=box.Bottom+2)
                    for(int dy=-2;dy<=2&&!near;dy++)for(int dx=-2;dx<=2;dx++)if(PtInRegion(region,rx+dx,ry+dy)){near=true;break;}
                  if(!near){farOutside++;if(delta>5){farChanged++;maximum=Math.Max(maximum,delta);}}
                }
                DeleteObject(region);
                bool valid=inside>0&&insideChanged>inside/5;
                bool pass=valid&&changed==0;
                if(!pass)failed++;
                string name=theme+"-"+provider+"-"+mode;
                if(provider=="all"||!pass){before.Save(Path.Combine(output,name+"-before.png"),ImageFormat.Png);after.Save(Path.Combine(output,name+"-after.png"),ImageFormat.Png);}
                bool materialActive=native.GetMethod("IsMaterialActive")!=null&&(bool)native.GetMethod("IsMaterialActive").Invoke(null,new object[]{window});
                reports.Add(new{Case=name,MaterialActive=materialActive,ValidCapture=valid,OutsidePixels=outside,ChangedOutside=changed,FarOutsidePixels=farOutside,ChangedFarOutside=farChanged,MaximumOutsideDelta=maximum,InsideChanged=insideChanged,Passed=pass});
                Console.WriteLine((pass?"PASS ":"FAIL ")+name+" outside="+changed+" far="+farChanged+" max="+maximum+" capture="+valid+" material="+materialActive);
              }
            }
          }
        }finally{quit.Invoke(window,null);backdrop.Close();}
        var report=new System.Web.Script.Serialization.JavaScriptSerializer().Serialize(new{Total=total,Passed=total-failed,Failed=failed,InstalledVersion=assembly.GetName().Version.ToString(),Cases=reports});
        File.WriteAllText(Path.Combine(output,"pixel-results.json"),report);
        Console.WriteLine("PIXEL RESULT "+(total-failed)+"/"+total+" passed");return failed==0?0:1;
    }
}
