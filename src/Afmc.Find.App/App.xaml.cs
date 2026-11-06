using System.Windows;
namespace Afmc.Find.App;
public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        if(e.Args.Contains("--check-config"))
        {
            try{var store=new SettingsStore();store.Load();Preferences.Load(store.DirectoryPath);_ = new PersonalLibrary(store.DirectoryPath);Shutdown(0);}
            catch(Exception error) when(error is not OutOfMemoryException){Shutdown(2);}
            return;
        }
        base.OnStartup(e);
    }
    protected override void OnSessionEnding(SessionEndingCancelEventArgs e)
    {
        if(MainWindow is MainWindow window)window.ExitApplication();
        base.OnSessionEnding(e);
    }
}
