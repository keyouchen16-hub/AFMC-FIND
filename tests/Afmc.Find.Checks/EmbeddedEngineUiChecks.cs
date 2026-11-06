using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Afmc.Find.App;

internal static class EmbeddedEngineUiChecks
{
    public static int Run()
    {
        var app=new Application{ShutdownMode=ShutdownMode.OnExplicitShutdown,Resources=new ResourceDictionary{Source=new Uri("/AFMC.Find;component/Theme.xaml",UriKind.Relative)}};
        var folder=Path.GetFullPath(".tools/engine-ui-"+Guid.NewGuid().ToString("N"));
        using var engine=new EmbeddedEngine(folder,Path.GetFullPath(".tools/jackett/runtime/Jackett"));
        var window=new EngineSourcesWindow(engine,"internetarchive");Exception? failure=null;int passed=0;
        void Check(bool ok,string text){if(!ok)throw new Exception(text);passed++;Console.WriteLine("PASS "+text);}
        window.Loaded+=async(_,_)=>
        {
            try
            {
                var add=(Button)window.FindName("AddButton");var list=(ListBox)window.FindName("CatalogList");
                var until=DateTime.UtcNow.AddSeconds(45);
                while(!add.IsEnabled&&DateTime.UtcNow<until)await Task.Delay(100);
                Check(add.IsEnabled&&list.SelectedItem is EngineSource{Id:"internetarchive"},"native window loads real source and its form");
                Check(window.FindName("KeyBox")==null&&window.FindName("EndpointBox")==null,"normal setup has no engine address/key entry");
                Check(((StackPanel)window.FindName("FieldsPanel")).Children.Count>0,"real schema rendered as native controls");
                Check(list.Items.Cast<EngineSource>().First().Type=="public","real directory puts public sources first");
                Check(list.Items.Cast<EngineSource>().Any(s=>s.Categories.Contains("书籍")),"real directory carries book category labels");
                var access=(ComboBox)window.FindName("AccessFilter");var category=(ComboBox)window.FindName("ResourceFilter");access.SelectedIndex=1;
                Check(list.Items.Count>0&&list.Items.Cast<EngineSource>().All(s=>s.Type=="public"),"native public-only filter works");
                category.SelectedIndex=5;
                Check(list.Items.Count>0&&list.Items.Cast<EngineSource>().All(s=>s.Categories.Contains("书籍")&&s.Type=="public"),"native book and public filters combine");
                access.SelectedIndex=0;category.SelectedIndex=0;
                var find=(TextBox)window.FindName("FindSourceBox");find.Text="https://archive.org/";
                Check(list.Items.Count>0&&list.Items.Cast<EngineSource>().All(x=>x.Site.Contains("archive.org")),"pasted website filters known adapters");
                list.SelectedIndex=0;until=DateTime.UtcNow.AddSeconds(30);while(!add.IsEnabled&&DateTime.UtcNow<until)await Task.Delay(100);
                Check(add.IsEnabled,"filtered source can be configured");
                window.UpdateLayout();
                await Task.Delay(300);await DialogGlassChecks.Capture(window,"catalog");
                var point=add.TranslatePoint(new Point(0,add.ActualHeight),window);Check(point.Y<window.ActualHeight&&point.Y>0,"primary action stays within window");
                Directory.CreateDirectory("docs/evidence/v0810");var image=new RenderTargetBitmap((int)window.ActualWidth,(int)window.ActualHeight,96,96,PixelFormats.Pbgra32);image.Render(window);var png=new PngBitmapEncoder();png.Frames.Add(BitmapFrame.Create(image));using(var file=File.Create("docs/evidence/v0810/embedded-source-dialog.png"))png.Save(file);
                add.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                until=DateTime.UtcNow.AddSeconds(100);while(window.IsVisible&&DateTime.UtcNow<until)await Task.Delay(100);
                if(window.IsVisible)throw new Exception("native save/test did not finish successfully: "+((TextBlock)window.FindName("StatusText")).Text);
            }
            catch(Exception ex){failure=ex;window.Close();}
        };
        var result=window.ShowDialog();if(failure is not null)throw failure;
        Check(result==true&&window.Result?.EngineIndexerId=="internetarchive"&&window.Result.ApiKey=="","native configure/test/add returns managed source without credentials");
        Console.WriteLine("ENGINE UI PASSED "+passed);return 0;
    }
}
