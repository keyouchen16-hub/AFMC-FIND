using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shell;

namespace Afmc.Find.App;

// Reuse the main window's compositor material and tint for source dialogs.
internal static class DialogGlass
{
    public static void Attach(Window window, bool useBackdrop = true)
    {
        var content=window.Content;window.Content=null;
        window.WindowStyle=WindowStyle.None;window.ShowInTaskbar=false;
        WindowChrome.SetWindowChrome(window,new WindowChrome{CaptionHeight=34,ResizeBorderThickness=new Thickness(5),GlassFrameThickness=new Thickness(-1),UseAeroCaptionButtons=false});
        var caption=new Grid{Height=34};
        caption.Children.Add(new TextBlock{Text=ProductInfo.Title,FontSize=12,Foreground=(Brush)window.FindResource("Muted"),Margin=new Thickness(16,0,45,0),VerticalAlignment=VerticalAlignment.Center});
        var close=new Button{Content="\uE8BB",Style=(Style)window.FindResource("SettingsIconButton"),FontSize=11,HorizontalAlignment=HorizontalAlignment.Right,ToolTip="关闭"};
        System.Windows.Automation.AutomationProperties.SetName(close,"关闭窗口");
        WindowChrome.SetIsHitTestVisibleInChrome(close,true);close.Click+=(_,_)=>window.Close();caption.Children.Add(close);
        DockPanel.SetDock(caption,Dock.Top);var panel=new DockPanel();panel.Children.Add(caption);panel.Children.Add(new ContentPresenter{Content=content});
        var surface=new Border{Background=(Brush)window.FindResource("DialogGlassTint"),BorderBrush=new SolidColorBrush(Color.FromArgb(48,255,255,255)),BorderThickness=new Thickness(1),Child=panel};
        window.Content=surface;
        GlassBackdrop? backdrop=null;
        if (useBackdrop) window.SourceInitialized+=(_,_)=>{backdrop=new GlassBackdrop(window,_=>{});backdrop.Attach();};
        window.Closed+=(_,_)=>backdrop?.Dispose();
    }
}
