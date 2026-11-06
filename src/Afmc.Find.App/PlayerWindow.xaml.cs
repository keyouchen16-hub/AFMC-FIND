using System.ComponentModel;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
using System.Windows.Interop;
using System.Windows.Shell;
using LibVLCSharp.Shared;
using MonoTorrent;

namespace Afmc.Find.App;

public sealed record PlaybackVideo(string Path, PlaybackFile File)
{
    public override string ToString() => Path;
}

public partial class PlayerWindow : Window
{
    private readonly List<string> onlineSubtitleFiles = [];
    private readonly string cacheRoot;
    private readonly Func<string, TorrentPlayback> torrentFactory;
    private readonly SemaphoreSlim releaseGate = new(1);
    private Task? closeTask;
    private CancellationTokenSource? cancellation;
    private CancellationTokenSource? requestCancellation;
    private Task operation = Task.CompletedTask;
    private TorrentPlayback? torrent;
    private LibVLC? vlc;
    private MediaPlayer? player;
    private Media? media;
    private PlaybackStreamInput? input;
    private readonly DispatcherTimer timer;
    private bool changingFiles, seeking, closing, closed, fullScreen;
    private string phase = "";
    private bool cacheCleanupPending;
    private long mediaGeneration;
    private Rect restoreBounds;
    private WindowState restoreState;
    private bool restoreTopmost;
    private ResizeMode restoreResizeMode;
    private Window? movingOverlay;
    private double movingOverlayOpacity;
    public bool IsFullscreen => fullScreen;
    [DllImport("user32.dll")] private static extern bool SetWindowPos(IntPtr hwnd, IntPtr after, int x, int y, int width, int height, uint flags);
    [DllImport("gdi32.dll")] private static extern IntPtr GetStockObject(int index);
    [DllImport("gdi32.dll")] private static extern uint SetBkColor(IntPtr dc, uint color);
    public MediaPlayer? NativePlayer => player;
    public TorrentPlayback? TorrentSession => torrent;
    public Task CurrentOperation => operation;

    public PlayerWindow(string profileDirectory, Func<string, TorrentPlayback>? torrentFactory = null)
    {
        InitializeComponent(); cacheRoot = Path.Combine(profileDirectory, "PlaybackCache");
        TorrentPlayback.RecoverCache(cacheRoot);
        this.torrentFactory = torrentFactory ?? (root => new TorrentPlayback(root));
        DialogGlass.Attach(this, false); ShowInTaskbar = true;
        Loaded += (_, _) => {
            ApplyVideoFrame();
            foreach (var control in new FrameworkElement[] { InputRow, FileRow }) {
                if (control.Parent is Panel parent && parent != OverlayTop) { parent.Children.Remove(control); OverlayTop.Children.Add(control); }
            }
        };
        timer = new DispatcherTimer(TimeSpan.FromMilliseconds(500), DispatcherPriority.Background, (_, _) => Tick(), Dispatcher);
        Closing += OnClosing;
        SourceInitialized += (_,_) => HwndSource.FromHwnd(new WindowInteropHelper(this).Handle)?.AddHook(PlayerWindowMessage);
        StateChanged += (_,_) =>
        {
            if (WindowState == WindowState.Maximized && !fullScreen)
            {
                WindowState = WindowState.Normal;
                ToggleFullscreen();
            }
        };
        MountControls(false);
    }

    public Task OpenAsync(string address) => OpenPreparedAsync(_ => Task.FromResult(address));

    public Task OpenCandidatesAsync(IReadOnlyList<PlaybackCandidate> candidates) =>
        OpenPreparedAsync(_ => Task.FromResult(""), candidates);
    public Task OpenWinnerAsync(PlaybackWinner prepared) => OpenPreparedAsync(_ => Task.FromResult(""), prepared: prepared);

    public string? PlayingVersion { get; private set; }
    public Task OpenPreparedAsync(Func<CancellationToken, Task<string>> prepare, IReadOnlyList<PlaybackCandidate>? candidates = null, PlaybackWinner? prepared = null)
    {
        if (closing) return prepared is null ? operation : prepared.Session.DisposeAsync().AsTask();
        requestCancellation?.Cancel();
        cancellation?.Cancel();
        requestCancellation?.Dispose(); requestCancellation = new();
        return operation = OpenAfterAsync(operation, prepare, requestCancellation.Token, candidates, prepared);
    }

