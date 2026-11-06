using System.Runtime.InteropServices;
using System.Windows.Interop;
namespace Afmc.Find.App;
internal static class UpdateExit
{
    [DllImport("user32.dll",CharSet=CharSet.Unicode)] private static extern uint RegisterWindowMessage(string name);
    public static void Attach(MainWindow window)
    {
        var message=RegisterWindowMessage("AFMC.Find.ExitForUpdate.v1");
        var source=HwndSource.FromHwnd(new WindowInteropHelper(window).Handle);
        source?.AddHook((IntPtr hwnd,int msg,IntPtr w,IntPtr l,ref bool handled)=>
        {
            if(message!=0 && (uint)msg==message){handled=true;window.Dispatcher.BeginInvoke(window.ExitApplication);}
            return IntPtr.Zero;
        });
    }
}
