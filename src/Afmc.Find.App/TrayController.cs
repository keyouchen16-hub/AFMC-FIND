using System.IO;
using System.Windows;
using Forms=System.Windows.Forms;

namespace Afmc.Find.App;
internal sealed class TrayController:IDisposable
{
    private readonly Forms.NotifyIcon icon;
    private readonly System.Drawing.Icon artwork;
    private readonly Forms.ContextMenuStrip menu=new();
    public TrayController(MainWindow window)
    {
        using var resource=Application.GetResourceStream(new Uri("pack://application:,,,/AFMC.Find;component/Assets/afmc-find.ico")).Stream;
        artwork=new System.Drawing.Icon(resource,32,32);
        icon=new Forms.NotifyIcon {Icon=artwork,Text=ProductInfo.Title,Visible=true,ContextMenuStrip=menu};
        menu.Items.Add("打开 AFMC FIND",null,(_,_)=>window.Dispatcher.Invoke(window.RestoreFromTray));
        menu.Items.Add("设置",null,(_,_)=>window.Dispatcher.Invoke(window.OpenPreferences));
        menu.Items.Add(new Forms.ToolStripSeparator());
        menu.Items.Add("退出",null,(_,_)=>window.Dispatcher.Invoke(window.ExitApplication));
        icon.MouseClick+=(_,e)=>{if(e.Button==Forms.MouseButtons.Left)window.Dispatcher.Invoke(window.RestoreFromTray);};
    }
    public void Dispose(){icon.Visible=false;icon.Dispose();menu.Dispose();artwork.Dispose();}
}