    private async Task OpenAfterAsync(Task previous, Func<CancellationToken, Task<string>> prepare, CancellationToken token, IReadOnlyList<PlaybackCandidate>? candidates, PlaybackWinner? prepared)
    {
        try {
            await previous;
            if (!token.IsCancellationRequested) await LoadAsync(prepare, token, candidates, prepared);
        } finally {
            if(prepared is not null && !ReferenceEquals(torrent,prepared.Session)) await prepared.Session.DisposeAsync();
        }
    }
    private async Task LoadAsync(Func<CancellationToken, Task<string>> prepare, CancellationToken requestToken, IReadOnlyList<PlaybackCandidate>? candidates, PlaybackWinner? prepared)
    {
        SetBusy(true);
        try
        {
            await ReleaseAsync();
            PlayerIdleLogo.Visibility = Visibility.Collapsed;
            PlayingVersion = null;
            cancellation = CancellationTokenSource.CreateLinkedTokenSource(requestToken); var ct = cancellation.Token;
            ct.ThrowIfCancellationRequested();
            phase = "正在准备播放地址…"; PlayerStatus.Text = phase;
            var address = (await prepare(ct)).Trim();
            var magnet = address.StartsWith("magnet:?", StringComparison.OrdinalIgnoreCase);
            var localTorrent = File.Exists(address) && address.EndsWith(".torrent", StringComparison.OrdinalIgnoreCase);
            Uri? direct = null;
            if (magnet) _ = MagnetLink.Parse(address);
            else if (!localTorrent && candidates is null && prepared is null) direct = await PlaybackAddress.ValidateDirectAsync(address, ct);
            phase = "正在校验播放器组件…";
            var runtime = await PlayerRuntime.EnsureAsync(new Progress<string>(s => { phase = s; PlayerStatus.Text = s; }), ct);
            ct.ThrowIfCancellationRequested();
            if (vlc is null)
            {
                LibVLCSharp.Shared.Core.Initialize(runtime);
                vlc = new LibVLC("--no-video-title-show", "--no-metadata-network-access", "--no-lua", "--no-snapshot-preview");
                player = new MediaPlayer(vlc) { Volume = (int)VolumeBar.Value, EnableMouseInput = false, EnableKeyInput = false };
                VideoSurface.MediaPlayer = player;
                player.EncounteredError += (_, _) => PostPlayerState(() => { phase = "播放失败：资源可能无法连接或格式不受支持。可停止后换一个版本。"; PlayerStatus.Text = phase; });
                player.EndReached += (_, _) => PostPlayerState(() => { phase = "播放结束 · 点击停止可清理缓存"; PlayerStatus.Text = phase; PauseButton.Content = "已结束"; });
            }
            if (direct is not null)
            {
                changingFiles = true; VideoFiles.ItemsSource = null; FileRow.Visibility = Visibility.Collapsed; changingFiles = false;
                media = new Media(vlc, direct); media.AddOption(":network-caching=1500");
                StartMedia();
            }
            else
            {
                phase = "正在获取视频列表…"; PlayerStatus.Text = phase; Placeholder.Text = phase;
                if (candidates is not null || prepared is not null)
                {
                    var winner = prepared ?? await PlaybackRace.RunAsync(candidates!, () => torrentFactory(cacheRoot), vlc,
                        message => { phase = message; PlayerStatus.Text = message; Placeholder.Text = "正在检测资源是否可播…"; }, ct);
                    torrent = winner.Session;
                    await torrent.PromoteAsync(ct);
                    PlayingVersion = winner.Title;
                    VideoSurface.ToolTip = "实际播放版本：" + winner.Title;
                    PlayerStatus.ToolTip = "实际播放版本：" + winner.Title;
                    phase = "已选可播版本：" + winner.Title;
                }
                else
                {
                    torrent = torrentFactory(cacheRoot);
                    await torrent.OpenAsync(address, ct);
                }
                var choices = torrent.Videos.Select(f => new PlaybackVideo(f.Path, f)).ToArray();
                changingFiles = true; VideoFiles.ItemsSource = choices; FileRow.Visibility = choices.Length > 1 && !fullScreen ? Visibility.Visible : Visibility.Collapsed;
                VideoFiles.SelectedItem = choices.OrderByDescending(f => f.File.Length).First(); changingFiles = false;
                await SelectVideoAsync(((PlaybackVideo)VideoFiles.SelectedItem).File, ct);
            }
        }
        catch (OperationCanceledException) { await ReleaseAsync(); phase = "已停止，播放缓存已清理。"; }
        catch (Exception e) when (e is not OutOfMemoryException)
        {
            await ReleaseAsync(); phase = e is InvalidDataException or IOException or Afmc.Find.Core.SourceException ? e.Message : "无法播放此资源，请检查链接、网络或更换资源重试。";
        }
        finally { SetBusy(false); PlayerStatus.Text = phase; }
    }

