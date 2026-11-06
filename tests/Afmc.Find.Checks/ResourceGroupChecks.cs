using Afmc.Find.Core;

internal static class ResourceGroupChecks
{
    public static void Run()
    {
        var passed = 0;
        void Check(bool ok, string name)
        {
            if (!ok) throw new InvalidOperationException(name);
            passed++;
            Console.WriteLine("PASS " + name);
        }

        SearchResult Row(string title, int id = 1, string category = "电影") => new()
        {
            Title = title,
            Source = "Fixture " + id,
            Category = category,
            Hash = id.ToString("x40"),
            Magnet = "magnet:?xt=urn:btih:" + id.ToString("x40"),
            Size = id * 1024L * 1024,
            Seeders = id,
            Published = DateTimeOffset.Parse("2026-01-01T00:00:00Z").AddDays(id),
            DetailsUrl = "https://fixture.invalid/resource/" + id,
            MetadataNote = "Original metadata " + id
        };
        int Count(params SearchResult[] rows) => ResourceGroups.Build(rows).Count;

        var first = Row("Far Horizon 2025 1080p H.264", 1);
        var second = Row("Far.Horizon.2025.2160p.H.265", 2);
        var third = Row("Other Journey 2025 1080p", 3);
        var groups = ResourceGroups.Build([first, third, second]);
        Check(groups.Count == 2 && groups[0].Versions.Count == 2, "explicit movie title and year group resolution variants");
        Check(ReferenceEquals(groups[0].Representative, first) && ReferenceEquals(groups[1].Representative, third), "group representatives preserve first occurrence ranking");
        Check(ReferenceEquals(groups[0].Versions[0], first) && ReferenceEquals(groups[0].Versions[1], second), "version grouping preserves original record references and order");
        Check(groups.SelectMany(g => g.Versions).Count() == 3 && groups[0].Versions[1].Magnet == second.Magnet &&
              groups[0].Versions[1].MetadataNote == second.MetadataNote && groups[0].Versions[1].Size == second.Size,
              "grouping never replaces per-version magnet metadata or size");
        Check(Count(first, first with { Title = "Far Horizon 2024 1080p" }) == 2, "same title with different explicit years stays separate");
        Check(Count(first with { Title = "Far Horizon 1080p" }, second with { Title = "Far Horizon 2160p" }) == 2, "missing year cannot be inferred from publication date");
        Check(Count(first, second with { MetadataConflict = true }) == 2, "conflicting source metadata is not collapsed");
        Check(Count(first, second with { ShareUrl = "https://pan.quark.cn/s/fixture" }) == 2, "cloud shares remain separate from BT versions");
        Check(Count(first, second with { Category = "动漫" }) == 2, "movie and animation categories are not crossed");
        Check(Count(first, second with { Category = "音乐" }) == 2, "same named music is not collapsed into a movie");
        Check(Count(first with { Category = "书籍" }, second with { Category = "书籍" }) == 2, "book editions without a reliable book identity stay separate");
        Check(Count(first with { Category = "软件" }, second with { Category = "软件" }) == 2, "software builds without a reliable software identity stay separate");
        Check(Count(Row("Far Horizon 2025 S01E01 1080p"), Row("Far Horizon 2025 S01E02 1080p", 2)) == 2, "different episodes never collapse together");
        Check(Count(Row("Far Horizon 2025 S01E01 1080p"), Row("Far Horizon 2025 S01E01 2160p", 2)) == 2, "even same-episode versions stay outside film grouping");
        Check(Count(Row("Far Horizon 2025 Part 1 1080p"), Row("Far Horizon 2025 Part 2 1080p", 2)) == 2, "separate movie parts after the year never collapse into versions");
        Check(Count(Row("Sky River 2025 [01] 1080p", category:"动漫"), Row("Sky River 2025 [02] 1080p", 2, "动漫")) == 2, "numeric anime episode labels remain individually visible");
        Check(Count(Row("星河旅人 (2025) - 01 [1080p]", category:"动漫"), Row("星河旅人 (2025) - 02 [1080p]", 2, "动漫")) == 2,
              "bare anime episode numbers after the year never collapse together");
        Check(Count(Row("Sky River (2025) – 01v2 [1080p]", category:"动漫"), Row("Sky River (2025) — 02v2 [1080p]", 2, "动漫")) == 2,
              "anime episode revisions and alternate dashes remain separate");
        Check(Count(Row("Sky River (2025) - 01-12 [1080p]", category:"动漫"), Row("Sky River (2025) - 13-24 [1080p]", 2, "动漫")) == 2,
              "bare anime episode ranges after the year remain separate");
        Check(Count(Row("Sky River (2025) - 720p", category:"动漫"), Row("Sky River (2025) - 1080p", 2, "动漫")) == 1,
              "anime resolution suffixes are not mistaken for episode numbers");
        Check(Count(Row("远山 第一季 2025 1080p"), Row("远山 第一季 2025 2160p", 2)) == 2, "Chinese seasons are not inferred as films");
        Check(Count(Row("Far Horizon 2025 1080p", category:"TV"), Row("Far Horizon 2025 2160p", 2, "TV")) == 2, "explicit series category is rejected without season tokens");
        Check(Count(Row("Far Horizon Complete 2025 1080p"), Row("Far Horizon Complete 2025 2160p", 2)) == 2, "complete packs remain individually visible");
        Check(Count(Row("远山 合集 2025 1080p"), Row("远山 合集 2025 2160p", 2)) == 2, "Chinese collections remain individually visible");
        Check(Count(first, second with { Title = "Far Horizon 2025 Director's Cut 2160p" }) == 2, "director cut after the year remains separate from ordinary release");
        Check(Count(Row("Far Horizon Director's Cut 2025 1080p"), Row("Far Horizon Director's Cut 2025 2160p", 2)) == 1, "matching director cuts may group their resolution variants");
        Check(Count(first, second with { Title = "Far Horizon 2025 Extended 2160p" }) == 2, "extended release stays separate even when WorkKeys match");
        Check(Count(first, second with { Title = "Far Horizon 2025 Unrated 2160p" }) == 2, "unrated release stays separate even when WorkKeys match");
        Check(Count(first, second with { Title = "Far Horizon 2025 导演剪辑版 2160p" }) == 2, "Chinese cut marker after year is preserved");

        var bridge = Row("远山 · Far Horizon 2025 1080p", 4);
        var chinese = Row("远山 2025 720p", 5);
        Check(Count(bridge, second) == 1 && Count(bridge, chinese) == 1, "an explicit bilingual movie row can match either direct alias");
        var noTransitive = ResourceGroups.Build([first, bridge, chinese]);
        Check(noTransitive.Count == 2 && noTransitive[0].Versions.Count == 2 && ReferenceEquals(noTransitive[1].Representative, chinese),
              "alias union cannot transitively expand an established group");
        Check(Count(bridge, first, chinese) == 2, "all group members must retain a common identity when bridge arrives first");
        var ambiguous = ResourceGroups.Build([first, chinese, bridge, second]);
        Check(ambiguous.Count == 3 && ambiguous[0].Versions.Count == 2 && ambiguous[2].Versions.Count == 1,
              "a bridge matching two established groups remains an isolated row");
        Check(Count(Row("Far Horizon / Other Journey 2025 1080p"), third) == 2, "two Latin titles separated by slash are not treated as safe aliases");
        Check(Count(Row("远山 / 另一旅程 2025 1080p"), Row("另一旅程 2025 1080p", 2)) == 2, "two Chinese titles separated by slash are not treated as safe aliases");
        Check(Count(Row("Far Horizon 2024 2025 1080p"), first) == 2, "multiple years are ambiguous and remain separate");
        Check(Count(first with { Title="Unknown movie", CoverUrl="https://image.tmdb.org/fixture.jpg" },
                    second with { Title="Unknown movie", CoverUrl="https://image.tmdb.org/fixture.jpg" }) == 2,
              "matching title and cover alone never create a group");
        Check(Count(Row("Sky River 2025 1080p", category:"动漫"), Row("Sky River 2025 2160p", 2, "动漫")) == 1,
              "animation films with explicit identity group without crossing categories");
        Check(ResourceGroups.Build([]).Count == 0, "empty results yield no artificial group");
        var duplicate = ResourceGroups.Build([first, first]);
        Check(duplicate.Count == 1 && duplicate[0].Versions.Count == 2, "presentation grouping does not silently deduplicate caller input");
        var enumerations = 0;
        IEnumerable<SearchResult> Once()
        {
            if (++enumerations != 1) throw new InvalidOperationException("Source enumerated twice");
            yield return first; yield return second;
        }
        Check(ResourceGroups.Build(Once()).Single().Versions.Count == 2 && enumerations == 1, "streamed ranked input is consumed only once");
        Check(Count(first with { Title=new string('x',1025)+" 2025" }, second) == 2, "oversized untrusted titles remain ungrouped");
        Console.WriteLine($"RESOURCE GROUPS PASSED {passed}");
    }
}
