using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using HtmlAgilityPack;
using Luma.Core;

namespace Luma.Services;

public sealed class EtymologyService
{
    private static readonly HttpClient Client = new() { Timeout = TimeSpan.FromSeconds(25) };
    private readonly Store store;
    public EtymologyService(Store store) => this.store = store;

    public async Task<EtymologyEntry> Fetch(string raw, CancellationToken ct)
    {
        var word = DictionaryService.Normalize(raw);
        if (!DictionaryService.IsWord(word)) throw new InvalidOperationException("词源查询需要一个英文单词。");
        var url = "https://en.wiktionary.org/w/api.php?action=parse&prop=text%7Crevid&format=json&disableeditsection=1&page=" + Uri.EscapeDataString(word);
        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.UserAgent.ParseAdd("Luma/0.2 (Windows English learning dictionary)");
        using var response = await Client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct);
        if (!response.IsSuccessStatusCode) throw new InvalidOperationException("维基词典暂时无法访问（HTTP " + (int)response.StatusCode + "）。离线词根仍可使用。");
        if (response.Content.Headers.ContentLength > 4000000) throw new InvalidOperationException("该词条数据过大，请点击来源在浏览器中查看。");
        var rawJson = await response.Content.ReadAsStringAsync(ct);
        if (rawJson.Length > 4000000) throw new InvalidOperationException("该词条数据过大，请在浏览器中查看。");
        using var json = JsonDocument.Parse(rawJson);
        if (!json.RootElement.TryGetProperty("parse", out var parsed)) throw new InvalidOperationException("维基词典未找到该词条，离线内容已保留。");
        var html = parsed.GetProperty("text").GetProperty("*").GetString() ?? "";
        var text = ExtractEnglishEtymology(html);
        if (text.Length == 0) throw new InvalidOperationException("此词条暂未提供可提取的英语词源。可点击“查看原文”核对。");
        ct.ThrowIfCancellationRequested();
        var revision = parsed.TryGetProperty("revid", out var rev) ? rev.GetInt64() : 0;
        var result = new EtymologyEntry
        {
            Word = word.ToLowerInvariant(),
            Text = text,
            Revision = revision,
            SourceUrl = revision > 0 ? "https://en.wiktionary.org/w/index.php?oldid=" + revision + "#English" : WordUrl(word)
        };
        store.CacheEtymology(result);
        return result;
    }
    public static string WordUrl(string word) => "https://en.wiktionary.org/wiki/" + Uri.EscapeDataString(word) + "#English";

    public static string ExtractEnglishEtymology(string html)
    {
        var doc = new HtmlDocument(); doc.LoadHtml(html);
        var root = doc.DocumentNode.SelectSingleNode("//*[contains(concat(' ',normalize-space(@class),' '),' mw-parser-output ')]") ?? doc.DocumentNode;
        var blocks = new List<string>(); var paragraphs = new List<string>();
        var english = false; var collecting = false;
        void Flush()
        {
            if (paragraphs.Count > 0) { blocks.Add(string.Join("\n", paragraphs)); paragraphs.Clear(); }
        }
        foreach (var node in root.ChildNodes)
        {
            var heading = Regex.IsMatch(node.Name, "^h[2-6]$") ? node : node.ChildNodes.FirstOrDefault(x => Regex.IsMatch(x.Name, "^h[2-6]$"));
            if (heading != null)
            {
                Flush(); collecting = false;
                var label = CleanText(heading.InnerText);
                if (heading.Name == "h2") { english = label.Equals("English", StringComparison.OrdinalIgnoreCase); continue; }
                if (english && label.StartsWith("Etymology", StringComparison.OrdinalIgnoreCase)) collecting = true;
                continue;
            }
            if (!english || !collecting || node.Name is not ("p" or "ul" or "ol")) continue;
            var clone = node.CloneNode(true);
            foreach (var unwanted in clone.SelectNodes(".//sup[contains(@class,'reference')]|.//style|.//script")?.ToList() ?? new()) unwanted.Remove();
            var text = CleanText(clone.InnerText);
            if (text.Length > 0 && paragraphs.Count < 5) paragraphs.Add(text.Length > 2200 ? text[..2200] + "…" : text);
        }
        Flush();
        return string.Join("\n\n", blocks.Take(4).Select((block, i) => blocks.Count > 1 ? "词源 " + (i + 1) + "\n" + block : block));
    }
    private static string CleanText(string text) => Regex.Replace(HtmlEntity.DeEntitize(text).Replace('\u200e', ' ').Replace('\u200f', ' '), @"\s+", " ").Trim();
    public static string Friendly(Exception ex) => ex switch
    {
        OperationCanceledException => "词源查询已取消或超时，已有离线内容保持可用。",
        HttpRequestException => "无法连接维基词典，请检查网络。已有离线内容保持可用。",
        JsonException => "维基词典返回格式暂不兼容，可点击原文查看。",
        _ => ex.Message
    };
}
