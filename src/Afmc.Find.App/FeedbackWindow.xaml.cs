using System.Diagnostics;
using System.Windows;
namespace Afmc.Find.App;
public partial class FeedbackWindow:Window
{
    private GlassBackdrop? backdrop;
    private readonly string diagnostics;
    public FeedbackWindow(string? diagnostics=null)
    {
        this.diagnostics=diagnostics??DiagnosticReport.Build([]);
        InitializeComponent();
        SourceInitialized+=(_,_)=>{backdrop=new GlassBackdrop(this,_=>{});backdrop.Attach();};
        Closed+=(_,_)=>backdrop?.Dispose();
    }
    private void CopyDiagnostics(object sender,RoutedEventArgs e)
    {
        try{Clipboard.SetText(diagnostics);HintText.Text="已复制诊断，不包含搜索词、资源名称或密钥。";}
        catch(System.Runtime.InteropServices.ExternalException){HintText.Text="剪贴板忙，请稍后重试。";}
    }
    private void CopyEmail(object sender,RoutedEventArgs e)
    {
        try {Clipboard.SetText(ProductInfo.FeedbackEmail);HintText.Text="邮箱已复制。";}
        catch(System.Runtime.InteropServices.ExternalException){HintText.Text="剪贴板忙，请稍后重试。";}
    }
    private void WriteEmail(object sender,RoutedEventArgs e)
    {
        try {Process.Start(new ProcessStartInfo(ProductInfo.FeedbackUri){UseShellExecute=true});HintText.Text="已请求打开邮件应用，请在其中确认发送。";}
        catch(Exception ex) when(ex is System.ComponentModel.Win32Exception or InvalidOperationException){HintText.Text="无法打开邮件应用，请复制邮箱后手动反馈。";}
    }
}
