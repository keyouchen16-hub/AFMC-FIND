using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shell;
namespace Afmc.Find.App;

internal sealed class WorkbenchDialog:Window
{
    private GlassBackdrop? backdrop;
    public StackPanel Tools {get;}=new(){Orientation=Orientation.Horizontal,Margin=new(0,0,0,12)};
    public Grid Body {get;}=new();
    public WorkbenchDialog(Window owner,string title,double width=720,double height=480)
    {
        Owner=owner;Title="AFMC FIND · "+title;Width=width;Height=height;MinWidth=Math.Min(width,600);MinHeight=280;WindowStyle=WindowStyle.None;ResizeMode=ResizeMode.CanResize;WindowStartupLocation=WindowStartupLocation.CenterOwner;ShowInTaskbar=false;
        SetResourceReference(StyleProperty,typeof(Window));WindowChrome.SetWindowChrome(this,new(){CaptionHeight=34,ResizeBorderThickness=new(4),GlassFrameThickness=new(-1),UseAeroCaptionButtons=false});
        var outer=new Border{Background=new SolidColorBrush(Color.FromArgb(235,16,18,21)),BorderThickness=new(1),BorderBrush=new SolidColorBrush(Color.FromArgb(48,255,255,255))};
        var dock=new DockPanel();outer.Child=dock;Content=outer;
        var caption=new Grid{Height=34};DockPanel.SetDock(caption,Dock.Top);dock.Children.Add(caption);
        caption.Children.Add(new TextBlock{Text=ProductInfo.Title,FontSize=12,Margin=new(16,0,0,0),VerticalAlignment=VerticalAlignment.Center});
        var close=new Button{Content="\uE8BB",FontSize=11,HorizontalAlignment=HorizontalAlignment.Right,Style=(Style)FindResource("SettingsIconButton")};close.Click+=(_,_)=>Close();WindowChrome.SetIsHitTestVisibleInChrome(close,true);caption.Children.Add(close);
        var layout=new DockPanel{Margin=new(24,12,24,24)};dock.Children.Add(layout);
        var heading=new TextBlock{Text=title,FontSize=23,FontWeight=FontWeights.SemiBold,Margin=new(0,0,0,16)};DockPanel.SetDock(heading,Dock.Top);layout.Children.Add(heading);
        DockPanel.SetDock(Tools,Dock.Top);layout.Children.Add(Tools);layout.Children.Add(Body);
        SourceInitialized+=(_,_)=>{backdrop=new(this,_=>{});backdrop.Attach();};Closed+=(_,_)=>backdrop?.Dispose();
    }
}
