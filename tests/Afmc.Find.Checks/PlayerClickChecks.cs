using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using Afmc.Find.App;
using Afmc.Find.Core;

internal static class PlayerClickChecks
{
    [DllImport("user32.dll")]static extern bool SetCursorPos(int x,int y);
    [DllImport("user32.dll")]static extern void mouse_event(uint flags,uint x,uint y,uint data,UIntPtr extra);
    [DllImport("user32.dll")]static extern IntPtr WindowFromPoint(System.Drawing.Point point);
    [DllImport("user32.dll")]static extern bool SetForegroundWindow(IntPtr window);
    public static int Run()
    {
        var app=new Application{Resources=new ResourceDictionary{Source=new Uri("/AFMC.Find;component/Theme.xaml",UriKind.Relative)}};
        var store=new SettingsStore(Path.Combine(Path.GetTempPath(),"afmc-click-"+Guid.NewGuid().ToString("N")));
        store.Save(SiteSources.Catalog.Select(s=>s with {Enabled=false}));
        var main=new MainWindow(store,new SearchService());var exit=1;PlayerWindow? player=null;
        main.Loaded+=async(_,_)=>{
            try{
                ((Button)main.FindName("PlayerEntryButton")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                await Task.Delay(500);player=app.Windows.OfType<PlayerWindow>().Single();
                var overlay=(FrameworkElement)player.FindName("VideoOverlay");
                var overlayWindow=Window.GetWindow(overlay);
                var handle=new WindowInteropHelper(overlayWindow).Handle;
                player.Topmost=true;await Task.Delay(200);main.Activate();player.Activate();overlayWindow.Activate();SetForegroundWindow(handle);player.Topmost=false;await Task.Delay(500);
                using var server=new PlaybackChecks.VideoServer(Path.GetFullPath(".tools/player-fixture/AFMC-test.avi"));
                for(int i=0;i<6;i++){
                    if(i==3){await player.OpenAsync(server.Url+"test.avi");var until=DateTime.UtcNow.AddSeconds(20);while(player.NativePlayer?.Time<500&&DateTime.UtcNow<until)await Task.Delay(100);if(player.NativePlayer is not {Time:>=500})throw new Exception("Fixture is not decoding");}
                    var point=overlay.PointToScreen(new Point(overlay.ActualWidth*.5,overlay.ActualHeight*.4));
                    var hit=WindowFromPoint(new System.Drawing.Point((int)point.X,(int)point.Y));
                    Console.WriteLine($"Before click {i}: hit={hit} overlay={handle} hostVisible={player.IsVisible} opacity={overlayWindow.Opacity}");
                    if(hit!=handle)throw new Exception("Player is occluded before input");
                    SetCursorPos((int)point.X,(int)point.Y);await Task.Delay(100);
                    mouse_event(2,0,0,0,UIntPtr.Zero);await Task.Delay(60);mouse_event(4,0,0,0,UIntPtr.Zero);
                    await Task.Delay(600);
                    hit=WindowFromPoint(new System.Drawing.Point((int)point.X,(int)point.Y));
                    Console.WriteLine($"After click {i}: hit={hit} overlay={handle} hostVisible={player.IsVisible} opacity={overlayWindow.Opacity}");
                    if(!player.IsVisible||!overlayWindow.IsVisible||overlayWindow.Opacity<.9||hit!=handle)throw new Exception("Click hides or occludes player");
                }
                Console.WriteLine("PLAYER CLICK PASSED");exit=0;
            }catch(Exception error){Console.Error.WriteLine(error);}
            finally{if(player is not null)await player.ClosePlayerAsync();main.ExitApplication();app.Shutdown(exit);app.Dispatcher.InvokeShutdown();}
        };
        app.MainWindow=main;app.Dispatcher.BeginInvoke(new Action(main.Show));app.Run();return exit;
    }
}
