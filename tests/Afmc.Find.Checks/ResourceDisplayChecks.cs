using System.Text.Json;
using Afmc.Find.Core;

internal static class ResourceDisplayChecks
{
    public static void Run()
    {
        var passed=0;
        void Check(bool ok,string name){if(!ok)throw new InvalidOperationException(name);passed++;Console.WriteLine("PASS "+name);}
        var movie=new SearchResult{Title="Far Horizon 2025 1080p Dual Audio.mkv",Category="电影",Source="Fixture",Hash=new string('a',40),Seeders=90000};
        Check(SearchQuality.Category(movie)=="影视" && ResourceDisplay.IsVideo(movie),"dual audio film retains its video action");
        Check(ResourceDisplay.IsVideo(movie with {Category=""}),"video extension outranks generic audio keyword");
        Check(ResourceDisplay.IsVideo(movie with {Category="",Title="Far Horizon 2025 1080p Dual Audio"}),"dual audio release without extension retains video classification");
        Check(SearchQuality.Category(movie with {Category="",Title="Far Horizon audiobook.mp3"})=="书籍","audiobook remains a book");
        var music=movie with {Category="音乐",Title="远山 · 原声音乐集.flac"};
        var book=movie with {Category="书籍",Title="远山 · 视觉设定集.pdf"};
        Check(!ResourceDisplay.IsVideo(music) && ResourceDisplay.Name(music)==music.Title,"music subtitle is retained and not offered video playback");
        Check(ResourceDisplay.Name(book)==book.Title,"book subtitle is never shortened as release metadata");
        Check(ResourceDisplay.Name(movie)=="Far Horizon 2025","technical release suffix is hidden only in display name");
        var bridge=movie with {Title="远山 · Far Horizon 2025 1080p"};
        Check(ResourceDisplay.Name(bridge)=="远山 (2025)","explicit bilingual movie display retains the year");
        Check(ResourceDisplay.Name(movie with {Title="远山 · 山的另一边 2025 1080p"})=="远山 · 山的另一边 2025","ambiguous same-language movie subtitle remains visible");
        var health=new PlaybackHealth();var now=DateTimeOffset.UtcNow;
        Check(ResourceDisplay.PlaybackLabel(movie,health,now)=="未验证","large reported seed count is not playback proof");
        health.Record(movie,PlaybackEvidence.DataVerified,now);
        Check(ResourceDisplay.PlaybackLabel(movie,health,now)=="起播未确认","received verified data is distinct from decoded playback");
        health.Record(movie,PlaybackEvidence.Playable,now);
        Check(ResourceDisplay.PlaybackLabel(movie,health,now)=="近期起播成功","successful decode is disclosed as recent evidence");
        Check(ResourceDisplay.PlaybackLabel(movie,health,now.AddMinutes(31))=="未验证","expired success no longer claims playback verification");
        health.Record(movie,PlaybackEvidence.TimedOut,now);
        Check(ResourceDisplay.PlaybackLabel(movie,health,now)=="本次超时","timeout is not described as permanent unavailability");
        health.Record(movie,PlaybackEvidence.PlaybackFailed,now);
        Check(ResourceDisplay.PlaybackLabel(movie,health,now)=="本次失败","decode failure remains separate from download ability");
        health.Record(movie,PlaybackEvidence.DataReceived,now);
        Check(ResourceDisplay.PlaybackLabel(movie,health,now)=="首片未就绪","partial unverified data remains distinct from a decode error");
        Check(ResourceDisplay.PlaybackLabel(music,health,now)=="—" && ResourceDisplay.PlaybackLabel(movie with {ShareUrl="https://pan.quark.cn/s/example"},health,now)=="—","non-video and share links do not inherit a movie playback badge");
        var json=JsonSerializer.Serialize(movie with {PlaybackLabel="近期起播成功",VersionLabel="3 个版本"});
        Check(!json.Contains("PlaybackLabel")&&!json.Contains("VersionLabel")&&!json.Contains("ResultName"),"transient display evidence is not persisted into source or favorite data");
        Check(movie.Title.Contains("Dual Audio") && movie.Hash==new string('a',40),"display shortening does not change download identity or raw filename");
        Console.WriteLine($"RESOURCE DISPLAY PASSED {passed}");
    }
}
