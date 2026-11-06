using System.Windows;
namespace Afmc.Find.App;
public partial class PreferencesWindow:Window
{
    private GlassBackdrop? backdrop;
    public Preferences Value {get;private set;}
    public PreferencesWindow(Preferences value)
    {
        InitializeComponent();Value=value;
        MaxHeight=Math.Max(300,SystemParameters.WorkArea.Height-40);
        SourceInitialized+=(_,_)=>{backdrop=new GlassBackdrop(this,_=>{});backdrop.Attach();};
        Closed+=(_,_)=>backdrop?.Dispose();
        CloseToTrayBox.IsChecked=value.CloseToTray;MinimizeToTrayBox.IsChecked=value.MinimizeToTray;
        RememberHistoryBox.IsChecked=value.RememberHistory;
        ReduceMotionBox.IsChecked=value.ReduceMotion;
    }
    private void SaveClick(object sender,RoutedEventArgs e)
    {
        Value=Value with {CloseToTray=CloseToTrayBox.IsChecked==true,MinimizeToTray=MinimizeToTrayBox.IsChecked==true,RememberHistory=RememberHistoryBox.IsChecked==true,ReduceMotion=ReduceMotionBox.IsChecked==true};
        DialogResult=true;
    }
    private void CopyEmailClick(object sender,RoutedEventArgs e)
    {
        try {Clipboard.SetText(ProductInfo.FeedbackEmail);SettingsFeedbackHint.Text="邮箱已复制。";}
        catch(System.Runtime.InteropServices.ExternalException){SettingsFeedbackHint.Text="剪贴板忙，请稍后重试。";}
    }
}
