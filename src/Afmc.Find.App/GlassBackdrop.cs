using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;

namespace Afmc.Find.App;

internal sealed class GlassBackdrop(Window window, Action<bool> changed) : IDisposable
{
    [StructLayout(LayoutKind.Sequential)] private struct Margins { public int Left,Right,Top,Bottom; }
    [DllImport("dwmapi.dll")] private static extern int DwmSetWindowAttribute(IntPtr hwnd,int attribute,ref int value,int size);
    [DllImport("dwmapi.dll")] private static extern int DwmExtendFrameIntoClientArea(IntPtr hwnd,ref Margins margins);
    private HwndSource? source;
    public void Attach()
    {
        source=HwndSource.FromHwnd(new WindowInteropHelper(window).Handle);
        source?.AddHook(Hook);Apply();
    }
    private IntPtr Hook(IntPtr hwnd,int message,IntPtr wParam,IntPtr lParam,ref bool handled)
    {
        if(message is 0x031E or 0x031A or 0x001A) window.Dispatcher.BeginInvoke(Apply);
        return IntPtr.Zero;
    }
    private void Apply()
    {
        if(source is null||source.IsDisposed)return;
        bool enabled=false;
        if(OperatingSystem.IsWindowsVersionAtLeast(10,0,22621)&&!SystemParameters.HighContrast)
        {
            var dark=1;DwmSetWindowAttribute(source.Handle,20,ref dark,sizeof(int));
            var roundCorners=2;DwmSetWindowAttribute(source.Handle,33,ref roundCorners,sizeof(int));
            // Suppress the separate system caption fill over our shared acrylic surface.
            var captionNone=unchecked((int)0xFFFFFFFE);
            DwmSetWindowAttribute(source.Handle,35,ref captionNone,sizeof(int));
            var type=3;
            if(DwmSetWindowAttribute(source.Handle,38,ref type,sizeof(int))==0)
            {
                var margins=new Margins {Left=-1,Right=-1,Top=-1,Bottom=-1};
                enabled=DwmExtendFrameIntoClientArea(source.Handle,ref margins)==0;
            }
        }
        if(!enabled&&OperatingSystem.IsWindowsVersionAtLeast(10,0,22621))
        {
            var none=1;DwmSetWindowAttribute(source.Handle,38,ref none,sizeof(int));
            var margins=new Margins();DwmExtendFrameIntoClientArea(source.Handle,ref margins);
        }
        if(source.CompositionTarget is {} target)target.BackgroundColor=enabled?Colors.Transparent:Color.FromRgb(16,18,21);
        window.Background=enabled?Brushes.Transparent:new SolidColorBrush(Color.FromRgb(16,18,21));
        changed(enabled);
    }
    public void Dispose(){source?.RemoveHook(Hook);source=null;}
}
