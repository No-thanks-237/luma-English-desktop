using System;
using System.IO;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using Microsoft.Data.Sqlite;
using Luma.Core;

namespace Luma.Services;

public sealed class DictionaryService
{
    private readonly string connectionString;
    private readonly Store store;
    public RootService Roots { get; }
    public DictionaryService(Store store)
    {
        this.store = store;
        Roots = new RootService(store);
        var path = Path.Combine(AppContext.BaseDirectory, "Assets", "dictionary.db");
        connectionString = new SqliteConnectionStringBuilder { DataSource = path, Mode = SqliteOpenMode.ReadOnly, Pooling = true }.ToString();
        if (!File.Exists(path)) throw new FileNotFoundException("离线词典缺失，请完整解压 Luma 文件夹。", path);
    }
    public static string Normalize(string raw) => raw.Trim().Trim('"', '\'', '“', '”', '‘', '’', '.', ',', '!', '?', ':', ';', '(', ')', '[', ']', '{', '}', '\r', '\n').Replace('’', '\'');
    public static bool IsWord(string raw) => Regex.IsMatch(raw, @"^[a-zA-Z]+(?:['-][a-zA-Z]+)*$") && raw.Length <= 64;
    public WordEntry Lookup(string raw)
    {
        var word = Normalize(raw);
        using var db = new SqliteConnection(connectionString);
        db.Open();
        var result = Find(db, word);
        if (result != null && (result.Translation.Contains("的过去") || result.Translation.Contains("的复数")))
        {
            var original = Regex.Match(result.Exchange, @"(?:^|/)0:([^/]+)");
            if (original.Success) result = Find(db, original.Groups[1].Value) ?? result;
        }
        if (result == null)
        {
            using var c = db.CreateCommand();
            c.CommandText = "SELECT lemma FROM forms WHERE form = $w COLLATE NOCASE LIMIT 1";
            c.Parameters.AddWithValue("$w", word);
            var lemma = c.ExecuteScalar() as string;
            if (lemma != null) result = Find(db, lemma);
        }
        result ??= new WordEntry { Word = word, Translation = "离线词典暂未收录这个词。可检查识别结果，或使用 AI 补充学习信息。", Found = false };
        result.QueriedAs = word;
        if (store.Details.TryGetValue(result.Word.ToLowerInvariant(), out var details)) result.Details = details;
        else result.Details = Curated(result.Word);
        result.RootInfo = Roots.Lookup(result.Word);
        return result;
    }
    private static WordEntry? Find(SqliteConnection db, string word)
    {
        using var cmd = db.CreateCommand();
        cmd.CommandText = "SELECT word,phonetic,translation,definition,exchange,tags FROM words WHERE word=$w COLLATE NOCASE LIMIT 1";
        cmd.Parameters.AddWithValue("$w", word);
        using var r = cmd.ExecuteReader();
        if (!r.Read()) return null;
        string Get(int i) => r.IsDBNull(i) ? "" : r.GetString(i).Replace("\\n", "\n");
        return new WordEntry { Word = Get(0), Phonetic = Get(1), Translation = Get(2), Definition = Get(3), Exchange = Get(4), Tags = Get(5), Found = true };
    }
    public WordEntry? CorrectOcr(string raw)
    {
        var word = Normalize(raw).ToLowerInvariant();
        if (word.Length < 7 || !IsWord(word)) return null;
        using var db = new SqliteConnection(connectionString); db.Open(); using var cmd = db.CreateCommand();
        cmd.CommandText = "SELECT word FROM words WHERE word LIKE $prefix AND length(word) BETWEEN $min AND $max LIMIT 1000";
        cmd.Parameters.AddWithValue("$prefix", word[..2] + "%" + word[^1]); cmd.Parameters.AddWithValue("$min", word.Length - 2); cmd.Parameters.AddWithValue("$max", word.Length + 2);
        var candidates = new List<(string word, int distance)>(); using (var reader = cmd.ExecuteReader())
        {
            while (reader.Read()) { var candidate = reader.GetString(0); if (!IsWord(candidate)) continue; var distance = Distance(word, candidate.ToLowerInvariant()); if (distance <= (word.Length >= 10 ? 2 : 1)) candidates.Add((candidate, distance)); }
        }
        var ordered = candidates.OrderBy(x => x.distance).ToList();
        if (ordered.Count == 0 || (ordered.Count > 1 && ordered[0].distance == ordered[1].distance)) return null;
        var result = Lookup(ordered[0].word); result.QueriedAs = raw; result.Resolution = "OCR 校正"; return result;
    }
    private static int Distance(string a, string b)
    {
        var previous = Enumerable.Range(0, b.Length + 1).ToArray();
        for (var i = 1; i <= a.Length; i++) { var next = new int[b.Length + 1]; next[0] = i; for (var j = 1; j <= b.Length; j++) next[j] = Math.Min(Math.Min(previous[j] + 1, next[j - 1] + 1), previous[j - 1] + (a[i - 1] == b[j - 1] ? 0 : 1)); previous = next; }
        return previous[b.Length];
    }
    private static WordDetails? Curated(string word) => word.ToLowerInvariant() switch
    {
        "philosophy" => new WordDetails
        {
            Roots = "philo-（爱，源自希腊语）+ sophia（智慧）。philosophy 的词源含义是“爱智慧”；现代词义不宜仅由词根机械推导。",
            Usage = "the philosophy of science：科学哲学\nmy philosophy of life：我的人生观\n也可指一套做事原则，如 a design philosophy（设计理念）。",
            Source = "内置学习笔记 · 例句为原创",
            Examples = new() { new() { English = "Her philosophy is to learn a little every day.", Chinese = "她的理念是每天学一点。" }, new() { English = "He became interested in the philosophy of science.", Chinese = "他开始对科学哲学产生兴趣。" } }
        },
        "serendipity" => new WordDetails
        {
            Roots = "源自 Serendip（斯里兰卡的旧称）与故事 The Three Princes of Serendip。这个词不适合机械拆成常见前缀和词根。",
            Usage = "通常为不可数名词，表示偶然发现美好或有价值事物的机缘。可说 by serendipity（机缘巧合地）。",
            Source = "内置学习笔记 · 例句为原创",
            Examples = new() { new() { English = "Finding that little bookshop was pure serendipity.", Chinese = "发现那家小书店纯属美好的巧合。" } }
        },
        _ => null
    };
}
