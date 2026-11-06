using System.Collections.Concurrent;
using System.IO;
using System.Net.Http;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Automation;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Afmc.Find.Core;

namespace Afmc.Find.App;

public sealed class ThumbnailView : Border
{
    public static readonly DependencyProperty UrlProperty=DependencyProperty.Register(nameof(Url),typeof(string),typeof(ThumbnailView),new PropertyMetadata(null,Changed));
    public string? Url {get=>(string?)GetValue(UrlProperty);set=>SetValue(UrlProperty,value);}
    private readonly Image picture=new() { Stretch=Stretch.UniformToFill };
    private static readonly ConcurrentDictionary<string,Task<BitmapSource?>> cache=new();
    private static readonly SemaphoreSlim gate=new(4);
    private static readonly HttpClient client=new(new HttpClientHandler {AllowAutoRedirect=false}) {Timeout=TimeSpan.FromSeconds(12)};
    private int revision;
    public ThumbnailView()
    {
        Background=new SolidColorBrush(Color.FromRgb(38,46,49));CornerRadius=new CornerRadius(5);ClipToBounds=true;
        Child=picture;Visibility=Visibility.Collapsed;
    }
    private static void Changed(DependencyObject obj,DependencyPropertyChangedEventArgs args)=>((ThumbnailView)obj).Refresh();
    private async void Refresh()
    {
        var current=++revision;picture.Source=null;Visibility=Visibility.Collapsed;
        AutomationProperties.SetName(picture,"");
        var url=Url;if(string.IsNullOrEmpty(url))return;
        var image=await (cache.Count<160?cache.GetOrAdd(url,Load):Load(url));
        if(current!=revision)return;
        picture.Source=image;Visibility=image is null?Visibility.Collapsed:Visibility.Visible;
        AutomationProperties.SetName(picture,image is null?"":"来源缩略图已加载");
    }
    public static async Task<BitmapSource?> Load(string url)
    {
        using var cancel=new CancellationTokenSource(TimeSpan.FromSeconds(15));var entered=false;
        try
        {
            if(ResultPresentation.SafeCover(url) is not {} safe || !Uri.TryCreate(safe,UriKind.Absolute,out var uri))return null;
            await gate.WaitAsync(cancel.Token);entered=true;
            for(var i=0;i<5;i++)
            {
                if(ResultPresentation.SafeCover(uri.AbsoluteUri) is null && !(uri.Scheme=="https" && uri.IsDefaultPort && uri.UserInfo.Length==0 && uri.Host.EndsWith(".archive.org",StringComparison.OrdinalIgnoreCase)))return null;
                using var response=await client.GetAsync(uri,HttpCompletionOption.ResponseHeadersRead,cancel.Token);
                if((int)response.StatusCode is >=300 and <400 && response.Headers.Location is {} next){uri=new Uri(uri,next);continue;}
                if(!response.IsSuccessStatusCode||response.Content.Headers.ContentLength>4*1024*1024||response.Content.Headers.ContentType?.MediaType?.StartsWith("image/",StringComparison.Ordinal)!=true)return null;
                await using var stream=await response.Content.ReadAsStreamAsync(cancel.Token);using var memory=new MemoryStream();var buffer=new byte[16384];
                int read;while((read=await stream.ReadAsync(buffer,cancel.Token))>0){if(memory.Length+read>4*1024*1024)return null;memory.Write(buffer,0,read);}
                memory.Position=0;var image=new BitmapImage();image.BeginInit();image.CacheOption=BitmapCacheOption.OnLoad;image.DecodePixelWidth=240;image.StreamSource=memory;image.EndInit();image.Freeze();return image;
            }
        }
        catch(Exception e) when(e is not OutOfMemoryException){return null;}
        finally{if(entered)gate.Release();}
        return null;
    }
}
