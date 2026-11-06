using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using Afmc.Find.App;

internal static class DialogGlassChecks
{
    [StructLayout(LayoutKind.Sequential)]private struct Rect{public int Left,Top,Right,Bottom;}
    [DllImport("dwmapi.dll")]private static extern int DwmGetWindowAttribute(IntPtr hwnd,int attribute,out int value,int size);
    [DllImport("user32.dll")]private static extern bool GetWindowRect(IntPtr hwnd,out Rect rect);
    [DllImport("user32.dll")]private static extern bool SetForegroundWindow(IntPtr hwnd);
    [DllImport("user32.dll")]private static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")]private static extern uint GetWindowThreadProcessId(IntPtr hwnd,out uint process);
    [DllImport("kernel32.dll")]private static extern uint GetCurrentThreadId();
    [DllImport("user32.dll")]private static extern bool AttachThreadInput(uint from,uint to,bool attach);
    [DllImport("user32.dll")]private static extern IntPtr SetFocus(IntPtr hwnd);
    public static async Task Capture(Window window,string name)
    {
        var handle=new WindowInteropHelper(window).Handle;
        bool TargetForeground()
        {
            var foreground=GetForegroundWindow();
            if(foreground==handle)return true;
            // LibVLCSharp's input layer is a separate, owned top-level HWND.
            // Accept only that exact layer, in this process, covering this player.
            if(window is not PlayerWindow || window.FindName("VideoOverlay") is not Visual overlay || PresentationSource.FromVisual(overlay) is not HwndSource layer || foreground!=layer.Handle)return false;
            GetWindowThreadProcessId(foreground,out var owner);
            return owner==Environment.ProcessId&&GetWindowRect(handle,out var host)&&GetWindowRect(foreground,out var top)
                &&Math.Abs(host.Left-top.Left)<=2&&Math.Abs(host.Top-top.Top)<=2&&Math.Abs(host.Right-top.Right)<=2&&Math.Abs(host.Bottom-top.Bottom)<=2;
        }
        if(window is not PlayerWindow && OperatingSystem.IsWindowsVersionAtLeast(10,0,22621)&&!SystemParameters.HighContrast)
        {
            if(DwmGetWindowAttribute(handle,38,out var value,4)!=0||value!=3)throw new Exception(name+" missing system acrylic");
            if(window.Background is not SolidColorBrush{Color.A:0})throw new Exception(name+" opaque window background");
        }
        if (window is PlayerWindow) {
            if(window.Content is not Border {Background:SolidColorBrush {Color: var black}, BorderThickness:var border} || black != Colors.Black || border != new Thickness(0)) throw new Exception(name+" video surface must have black backing and no frame");
            Console.WriteLine("PASS "+name+" black video backing without frame");
        } else {
            if(window.Content is not Border{Background:LinearGradientBrush tint}||tint.GradientStops.Any(s=>s.Color.A>160)||!ReferenceEquals(tint,window.FindResource("DialogGlassTint")))throw new Exception(name+" does not share translucent glass tint");
            Console.WriteLine("PASS "+name+" compositor backdrop and shared tint");
        }
        var wasTopmost=window.Topmost;window.Topmost=true;window.Activate();SetForegroundWindow(handle);
        await Task.Delay(250);
        if(!TargetForeground())
        {
            var current=GetCurrentThreadId();var foreground=GetWindowThreadProcessId(GetForegroundWindow(),out _);
            var attached=current!=foreground&&foreground!=0&&AttachThreadInput(current,foreground,true);
            try{SetForegroundWindow(handle);SetFocus(handle);}finally{if(attached)AttachThreadInput(current,foreground,false);}
            await Task.Delay(250);
        }
        if(!TargetForeground()){window.Topmost=wasTopmost;throw new Exception("Screenshot target is not foreground");}
        if(!GetWindowRect(handle,out var rect))throw new Exception("window rect unavailable");
        Directory.CreateDirectory("docs/evidence/v0810");
        using var bitmap=new System.Drawing.Bitmap(rect.Right-rect.Left,rect.Bottom-rect.Top);
        using(var graphics=System.Drawing.Graphics.FromImage(bitmap))graphics.CopyFromScreen(rect.Left,rect.Top,0,0,bitmap.Size);
        bitmap.Save("docs/evidence/v0810/"+name+"-screen.png",System.Drawing.Imaging.ImageFormat.Png);
        if (window is PlayerWindow) {
            var pixel=bitmap.GetPixel(8,8);
            if(pixel.R>20||pixel.G>20||pixel.B>20)throw new Exception("Player edge pixels must be black, not translucent chrome");
        }
        window.Topmost=wasTopmost;
    }
    public static int Run()
    {
        var app=new Application{ShutdownMode=ShutdownMode.OnExplicitShutdown,Resources=new ResourceDictionary{Source=new Uri("/AFMC.Find;component/Theme.xaml",UriKind.Relative)}};
        var background=new Window{Width=900,Height=700,WindowStartupLocation=WindowStartupLocation.CenterScreen,ShowInTaskbar=false,Content=new Border{Background=new LinearGradientBrush(Color.FromRgb(170,60,110),Color.FromRgb(20,130,180),0)}};background.Show();
        var window=new PreferencesWindow(new Preferences()){Owner=background};Exception? error=null;
        window.Loaded+=async(_,_)=>{try{await Task.Delay(600);await Capture(window,"settings");}catch(Exception ex){error=ex;}finally{window.Close();}};
        window.ShowDialog();background.Close();if(error is not null)throw error;Console.WriteLine("DIALOG GLASS PASSED 1");return 0;
    }
}
