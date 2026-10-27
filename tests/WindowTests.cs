using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
namespace GlassPulse.Tests {
static class WindowTests {
[DllImport("gdi32.dll")]static extern IntPtr CreateRectRgn(int a,int b,int c,int d);
[DllImport("gdi32.dll")]static extern bool DeleteObject(IntPtr r);
[DllImport("gdi32.dll")]static extern bool PtInRegion(IntPtr r,int x,int y);
[DllImport("user32.dll")]static extern int GetWindowRgn(IntPtr h,IntPtr r);
static void Pump(){var f=new DispatcherFrame();Dispatcher.CurrentDispatcher.BeginInvoke(DispatcherPriority.ApplicationIdle,new Action(()=>f.Continue=false));Dispatcher.PushFrame(f);}
[STAThread]static int Main(string[] args){
var app=new Application {ShutdownMode=ShutdownMode.OnExplicitShutdown};
var p=new Settings {AlwaysOnTop=false,CodexBudget=100,ClaudeBudget=100};
var w=new WidgetWindow(p,args[1],args[0],true){Left=60,Top=60,ShowActivated=false};
w.Show();NativeGlass.Attach(w,()=>{});Pump();int fail=0,total=0;
foreach(var theme in new[]{"dark","light"}) foreach(var providers in new[]{"all","codex","claude"}) foreach(var mode in new[]{"compact","dashboard","mini"}){
p.Theme=theme;p.Providers=providers;total++;
p.Expanded=mode=="dashboard";p.Mini=mode=="mini";w.ApplyAppearance();NativeGlass.ApplyBackdrop(w,p.Expanded&&!p.Mini,p.Theme=="light");Pump();
var frame=w.Get<System.Windows.Controls.Border>("Frame");
var b=frame.TransformToAncestor(w).TransformBounds(new Rect(frame.RenderSize));
var scale=PresentationSource.FromVisual(w).CompositionTarget.TransformToDevice;
int l=(int)Math.Round(b.Left*scale.M11),t=(int)Math.Round(b.Top*scale.M22);
int r=(int)Math.Ceiling(b.Right*scale.M11),bottom=(int)Math.Ceiling(b.Bottom*scale.M22);
IntPtr region=CreateRectRgn(0,0,0,0);int kind=GetWindowRgn(new WindowInteropHelper(w).Handle,region);
bool ok=kind>0&&!PtInRegion(region,0,0)&&!PtInRegion(region,l,t)&&PtInRegion(region,(l+r)/2,(t+bottom)/2)&&!PtInRegion(region,r+1,bottom+1);
DeleteObject(region);Console.WriteLine((ok?"PASS ":"FAIL ")+theme+"/"+providers+"/"+mode+" native rounded region");if(!ok)fail++;
}
w.Quit();Console.WriteLine("WINDOW RESULT "+(total-fail)+"/"+total+" passed");return fail==0?0:1;
}}}