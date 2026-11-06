using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using Afmc.Find.App;
using Afmc.Find.Core;

internal static class SourceMenuChecks
{
    [StructLayout(LayoutKind.Sequential)]struct Rect {public int Left,Top,Right,Bottom;}
    [DllImport("user32.dll")]static extern bool GetWindowRect(IntPtr handle,out Rect bounds);
    [DllImport("user32.dll")]static extern IntPtr WindowFromPoint(System.Drawing.Point point);
    [DllImport("user32.dll")]static extern uint GetWindowThreadProcessId(IntPtr handle,out uint process);
    static IEnumerable<DependencyObject> Tree(DependencyObject root){yield return root;for(int i=0;i<VisualTreeHelper.GetChildrenCount(root);i++)foreach(var child in Tree(VisualTreeHelper.GetChild(root,i)))yield return child;}
    public static int Run()
    {
        var app=new Application{Resources=new ResourceDictionary{Source=new Uri("/AFMC.Find;component/Theme.xaml",UriKind.Relative)}};
        var store=new SettingsStore(Path.Combine(Path.GetTempPath(),"afmc-menu-"+Guid.NewGuid().ToString("N")));
        store.Save(SiteSources.Catalog.Select(s=>s with{Enabled=false}));
        var window=new MainWindow(store,new SearchService());var exit=1;
        window.Loaded+=async(_,_)=>{
            ContextMenu? menu=null;
            try{
                ((Button)window.FindName("SourcesNav")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));window.UpdateLayout();
                var list=(ListBox)window.FindName("SourceList");
                var button=Tree(list).OfType<Button>().First(b=>Equals(b.ToolTip,"来源操作"));
                button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));await Task.Delay(300);
                menu=PresentationSource.CurrentSources.Cast<PresentationSource>().Where(s=>s.RootVisual is not null).SelectMany(s=>Tree(s.RootVisual)).OfType<ContextMenu>().Single(m=>m.IsOpen);
                if(menu.Items.OfType<MenuItem>().Count()!=4)throw new Exception("Source actions missing");
                var handle=((HwndSource)PresentationSource.FromVisual(menu)).Handle;
                if(!GetWindowRect(handle,out var rect))throw new Exception("Popup bounds missing");
                var hit=WindowFromPoint(new System.Drawing.Point((rect.Left+rect.Right)/2,(rect.Top+rect.Bottom)/2));
                GetWindowThreadProcessId(hit,out var owner);
                if(owner!=Environment.ProcessId || hit!=handle)throw new Exception("Popup is obscured; screenshot is unverified");
                using var bitmap=new System.Drawing.Bitmap(rect.Right-rect.Left,rect.Bottom-rect.Top);
                using(var graphics=System.Drawing.Graphics.FromImage(bitmap))graphics.CopyFromScreen(rect.Left,rect.Top,0,0,bitmap.Size);
                var folder="docs/evidence/v0817";Directory.CreateDirectory(folder);
                var label=Environment.GetEnvironmentVariable("AFMC_MENU_LABEL")??"after";
                bitmap.Save(Path.Combine(folder,"source-menu-"+label+".png"));
                var bright=0;var sampled=0;
                // Empty popup gutter must not retain the system light menu background.
                for(var y=8;y<bitmap.Height-8;y++)for(var x=3;x<Math.Min(10,bitmap.Width-3);x++){
                    var pixel=bitmap.GetPixel(x,y);sampled++;if(pixel.R>180&&pixel.G>180&&pixel.B>180)bright++;
                }
                Console.WriteLine($"MENU gutter bright pixels={bright}/{sampled}, size={bitmap.Width}x{bitmap.Height}");
                if(bright>sampled/10)throw new Exception("White system-menu gutter remains");
                if(menu.Items.OfType<MenuItem>().Any(i=>i.ActualWidth<=0||i.ActualHeight<24))throw new Exception("Menu targets clipped");
                menu.IsOpen=false;Console.WriteLine("SOURCE MENU PASSED: real popup, four actions, dark gutter, usable row sizes");exit=0;
            }catch(Exception error){Console.Error.WriteLine(error);}
            finally{if(menu is not null)menu.IsOpen=false;window.ExitApplication();app.Shutdown(exit);app.Dispatcher.InvokeShutdown();}
        };
        app.MainWindow=window;app.Dispatcher.BeginInvoke(new Action(window.Show));app.Run();Console.WriteLine("Fixture dispatcher stopped");return exit;
    }
}
