using System.IO;
using System.Net.Http;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;

namespace Afmc.Find.App;

internal sealed class OnlineSubtitleWindow : Window
{
    private static readonly HttpClient Client = new(new HttpClientHandler { AllowAutoRedirect = false }) { Timeout = TimeSpan.FromSeconds(16), MaxResponseContentBufferSize = 3 * 1024 * 1024 };
    private readonly TextBox query = new();
    private readonly ListBox results = new();
    private readonly TextBlock hint = new() { TextWrapping = TextWrapping.Wrap };
    private readonly Button search = new() { Content = "搜索" };
    private readonly Button choose = new() { Content = "查看字幕" };
    private readonly CancellationTokenSource lifetime = new();
    private readonly Func<string,bool> load;
    private sealed record Entry(string Label, string Id, Uri? Download = null) { public override string ToString() => Label; }

    public OnlineSubtitleWindow(string title, Func<string,bool> load)
    {
        this.load = load;
        Title = "在线字幕"; Width = 560; Height = 435; WindowStartupLocation = WindowStartupLocation.CenterOwner;
        var body = new DockPanel { Margin = new Thickness(18) };
        var top = new StackPanel();
        top.Children.Add(new TextBlock { Text = Title, FontSize = 22, FontWeight = FontWeights.SemiBold });
        var bar = new DockPanel { Margin = new Thickness(0,12,0,8) };
        DockPanel.SetDock(search,Dock.Right); bar.Children.Add(search); bar.Children.Add(query); top.Children.Add(bar);
        hint.Text = "输入电影片名或 IMDb 编号。剧集可用 tt编号:季:集。";
        top.Children.Add(hint); DockPanel.SetDock(top,Dock.Top); body.Children.Add(top);
        DockPanel.SetDock(choose,Dock.Bottom); body.Children.Add(choose); body.Children.Add(results);
        Content = body; DialogGlass.Attach(this);
                query.Name = "SubtitleQuery"; results.Name = "SubtitleResults"; search.Name = "SearchSubtitles"; choose.Name = "ChooseSubtitle";
        System.Windows.Automation.AutomationProperties.SetAutomationId(query,query.Name);
        System.Windows.Automation.AutomationProperties.SetAutomationId(results,results.Name);
        System.Windows.Automation.AutomationProperties.SetAutomationId(search,search.Name);
        System.Windows.Automation.AutomationProperties.SetAutomationId(choose,choose.Name);
        query.Text = Regex.Split(title, @"(?i)[ ._\-](?:19\d{2}|20\d{2}|1080p|2160p|720p|bluray)\b")[0].Replace('.', ' ');
        search.Click += async (_,_) => await RunAsync(SearchAsync);
        query.KeyDown += async (_,e) => { if(e.Key == System.Windows.Input.Key.Enter && search.IsEnabled) await RunAsync(SearchAsync); };
        choose.Click += async (_,_) => await RunAsync(ChooseAsync);
        Closed += (_,_) => lifetime.Cancel();
    }

    private async Task RunAsync(Func<Task> operation)
    {
        search.IsEnabled = choose.IsEnabled = false;
        try { await operation(); }
        catch (OperationCanceledException) { if(!lifetime.IsCancellationRequested) hint.Text = "字幕服务连接超时，请重试。"; }
        catch (Exception e) when(e is HttpRequestException or JsonException or IOException or InvalidOperationException) { hint.Text = "字幕服务暂时无法返回有效结果，请重试或加载本地字幕。"; }
        finally { search.IsEnabled = choose.IsEnabled = true; }
    }

    private static string Field(JsonElement item,string name) => item.TryGetProperty(name,out var value) ? value.ToString() : "";
    private async Task<JsonElement[]> GetListAsync(string url,string key)
    {
        var bytes = await Client.GetByteArrayAsync(url,lifetime.Token);
        using var doc = JsonDocument.Parse(bytes);
        if (!doc.RootElement.TryGetProperty(key,out var list) || list.ValueKind != JsonValueKind.Array) throw new InvalidDataException("Missing subtitle catalog");
        return list.EnumerateArray().Select(x=>x.Clone()).ToArray();
    }

    private async Task SearchAsync()
    {
        var text = query.Text.Trim(); if(text.Length==0) return;
        results.Items.Clear(); choose.Content = "查看字幕"; hint.Text = "正在搜索影片…";
        if(Regex.IsMatch(text,@"^tt[0-9]{6,12}(:[0-9]{1,3}:[0-9]{1,4})?$")) { await SubtitlesAsync(text); return; }
        var response = await GetListAsync("https://v3-cinemeta.strem.io/catalog/movie/top/search="+Uri.EscapeDataString(text)+".json","metas");
        foreach(var item in response.Take(35)) results.Items.Add(new Entry(Field(item,"name")+"　"+Field(item,"releaseInfo"),Field(item,"id")));
        hint.Text = results.Items.Count==0 ? "没有匹配影片，可试英文原名或 IMDb 编号。" : "选择片名和年份，再查看字幕。";
        results.SelectedIndex = results.Items.Count>0 ? 0 : -1;
    }
    private async Task SubtitlesAsync(string id)
    {
        var type = id.Contains(':') ? "series" : "movie";
        hint.Text = "正在查找字幕…";
        var response = await GetListAsync($"https://opensubtitles-v3.strem.io/subtitles/{type}/{Uri.EscapeDataString(id)}.json","subtitles");
        results.Items.Clear(); choose.Content = "加载选中字幕";
        foreach(var item in response.OrderBy(x => Field(x,"lang") switch {"chi" or "zho" or "zht" or "zhs" => 0, "eng" => 1, _ => 2}))
        {
            if(!Uri.TryCreate(Field(item,"url"),UriKind.Absolute,out var uri) || uri.Scheme!="https" || !uri.IdnHost.EndsWith(".strem.io",StringComparison.OrdinalIgnoreCase)) continue;
                        var language = Field(item,"lang") switch { "chi" or "zho" => "中文", "zhs" => "简体中文", "zht" => "繁體中文", "eng" => "英语", var other => other };
            var release = Field(item,"movieReleaseName");
            results.Items.Add(new Entry(language+"　"+(release.Length>0 ? release : Field(item,"subtitleFileName")),id,uri));
        }
        hint.Text = results.Items.Count==0 ? "这部影片暂未找到字幕。" : "按语言和发行版本选择字幕。";
        results.SelectedIndex = results.Items.Count>0 ? 0 : -1;
    }
    private async Task ChooseAsync()
    {
        if(results.SelectedItem is not Entry entry) return;
        if(entry.Download is null) { await SubtitlesAsync(entry.Id); return; }
        hint.Text = "正在下载字幕…";
        var bytes = await Client.GetByteArrayAsync(entry.Download,lifetime.Token);
        if (!System.Text.Encoding.UTF8.GetString(bytes).Contains("-->")) throw new InvalidDataException("Invalid timed subtitle");
        var path = Path.Combine(Path.GetTempPath(),"afmc-sub-"+Guid.NewGuid().ToString("N")+".srt");
        try {
            await File.WriteAllBytesAsync(path,bytes,lifetime.Token);
            if(!load(path)) throw new IOException("Could not load subtitle");
        } catch { File.Delete(path); throw; }
        Close();
    }
}
