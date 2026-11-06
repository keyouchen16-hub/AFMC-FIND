using System.IO;
using System.Collections.Concurrent;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using Afmc.Find.App;
using Drawing=System.Drawing;

internal static class PlayerDragChecks
{
    [StructLayout(LayoutKind.Sequential)] struct Box {public int Left,Top,Right,Bottom;public readonly Drawing.Rectangle Rectangle=>Drawing.Rectangle.FromLTRB(Left,Top,Right,Bottom);}
    [DllImport("user32.dll")] static extern bool GetWindowRect(IntPtr hwnd,out Box box);
    [DllImport("user32.dll")] static extern bool SetCursorPos(int x,int y);
    [DllImport("user32.dll")] static extern void mouse_event(uint flags,uint x,uint y,uint data,UIntPtr extra);
    [DllImport("user32.dll")] static extern bool SetForegroundWindow(IntPtr hwnd);
    [DllImport("user32.dll")] static extern IntPtr WindowFromPoint(Drawing.Point point);
    [DllImport("user32.dll")] static extern uint GetWindowThreadProcessId(IntPtr hwnd,out uint processId);
    [DllImport("user32.dll")] static extern bool IsWindowVisible(IntPtr hwnd);
    [DllImport("user32.dll")] static extern bool GetCursorPos(out Drawing.Point point);
    [DllImport("user32.dll")] static extern short GetAsyncKeyState(int key);
    static Drawing.Bitmap Capture(Drawing.Rectangle area){var image=new Drawing.Bitmap(area.Width,area.Height);using var graphics=Drawing.Graphics.FromImage(image);graphics.CopyFromScreen(area.Location,Drawing.Point.Empty,area.Size);return image;}
    public static int Run()
    {
        var app=new Application{ShutdownMode=ShutdownMode.OnExplicitShutdown,Resources=new ResourceDictionary{Source=new Uri("/AFMC.Find;component/Theme.xaml",UriKind.Relative)}};
        var folder=Path.GetFullPath("docs/evidence/player-drag/"+(Environment.GetEnvironmentVariable("AFMC_DRAG_LABEL")??"before"));Directory.CreateDirectory(folder);
        var background=new Window{WindowStyle=WindowStyle.None,ResizeMode=ResizeMode.NoResize,Left=20,Top=20,Width=1400,Height=850,Background=new SolidColorBrush(Color.FromRgb(160,15,160)),ShowInTaskbar=false,Topmost=true};
        var window=new PlayerWindow(Path.GetFullPath(".tools/player-drag-profile")){WindowStartupLocation=WindowStartupLocation.Manual,Left=85,Top=85,Width=820,Height=520,Topmost=true};
        int exit=1;var results=new List<object>();
        background.Loaded+=async(_,_)=>{
            try{
                await Task.Delay(300);var backdropHandle=new WindowInteropHelper(background).Handle;GetWindowRect(backdropHandle,out var backdrop);var area=backdrop.Rectangle;
                using var baseline=Capture(area);baseline.Save(Path.Combine(folder,"background.png"));
                window.Owner=background;window.Show();await Task.Delay(350);
                var handle=new WindowInteropHelper(window).Handle;
                var overlay=(FrameworkElement)window.FindName("VideoOverlay");var overlayHandle=((HwndSource)PresentationSource.FromVisual(overlay)).Handle;
                var videoPath=Path.GetFullPath(".tools/player-fixture/AFMC-test.avi");
                using var server=new PlaybackChecks.VideoServer(videoPath);
                foreach(var round in Enumerable.Range(0,4))
                {
                    var playing=round>=2;
                    if(playing){await window.OpenAsync(server.Url+"test.avi");var until=DateTime.UtcNow.AddSeconds(15);while(window.NativePlayer?.Time<1000&&DateTime.UtcNow<until)await Task.Delay(100);if(window.NativePlayer?.Time<1000)throw new Exception("Fixture did not play");window.NativePlayer!.Mute=true;}
                    window.Left=85;window.Top=85;await Task.Delay(300);
                    var controls=(FrameworkElement)window.FindName("WindowControls");
                    var events=new ConcurrentQueue<string>();
                    controls.AddHandler(UIElement.PreviewMouseLeftButtonDownEvent,new System.Windows.Input.MouseButtonEventHandler((_,e)=>events.Enqueue($"HeaderDown {e.OriginalSource.GetType().Name} {e.LeftButton}")),true);
                    window.LocationChanged+=(_,_)=>events.Enqueue($"HostMoved {window.Left},{window.Top}");
                    var start=controls.PointToScreen(new Point(180,16));
                    var target=System.Windows.Media.VisualTreeHelper.HitTest(overlay,overlay.PointFromScreen(start));
                    events.Enqueue($"Target={target?.VisualHit.GetType().Name} controlsHit={controls.IsHitTestVisible} controlsVisible={controls.IsVisible} overlayHit={overlay.IsHitTestVisible}");
                    GetWindowRect(handle,out var initialBox);
                    var samples=new List<object>();int maxMismatch=0,maxDrift=0,visibleDuringMove=0,whiteFrames=0;
                    var videoTimeBefore=window.NativePlayer?.Time ?? 0;
                    window.Activate();Window.GetWindow(overlay).Activate();
                    SetForegroundWindow(overlayHandle);
                    await Task.Delay(250); // establish activation before synthetic button-down
                    await Task.Run(()=>{
                        var startPoint=new Drawing.Point((int)start.X,(int)start.Y);SetCursorPos(startPoint.X,startPoint.Y);Thread.Sleep(100);
                        var hit=WindowFromPoint(startPoint);GetWindowThreadProcessId(hit,out var owner);events.Enqueue($"Start {startPoint}, hit={hit}, overlay={overlayHandle}, host={handle}");if(owner!=Environment.ProcessId)throw new Exception("Drag target not owned by fixture; no input sent");
                        mouse_event(2,0,0,0,UIntPtr.Zero);
                        Thread.Sleep(100);
                        GetCursorPos(out var actualCursor);events.Enqueue($"Cursor={actualCursor} LeftDown={GetAsyncKeyState(1)}");
                        try{
                            for(var step=1;step<=24;step++){
                                var dx=round%2==0?step*14:step<=8?step*35:step<=16?280-(step-8)*30:40+(step-16)*37;
                                SetCursorPos(startPoint.X+dx,startPoint.Y+step*4);Thread.Sleep(round%2==0?20:5);
                                GetWindowRect(handle,out var main);GetWindowRect(overlayHandle,out var top);var opacity=window.Dispatcher.Invoke(()=>Window.GetWindow(overlay).Opacity);var visible=IsWindowVisible(overlayHandle)&&opacity>0;
                                if(visible)visibleDuringMove++;
                                var drift=visible?Math.Max(Math.Abs(main.Left-top.Left),Math.Abs(main.Top-top.Top)):0;maxDrift=Math.Max(maxDrift,drift);
                                using var frame=Capture(area);var excluded=main.Rectangle;excluded.Inflate(20,20);int mismatch=0;
                                if(!playing){var center=frame.GetPixel(Math.Clamp((main.Left+main.Right)/2-area.Left,0,frame.Width-1),Math.Clamp((main.Top+main.Bottom)/2-area.Top,0,frame.Height-1));if(center.R>180&&center.G>180&&center.B>180)whiteFrames++;}
                                for(var y=30;y<area.Height-30;y+=5)for(var x=30;x<area.Width-30;x+=5){if(excluded.Contains(area.Left+x,area.Top+y))continue;var expected=baseline.GetPixel(x,y);var actual=frame.GetPixel(x,y);if(Math.Abs(actual.R-expected.R)+Math.Abs(actual.G-expected.G)+Math.Abs(actual.B-expected.B)>35)mismatch++;}
                                if(mismatch>maxMismatch){maxMismatch=mismatch;frame.Save(Path.Combine(folder,playing?"playing-peak.png":"idle-peak.png"));}
                                samples.Add(new{step,MainX=main.Left,MainY=main.Top,OverlayX=top.Left,OverlayY=top.Top,OverlayVisible=visible,Drift=drift,OutsideMismatchSamples=mismatch});
                            }
                        }finally{mouse_event(4,0,0,0,UIntPtr.Zero);}
                    });
                    await Task.Delay(400);using var settled=Capture(area);settled.Save(Path.Combine(folder,playing?"playing-settled.png":"idle-settled.png"));
                    GetWindowRect(handle,out var end);
                    var excludedSettled=end.Rectangle;excludedSettled.Inflate(20,20);var settledMismatch=0;
                    for(var y=30;y<area.Height-30;y+=5)for(var x=30;x<area.Width-30;x+=5){if(excludedSettled.Contains(area.Left+x,area.Top+y))continue;var expected=baseline.GetPixel(x,y);var actual=settled.GetPixel(x,y);if(Math.Abs(actual.R-expected.R)+Math.Abs(actual.G-expected.G)+Math.Abs(actual.B-expected.B)>35)settledMismatch++;}
                    GetWindowRect(overlayHandle,out var restored);var overlayRestored=IsWindowVisible(overlayHandle)&&Window.GetWindow(overlay).Opacity==1;
                    var videoTimeAfter=window.NativePlayer?.Time ?? 0;
                    results.Add(new{Playing=playing,WhiteFrames=whiteFrames,SettledMismatch=settledMismatch,MaxDrift=maxDrift,MaxOutsideMismatchSamples=maxMismatch,VisibleDuringMove=visibleDuringMove,OverlayRestored=overlayRestored,VideoTimeBefore=videoTimeBefore,VideoTimeAfter=videoTimeAfter,Events=events.ToArray(),StartX=initialBox.Left,EndX=end.Left,Samples=samples});
                    if(end.Left-initialBox.Left<100)throw new Exception("Window did not actually move");
                    if(visibleDuringMove!=0)throw new Exception("Layered controls visible during native drag");
                    if(whiteFrames!=0)throw new Exception("Idle native video surface flashed white during drag");
                    if(settledMismatch!=0)throw new Exception("Desktop retains pixels outside player after releasing drag");
                    if(!overlayRestored||Math.Abs(end.Left-restored.Left)>1||Math.Abs(end.Top-restored.Top)>1)throw new Exception("Controls not restored at final window position");
                    if(playing&&videoTimeAfter<=videoTimeBefore)throw new Exception("Playback clock stopped during drag");
                    Console.WriteLine($"DRAG playing={playing} max drift={maxDrift}px outside mismatch samples={maxMismatch}; synthetic real mouse move");
                }
                exit=0;
            }catch(Exception e){Console.Error.WriteLine(e);}
            finally{File.WriteAllText(Path.Combine(folder,"drag.json"),JsonSerializer.Serialize(results,new JsonSerializerOptions{WriteIndented=true}));window.Close();await Task.Delay(800);background.Close();app.Shutdown(exit);}
        };
        app.Run(background);return exit;
    }
}