    private async Task SelectVideoAsync(PlaybackFile file, CancellationToken ct)
    {
        phase = "正在缓冲 · 没有在线做种时需要等待，可点击停止";
        var stream = await torrent!.SelectAsync(file, ct);
        input = new PlaybackStreamInput(stream, ct);
        media = new Media(vlc!, input); PlaybackMedia.Configure(media, file.Path);
        StartMedia();
    }

    private void ToggleSourceClick(object sender, RoutedEventArgs e)
    {
        InputRow.Visibility = InputRow.IsVisible ? Visibility.Collapsed : Visibility.Visible;
        if (InputRow.IsVisible) AddressBox.Focus();
    }

    private long lastInteraction;
    private bool barShown = true;
    private bool keyboardNavigation;
    private void ApplyVideoFrame()
    {
        if (Content is Border { Child: DockPanel panel } frame) {
            panel.Children[0].Visibility = Visibility.Collapsed;
            frame.BorderThickness = new Thickness(0);
            frame.Background = System.Windows.Media.Brushes.Black;
        }
        WindowChrome.GetWindowChrome(this).CaptionHeight = 0;
        WindowChrome.GetWindowChrome(this).GlassFrameThickness = new Thickness(0);
        Background = System.Windows.Media.Brushes.Black;
        PlayerLayout.Margin = new Thickness(0);
        CanvasFrame.BorderThickness = new Thickness(0);
        CanvasFrame.CornerRadius = new CornerRadius(0);
        var handle = new WindowInteropHelper(this).Handle;
        if (handle != IntPtr.Zero) { var backdrop = 1; DwmSetWindowAttribute(handle,38,ref backdrop,4); var color = -2; DwmSetWindowAttribute(handle,34,ref color,4); var corner = 1; DwmSetWindowAttribute(handle,33,ref corner,4); }
    }
    [DllImport("dwmapi.dll")] private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);
    private void CloseOverlayPlayer(object sender, RoutedEventArgs e) => Close();
    private void MinimizePlayer(object sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;
    private void WindowDrag(object sender, MouseButtonEventArgs e)
    {
        if (e.OriginalSource is DependencyObject source) {
            while (source is not null) {
                if (source is Button) return;
                source = System.Windows.Media.VisualTreeHelper.GetParent(source);
            }
        }
        if (e.ClickCount == 2) ToggleFullscreen();
        else if (!fullScreen) {
            try { DragMove(); }
            finally { RestoreMovingOverlay(); }
        }
        e.Handled = true;
    }
    private void HideMovingOverlay()
    {
        // LibVLCSharp hosts WPF controls in a separate layered HWND. During the
        // native move loop it can trail the video HWND; keep only video visible.
        if (movingOverlay is not null || closing) return;
        if (Window.GetWindow(VideoOverlay) is { IsVisible: true } overlay && overlay != this) {
            movingOverlay = overlay;
            movingOverlayOpacity = overlay.Opacity;
            // Hiding the HWND itself cancels the native drag by changing activation.
            // Only suppress painting; retain the HWND, capture and its input loop.
            overlay.Opacity = 0;
        }
    }
    private void RestoreMovingOverlay()
    {
        var overlay = movingOverlay;
        movingOverlay = null;
        if (overlay is null) return;
        if (closing || !IsVisible || WindowState == WindowState.Minimized) { overlay.Opacity = movingOverlayOpacity; return; }
        VideoSurface.UpdateLayout();
        var origin = VideoSurface.PointToScreen(new Point(0, 0));
        var edge = VideoSurface.PointToScreen(new Point(VideoSurface.ActualWidth, VideoSurface.ActualHeight));
        // Position and reveal atomically, without stealing focus or changing Z order.
        SetWindowPos(new WindowInteropHelper(overlay).Handle, IntPtr.Zero, (int)Math.Round(origin.X), (int)Math.Round(origin.Y),
            (int)Math.Round(edge.X-origin.X), (int)Math.Round(edge.Y-origin.Y), 0x0014);
        overlay.Opacity = movingOverlayOpacity;
        WakeBar();
    }
    private void ShowBar(bool show)
    {
        if (barShown == show) return;
        barShown = show;
        FloatingBar.IsHitTestVisible = show;
        OverlayTop.IsHitTestVisible = show;
        OverlayTop.BeginAnimation(OpacityProperty, new System.Windows.Media.Animation.DoubleAnimation(show ? 1 : 0, TimeSpan.FromMilliseconds(160)));
        FloatingBar.BeginAnimation(OpacityProperty, new System.Windows.Media.Animation.DoubleAnimation(show ? 1 : 0, TimeSpan.FromMilliseconds(160)));
    }
    private void WakeBar()
    {
        lastInteraction = Environment.TickCount64;
        ShowBar(true);
    }
    private void OverlayMove(object sender, MouseEventArgs e) { keyboardNavigation = false; WakeBar(); }
    private void OverlayDown(object sender, MouseButtonEventArgs e)
    {
        if (FloatingBar.IsMouseOver || SubtitlePanel.IsMouseOver || OverlayTop.IsMouseOver) return;
        VideoOverlay.Focus();
        WakeBar();
        if (e.ClickCount == 2) { ToggleFullscreen(); e.Handled = true; }
    }
    private void MountControls(bool playing)
    {
        VideoOverlay.Background = new System.Windows.Media.SolidColorBrush(playing ? System.Windows.Media.Color.FromArgb(1,0,0,0) : System.Windows.Media.Color.FromRgb(8,10,14));
        Panel target = FloatingContent;
        foreach (FrameworkElement control in new FrameworkElement[] { Transport, PlayerStatus })
        {
            if (control.Parent == target) continue;
            ((Panel)control.Parent).Children.Remove(control);
            target.Children.Add(control);
        }
        PlayerStatus.Margin = new Thickness(0,3,0,0);
        SubtitlePanel.Visibility = Visibility.Collapsed;
        WakeBar();
    }
    private void BackTen(object sender, RoutedEventArgs e) => Skip(-10000);
    private void ForwardTen(object sender, RoutedEventArgs e) => Skip(10000);
    private void Skip(long amount)
    {
        if (player is { IsSeekable:true, Length: > 0 }) player.Time = Math.Clamp(player.Time + amount, 0, player.Length);
    }
    private void CloseSubtitles(object sender, RoutedEventArgs e) { SubtitlePanel.Visibility = Visibility.Collapsed; WakeBar(); }
    private void StartMedia()
    {
        Interlocked.Increment(ref mediaGeneration);
        if (!player!.Play(media!)) throw new IOException("播放器未能打开此视频，请更换资源。");
        InputRow.Visibility = Visibility.Collapsed;
        MountControls(true); VideoSurface.Visibility = Visibility.Visible; Placeholder.Visibility = Visibility.Collapsed; PlayerIdleLogo.Visibility = Visibility.Collapsed;
        phase = "等待可播分片…"; PauseButton.Content = "暂停"; PauseButton.IsEnabled = SubtitleButton.IsEnabled = true;
    }

    private async void FileChanged(object sender, SelectionChangedEventArgs e)
    {
        if (changingFiles || torrent is null || VideoFiles.SelectedItem is not PlaybackVideo choice || !operation.IsCompleted) return;
        requestCancellation?.Dispose(); requestCancellation = new();
        operation = ChangeVideoAsync(choice.File, requestCancellation.Token); await operation;
    }

    private async Task ChangeVideoAsync(PlaybackFile file, CancellationToken requestToken)
    {
        SetBusy(true);
        try
        {
            cancellation?.Cancel(); await StopMediaAsync(); cancellation?.Dispose(); cancellation = CancellationTokenSource.CreateLinkedTokenSource(requestToken);
            await SelectVideoAsync(file, cancellation.Token);
        }
        catch (OperationCanceledException) { await ReleaseAsync(); phase = "已停止。"; }
        catch (Exception e) when (e is not OutOfMemoryException) { await ReleaseAsync(); phase = "切换视频失败：" + (e is IOException ? e.Message : "请重新打开资源。"); }
        finally { SetBusy(false); PlayerStatus.Text = phase; }
    }

    private void SetBusy(bool busy)
    {
        LoadButton.IsEnabled = TorrentButton.IsEnabled = AddressBox.IsEnabled = VideoFiles.IsEnabled = !busy;
        StopButton.IsEnabled = busy || media is not null;
    }

    public Task StopAsync()
    {
        requestCancellation?.Cancel(); cancellation?.Cancel();
        return operation = StopCoreAsync(operation);
    }
    private async Task StopCoreAsync(Task previous)
    {
        SetBusy(true); StopButton.IsEnabled = false;
        await previous; await ReleaseAsync();
        phase = cacheCleanupPending ? "已停止。部分缓存仍被占用，下次打开播放器时重试清理。" : "已停止，播放缓存已清理。"; PlayerStatus.Text = phase; SetBusy(false);
    }

    private async Task StopMediaAsync()
    {
        Interlocked.Increment(ref mediaGeneration);
        if (player is not null) await Task.Run(() => player.Stop());
        foreach (var subtitle in onlineSubtitleFiles.ToArray())
        {
            try { File.Delete(subtitle); onlineSubtitleFiles.Remove(subtitle); } catch (IOException) { } catch (UnauthorizedAccessException) { }
        }
        media?.Dispose(); media = null; input?.Dispose(); input = null;
        PauseButton.IsEnabled = SeekBar.IsEnabled = SubtitleButton.IsEnabled = false;
        MountControls(false); VideoSurface.Visibility = Visibility.Visible; Placeholder.Visibility = Visibility.Visible; Placeholder.Text = ""; PlayerIdleLogo.Visibility = Visibility.Visible;
    }

    private void PostPlayerState(Action action)
    {
        var current = Interlocked.Read(ref mediaGeneration);
        Dispatcher.BeginInvoke(() => { if (current == mediaGeneration && !closing && media is not null && cancellation?.IsCancellationRequested == false) action(); });
    }

    private async Task ReleaseAsync()
    {
        await releaseGate.WaitAsync();
        try {
        cancellation?.Cancel(); await StopMediaAsync();
        if (torrent is not null) { await torrent.DisposeAsync(); cacheCleanupPending = !torrent.CacheRemoved; torrent = null; }
        cancellation?.Dispose(); cancellation = null;
        changingFiles = true; VideoFiles.ItemsSource = null; FileRow.Visibility = Visibility.Collapsed; changingFiles = false;
        SeekBar.Value = 0; TimeLabel.Text = "00:00 / 00:00";
        } finally { releaseGate.Release(); }
    }

    public Task ClosePlayerAsync() => closeTask ??= CloseCoreAsync();
    private async Task CloseCoreAsync()
    {
        closing = true;
        timer.Stop();
        // Let WPF finish the cancelled Closing event, even if cleanup completes synchronously.
        await Dispatcher.Yield(DispatcherPriority.Normal);
        await StopAsync();
        VideoSurface.MediaPlayer = null; player?.Dispose(); player = null; vlc?.Dispose(); vlc = null;
        requestCancellation?.Dispose(); requestCancellation = null;
        closed = true; Close();
    }
    private async void OnClosing(object? sender, CancelEventArgs e) { if (closed) return; e.Cancel = true; await ClosePlayerAsync(); }
    private async void LoadClick(object sender, RoutedEventArgs e) => await OpenAsync(AddressBox.Text);
    private async void AddressKeyDown(object sender, KeyEventArgs e) { if (e.Key == Key.Enter) { e.Handled = true; await OpenAsync(AddressBox.Text); } }
    private async void TorrentClick(object sender, RoutedEventArgs e)
    {
        var dialog = new Microsoft.Win32.OpenFileDialog { Filter = "种子文件 (*.torrent)|*.torrent", CheckFileExists = true };
        if (dialog.ShowDialog(this) == true) await OpenAsync(dialog.FileName);
    }
    private async void StopClick(object sender, RoutedEventArgs e) => await StopAsync();
    private void PauseClick(object sender, RoutedEventArgs e) { if (player is null) return; player.Pause(); PauseButton.Content = player.IsPlaying ? "暂停" : "继续"; }
    private void VolumeChanged(object sender, RoutedPropertyChangedEventArgs<double> e) { if (player is not null) player.Volume = (int)e.NewValue; }
    private void SeekDown(object sender, MouseButtonEventArgs e) => seeking = true;
    private void SeekUp(object sender, MouseButtonEventArgs e) { Seek(); seeking = false; }
    private void SeekKeyUp(object sender, KeyEventArgs e) => Seek();
    private void Seek() { if (player is { IsSeekable: true, Length: > 0 }) player.Time = (long)(player.Length * SeekBar.Value / 1000); }
    private void SubtitleClick(object sender, RoutedEventArgs e)
    {
        if (SubtitlePanel.IsVisible) { CloseSubtitles(this,new()); return; }
        SubtitleChoices.Children.Clear();
        SubtitlePanel.Visibility = Visibility.Visible;
        foreach (var track in player!.SpuDescription)
        {
            var id = track.Id;
            var button = new Button { Content = (id == player.Spu ? "✓ " : "") + (id == -1 ? "关闭字幕" : track.Name) };
            button.Click += (_,_) => { player.SetSpu(id); CloseSubtitles(this,new()); };
            SubtitleChoices.Children.Add(button);
        }
        var open = new Button { Content = "加载本地字幕…" }; open.Click += (_, _) => {
            var dialog = new Microsoft.Win32.OpenFileDialog { Filter = "字幕 (*.srt;*.ass;*.ssa;*.vtt)|*.srt;*.ass;*.ssa;*.vtt" };
            if (dialog.ShowDialog(this) == true) player.AddSlave(MediaSlaveType.Subtitle, new Uri(dialog.FileName).AbsoluteUri, true);
        }; SubtitleChoices.Children.Add(open);
        var online = new Button { Content = "搜索在线字幕…" };
        online.Click += (_, _) => {
            var seed = VideoFiles.SelectedItem is PlaybackVideo v ? Path.GetFileNameWithoutExtension(v.Path) : "";
            var generation = mediaGeneration;
            var dialog = new OnlineSubtitleWindow(seed, path => {
                if (generation != mediaGeneration || player is null || media is null) return false;
                if (!player.AddSlave(MediaSlaveType.Subtitle, new Uri(path).AbsoluteUri, true)) return false;
                onlineSubtitleFiles.Add(path); return true;
            }) { Owner = this };
            dialog.ShowDialog(); CloseSubtitles(this,new());
        };
        SubtitleChoices.Children.Add(online);
    }
    private void Tick()
    {
        if (media is not null)
            ShowBar(player?.IsPlaying != true || seeking || FloatingBar.IsMouseOver || OverlayTop.IsMouseOver || InputRow.IsVisible || (keyboardNavigation && (FloatingBar.IsKeyboardFocusWithin || OverlayTop.IsKeyboardFocusWithin)) || SubtitlePanel.IsVisible || Environment.TickCount64-lastInteraction < 2400);
        if (player is null || media is null) { if (phase.Length > 0) PlayerStatus.Text = phase + (torrent?.Status is {} pending ? (pending.Peers == 0 ? $" · 正在寻找资源节点（{pending.Nodes} 个路由节点）" : $" · 已连接 {pending.Peers} 个资源节点，等待对方提供文件信息") : ""); return; }
        SeekBar.IsEnabled = player.IsSeekable && player.Length > 0;
        if (!seeking && player.Length > 0) SeekBar.Value = Math.Clamp(player.Time * 1000d / player.Length, 0, 1000);
        static string Format(long ms) => TimeSpan.FromMilliseconds(Math.Max(0, ms)).ToString(ms >= 3600000 ? @"hh\:mm\:ss" : @"mm\:ss");
        TimeLabel.Text = Format(player.Time) + " / " + Format(player.Length);
        PauseButton.Content = player.IsPlaying ? "暂停" : "继续";
        var state = player.State == VLCState.Playing && player.Time > 0 ? "正在播放" : player.State == VLCState.Paused ? "已暂停" : phase;
        PlayerStatus.Text = state + (PlayingVersion is {} version ? " · 已选版本：" + (version.Length > 36 ? version[..36] + "…" : version) : "")
            + (torrent?.Status is {} m ? $" · 已连接 {m.Peers} 个节点 · 下载 {m.Progress:0.0}% · {m.Rate / 1024d:0} KB/s · 关闭后停止并清理缓存" : "");
    }
    private IntPtr PlayerWindowMessage(IntPtr hwnd, int message, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        // LibVLCSharp hosts video in a Win32 STATIC child. WPF Background does
        // not paint that child; it otherwise exposes the system white brush
        // while the separate controls window is transparent during a drag.
        if (message == 0x0138 && VideoSurface.Template?.FindName("PART_PlayerHost",VideoSurface) is HwndHost host && lParam==host.Handle) {
            SetBkColor(wParam,0); handled=true; return GetStockObject(4); // WM_CTLCOLORSTATIC / BLACK_BRUSH, system-owned
        }
        if (message == 0x0231) HideMovingOverlay(); // WM_ENTERSIZEMOVE
        else if (message == 0x0232) RestoreMovingOverlay(); // WM_EXITSIZEMOVE
        const int systemCommand = 0x0112;
        const long maximize = 0xF030;
        if (message == systemCommand && (wParam.ToInt64() & 0xFFF0) == maximize)
        {
            handled = true;
            if (!fullScreen) Dispatcher.BeginInvoke(ToggleFullscreen);
        }
        return IntPtr.Zero;
    }
    private void FullscreenClick(object sender, RoutedEventArgs e) => ToggleFullscreen();
    private void ToggleFullscreen()
    {
        fullScreen = !fullScreen;
        if (fullScreen)
        {
            restoreBounds = new Rect(Left, Top, Width, Height); restoreState = WindowState;
            restoreTopmost = Topmost; restoreResizeMode = ResizeMode;
            WindowState = WindowState.Normal; ResizeMode = ResizeMode.NoResize; Topmost = true;
            var handle = new WindowInteropHelper(this).Handle;
            var bounds = System.Windows.Forms.Screen.FromHandle(handle).Bounds;
            WindowChrome.GetWindowChrome(this).CaptionHeight = 0;
            WindowChrome.GetWindowChrome(this).ResizeBorderThickness = new Thickness(0);
            SetWindowPos(handle, new IntPtr(-1), bounds.X, bounds.Y, bounds.Width, bounds.Height, 0x0040);
        }
        else {
            Topmost = restoreTopmost; ResizeMode = restoreResizeMode;
            WindowChrome.GetWindowChrome(this).CaptionHeight = 34;
            WindowChrome.GetWindowChrome(this).ResizeBorderThickness = new Thickness(5);
            WindowState = WindowState.Normal; Left = restoreBounds.Left; Top = restoreBounds.Top; Width = restoreBounds.Width; Height = restoreBounds.Height; WindowState = restoreState;
        }
        if (Content is Border { Child: DockPanel panel } && panel.Children[0] is FrameworkElement caption)
        {
            caption.Visibility = fullScreen ? Visibility.Collapsed : Visibility.Visible;
            caption.IsHitTestVisible = !fullScreen;
        }
        InputRow.Visibility = Visibility.Collapsed;
        FileRow.Visibility = !fullScreen && VideoFiles.Items.Count > 1 ? Visibility.Visible : Visibility.Collapsed;
        PlayerLayout.Margin = fullScreen ? new Thickness(0) : new Thickness(12,4,12,8);
        CanvasFrame.BorderThickness = new Thickness(fullScreen ? 0 : 1);
        CanvasFrame.CornerRadius = new CornerRadius(fullScreen ? 0 : 8);
        if (Content is Border frame) frame.BorderThickness = new Thickness(fullScreen ? 0 : 1);
        Transport.Visibility = Visibility.Visible; WakeBar();
        PlayerStatus.Visibility = fullScreen ? Visibility.Collapsed : Visibility.Visible;
        SourceToggle.Visibility = fullScreen ? Visibility.Collapsed : Visibility.Visible;
        if (!fullScreen && media is not null) InputRow.Visibility = Visibility.Collapsed;
        FullscreenButton.Content = fullScreen ? "退出全屏" : "全屏";
        ApplyVideoFrame();
    }
    private void PlayerKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Tab) { keyboardNavigation = true; WakeBar(); }
        if (e.Key == Key.Escape && SubtitlePanel.IsVisible) { CloseSubtitles(this,new()); e.Handled=true; }
        else if (e.Key == Key.Escape && fullScreen) { ToggleFullscreen(); e.Handled = true; }
        else if (e.Key == Key.F11) { ToggleFullscreen(); e.Handled = true; }
        else if (e.Key == Key.Space && Keyboard.FocusedElement is not TextBox && PauseButton.IsEnabled) { PauseClick(this, new()); e.Handled = true; }
    }
}
