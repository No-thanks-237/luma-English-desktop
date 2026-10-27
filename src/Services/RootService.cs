using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.RegularExpressions;
using Luma.Core;

namespace Luma.Services;

/// <summary>
/// Uses the source's explicit example-word associations, never substring guesses.
/// These are relevant morphemes, not a claim to a complete word segmentation.
/// </summary>
public sealed class RootService
{
    private readonly Dictionary<string, List<RootHint>> index = new(StringComparer.OrdinalIgnoreCase);
    private readonly Store store;
    public int RootCount { get; }
    public int CoveredWords => index.Count;
    public const string SourceUrl = "https://github.com/skywind3000/ECDICT/blob/master/wordroot.txt";

    public RootService(Store store)
    {
        this.store = store;
        var path = Path.Combine(AppContext.BaseDirectory, "Assets", "wordroots.json");
        if (!File.Exists(path)) return;
        using var document = JsonDocument.Parse(File.ReadAllText(path));
        foreach (var property in document.RootElement.EnumerateObject())
        {
            var item = property.Value;
            string Get(string name) => item.TryGetProperty(name, out var value) ? value.GetString() ?? "" : "";
            var hint = new RootHint
            {
                Form = Regex.Replace(Get("root"), @"\d+(?=,|$)", ""),
                Kind = Get("class"),
                Meaning = Get("meaning"),
                Origin = Get("origin")
            };
            if (!item.TryGetProperty("example", out var examples)) continue;
            hint.Examples = examples.EnumerateArray().Select(x => Regex.Replace(x.GetString() ?? "", @"\d+$", ""))
                .Where(DictionaryService.IsWord).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
            foreach (var word in hint.Examples)
            {
                if (!index.TryGetValue(word, out var hints)) index[word] = hints = new();
                hints.Add(hint);
            }
            RootCount++;
        }
    }
    public RootInfo Lookup(string word)
    {
        index.TryGetValue(word, out var hints);
        store.Etymologies.TryGetValue(word.ToLowerInvariant(), out var online);
        return new RootInfo { Hints = hints?.OrderBy(x => x.Kind.Contains("prefix") ? 0 : x.Kind.Contains("suffix") ? 2 : 1).ToList() ?? new(), Online = online };
    }
    public static string KindLabel(string kind) => kind.Contains("prefix") ? "前缀" : kind.Contains("suffix") ? "后缀" : "词根";
    public static string OriginLabel(string origin) => origin switch
    {
        "Latin" => "拉丁语",
        "Greek" => "希腊语",
        "Old English" => "古英语",
        "Middle English" => "中古英语",
        "French" => "法语",
        "German" => "德语",
        "English" => "英语",
        _ => origin
    };
    // Small built-in Chinese glosses; the original dataset meanings remain visible.
    public static string ChineseGloss(string meaning) => meaning switch
    {
        "wise, wisdom" => "智慧、明智",
        "love, fondness" => "爱、喜爱",
        "love" => "爱",
        "without" => "没有、缺少",
        "not" => "不、非",
        "carry" => "搬运、携带",
        "between, among" => "在……之间",
        "full of" => "充满……的",
        "life, living, living things" => "生命、生物",
        "life" => "生命",
        "sound" => "声音",
        "light" => "光",
        "water" => "水",
        "earth" => "土地、地球",
        "air" => "空气",
        "fire" => "火",
        "time" => "时间",
        "before" => "在……之前",
        "after" => "在……之后",
        "again" => "再次",
        "under, below" => "在……之下",
        "with, together with" => "和、共同",
        "one" => "一",
        "two" => "二",
        "three" => "三",
        "many" => "多、许多",
        "small" => "小",
        "large" => "大",
        "new, recent" => "新的、最近的",
        "man, human" => "人、人类",
        "government, rule" => "治理、统治",
        "to view, to observe" => "看、观察",
        "to cut" => "切、割",
        "fasten, attach" => "固定、连接",
        "bind, tighten" => "束缚、收紧",
        "wing, fin" => "翅膀、鳍",
        "made of" => "由……制成",
        "study of, science of; written work; structure or principle" => "……学；著述；结构或原理",
        _ => ""
    };
}
