using System.Windows;
using Afmc.Find.Core;
namespace Afmc.Find.App;
public partial class SourceDialog : Window
{
    private readonly SourceConfig? original;
    private readonly CancellationTokenSource lifetime = new();
    public SourceConfig? Result { get; private set; }
    public IReadOnlyList<SourceConfig> Results { get; private set; } = [];
    public SourceDialog(SourceConfig? source = null)
    {
        InitializeComponent(); original = source;
        DialogGlass.Attach(this);
        EndpointBox.Text = source?.Endpoint ?? "http://127.0.0.1:9117/";
        KeyBox.Password = source?.ApiKey ?? "";
        NameBox.Text = source?.Name ?? "";
        EnabledBox.IsChecked = source?.Enabled ?? true;
        if (source is not null)
        {
            EditOptions.Visibility = Visibility.Visible; ConnectButton.Visibility = Visibility.Collapsed;
            SaveButton.Content = "保存"; SaveButton.IsEnabled = true;
            AddressLabel.Text = "Torznab API 地址"; HintText.Text = "编辑已有来源，保存后可在来源列表测试连接。";
        }
        EndpointBox.TextChanged += (_, _) => InvalidateList();
        KeyBox.PasswordChanged += (_, _) => InvalidateList();
        Closed += (_, _) => lifetime.Cancel();
    }
    private void InvalidateList()
    {
        if (original is not null) return;
        IndexerList.ItemsSource = null; IndexerList.Visibility = Visibility.Collapsed; SaveButton.IsEnabled = false;
    }
    private async void Connect(object sender, RoutedEventArgs e)
    {
        ConnectButton.IsEnabled = false;
        try
        {
            var connection = JackettConnection.Parse(EndpointBox.Text, KeyBox.Password);
            EndpointBox.Text = connection.BaseUrl; KeyBox.Password = connection.ApiKey;
            EndpointBox.IsEnabled = KeyBox.IsEnabled = false;
            ErrorText.Text = "正在读取已配置来源…";
            using var service = new SearchService();
            var rows = await service.JackettIndexersAsync(connection, lifetime.Token);
            if (lifetime.IsCancellationRequested) return;
            IndexerList.ItemsSource = rows; IndexerList.Visibility = Visibility.Visible;
            SaveButton.IsEnabled = rows.Count > 0;
            ErrorText.Text = rows.Count == 0 ? "没有已配置来源，请先在 Jackett 页面添加并测试来源。" : $"已读取 {rows.Count} 个来源，请勾选导入；导入不代表每个上游站点均可用。";
        }
        catch (Exception ex) when (ex is not OutOfMemoryException) { ErrorText.Text = Links.Error(ex); }
        finally { ConnectButton.IsEnabled = EndpointBox.IsEnabled = KeyBox.IsEnabled = true; }
    }
    private void Save(object sender, RoutedEventArgs e)
    {
        try
        {
            if (original is null)
            {
                Results = IndexerList.SelectedItems.Cast<SourceConfig>().ToArray();
                if (Results.Count == 0) throw new SourceException("请先勾选需要导入的来源。");
            }
            else
            {
                if (string.IsNullOrWhiteSpace(NameBox.Text) || NameBox.Text.Any(char.IsControl)) throw new SourceException("请输入有效的搜索源名称。");
                Result = original with { Name = NameBox.Text.Trim(), Endpoint = Links.Endpoint(EndpointBox.Text).AbsoluteUri, ApiKey = KeyBox.Password.Trim(), Enabled = EnabledBox.IsChecked == true };
                Results = [Result];
            }
            DialogResult = true;
        }
        catch (SourceException ex) { ErrorText.Text = ex.Message; }
    }
}
