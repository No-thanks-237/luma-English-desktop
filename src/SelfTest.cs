using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Luma.Core;
using Luma.Services;

namespace Luma;

internal static class SelfTest
{
    public static int Online(string folder)
    {
        Directory.CreateDirectory(folder);
        var store = new Store(Path.Combine(folder, "data"));
        var service = new EtymologyService(store);
        using var ct = new CancellationTokenSource(TimeSpan.FromSeconds(55));
        var results = new List<object>(); var failures = 0;
        foreach (var word in new[] { "happiness", "philosophy" })
        {
            try
            {
                var entry = service.Fetch(word, ct.Token).GetAwaiter().GetResult();
                if (entry.Text.Length == 0 || entry.Revision <= 0 || !entry.SourceUrl.Contains("oldid=")) throw new Exception("Missing source text or revision");
                if (word == "happiness" && (!entry.Text.Contains("happy") || !entry.Text.Contains("ness"))) throw new Exception("Etymology mismatch");
                if (new Store(store.Root).Etymologies[word].Text != entry.Text) throw new Exception("Cache roundtrip failed");
                results.Add(new { word, passed = true, source = entry.SourceUrl, characters = entry.Text.Length });
            }
            catch (Exception ex) { failures++; results.Add(new { word, passed = false, error = ex.Message }); }
        }
        File.WriteAllText(Path.Combine(folder, "online-test-results.json"), JsonSerializer.Serialize(new { at = DateTimeOffset.Now, passed = 2 - failures, failed = failures, tests = results }, Store.Json));
        return failures == 0 ? 0 : 1;
    }
    public static int Probe(string folder)
    {
        Directory.CreateDirectory(folder); using var service = new CaptureService { DiagnosticFolder = folder }; Native.GetCursorPos(out var p);
        var result = service.WordAt(p, true, CancellationToken.None).GetAwaiter().GetResult(); File.WriteAllText(Path.Combine(folder, "probe-result.json"), JsonSerializer.Serialize(result, Store.Json)); return result == null ? 1 : 0;
    }
    public static int Run(string? folder)
    {
        folder ??= Path.Combine(Path.GetTempPath(), "Luma-test-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(folder);
        var results = new List<object>(); var failed = 0;
        void Check(string name, Action action)
        {
            try { action(); results.Add(new { name, passed = true, detail = "OK" }); }
            catch (Exception ex) { failed++; results.Add(new { name, passed = false, detail = ex.ToString() }); }
        }
        void Assert(bool condition, string message) { if (!condition) throw new Exception(message); }
        var store = new Store(Path.Combine(folder, "data")); var dictionary = new DictionaryService(store);
        Check("Dock geometry / four edges / high DPI",()=>{
            foreach(var scale in new[]{1.0,1.5,2.0})
            {
                var area=new Rectangle(0,0,(int)(1200*scale),(int)(800*scale));var size=(int)(92*scale);
                var cases=new[]{(new Rectangle(2,(int)(300*scale),size,size),DockEdge.Left),(new Rectangle(area.Right-size-2,(int)(300*scale),size,size),DockEdge.Right),(new Rectangle((int)(400*scale),2,size,size),DockEdge.Top),(new Rectangle((int)(400*scale),area.Bottom-size-2,size,size),DockEdge.Bottom)};
                foreach(var item in cases)Assert(BubbleGeometry.Snap(item.Item1,area,scale).Edge==item.Item2,"Wrong edge at DPI "+scale);
                Assert(BubbleGeometry.Snap(new Rectangle((int)(400*scale),(int)(300*scale),size,size),area,scale).Edge==DockEdge.None,"Center must not dock");
            }
        });
        Check("Hidden orb keeps reveal strip and negative monitor coordinates",()=>{
            var area=new Rectangle(-1920,0,1920,1080);var left=BubbleGeometry.Snap(new Rectangle(-1918,300,92,92),area,1);var hidden=BubbleGeometry.Hidden(left,area,1);
            Assert(left.Edge==DockEdge.Left&&Math.Abs(hidden.X+82-area.Left-14)<.1,"Left reveal strip");
            var right=BubbleGeometry.Hidden(new DockPlacement(-90,300,DockEdge.Right),area,1);Assert(Math.Abs(area.Right-(right.X+10)-14)<.1,"Right reveal strip");
        });
        Check("Capsule expansion stays on screen",()=>{
            var area=new Rectangle(0,0,1200,800);var right=BubbleGeometry.Expand(new DockPlacement(20,300,DockEdge.None),area,1);Assert(right.OpensRight&&right.Width==312&&right.X==20,"Default rightward expansion");
            var left=BubbleGeometry.Expand(new DockPlacement(1110,300,DockEdge.Right),area,1);Assert(!left.OpensRight&&left.X>=-2&&left.X+312<=1202,"Right edge avoidance");
        });
        Check("Offline root data / explicit word association / no API key", () =>
        {
            Assert(dictionary.Roots.RootCount == 611 && dictionary.Roots.CoveredWords == 6738, "Source index counts");
            var roots = dictionary.Lookup("PHILOSOPHY").RootInfo.Hints;
            Assert(roots.Any(x => x.Form.Contains("phil")) && roots.Any(x => x.Meaning.Contains("wisdom")), "Philosophy roots");
            Assert(dictionary.Lookup("biology").RootInfo.Hints.Any(x => x.Form.Contains("logy")), "Biology suffix");
            Assert(dictionary.Roots.Lookup("xzzzznonword").Hints.Count == 0, "Must not invent roots");
        });
        Check("English etymology parser / language and section boundaries", () =>
        {
            const string html = "<div class='mw-parser-output'><h2>French</h2><h3>Etymology</h3><p>FRENCHTEXT</p><div class='mw-heading'><h2>English</h2></div><div class='mw-heading'><h3>Etymology 1</h3></div><style>STYLE</style><p>happy + -ness.<sup class='reference'>BADREF</sup></p><h3>Noun</h3><p>NOTETYMOLOGY</p><h3>Etymology 2</h3><p>Another origin &amp; form.</p><h2>German</h2><h3>Etymology</h3><p>GERMANTEXT</p></div>";
            var text = EtymologyService.ExtractEnglishEtymology(html); Assert(text.Contains("happy + -ness.") && text.Contains("Another origin & form."), "Missing English etymology");
            foreach (var noise in new[] { "FRENCHTEXT", "GERMANTEXT", "NOTETYMOLOGY", "BADREF", "STYLE" }) Assert(!text.Contains(noise), "Wrong section or markup leaked");
        });
        Check("Independent etymology cache and reload", () =>
        {
            store.CacheEtymology(new EtymologyEntry { Word = "happiness", Text = "happy + -ness.", Revision = 123, SourceUrl = "https://en.wiktionary.org/w/index.php?oldid=123#English" });
            var reloaded = new Store(store.Root); Assert(reloaded.Etymologies["happiness"].Revision == 123, "Source metadata persistence");
            Assert(new DictionaryService(reloaded).Lookup("happiness").RootInfo.Online?.Text == "happy + -ness.", "Cached entry not attached to word");
        });
        Check("Offline dictionary / Unicode / case / punctuation", () =>
        {
            var entry = dictionary.Lookup("“Philosophy,”"); Assert(entry.Found && entry.Word == "philosophy" && entry.Translation.Contains("哲学"), "Dictionary lookup failed");
            Assert(dictionary.Lookup("serendipity").Found, "Second entry not found"); Assert(!dictionary.Lookup("xzzzznonword").Found, "Missing words should not be fabricated");
        });
        Check("Input normalization and valid words", () =>
        {
            Assert(DictionaryService.IsWord("can't"), "Apostrophe"); Assert(DictionaryService.IsWord("well-known"), "Hyphen"); Assert(!DictionaryService.IsWord("two words"), "Sentence rejected"); Assert(!DictionaryService.IsWord("https://test"), "URL rejected");
        });
        Check("Inflection index", () =>
        {
            Assert(dictionary.Lookup("went").Word == "go" && dictionary.Lookup("gave").Word == "give", "Inflected-word lookup");
            using var db = new Microsoft.Data.Sqlite.SqliteConnection("Data Source=" + Path.Combine(AppContext.BaseDirectory, "Assets", "dictionary.db") + ";Mode=ReadOnly"); db.Open(); using var cmd = db.CreateCommand(); cmd.CommandText = "SELECT lemma FROM forms WHERE form='went'"; Assert((string?)cmd.ExecuteScalar() == "go", "went -> go index");
        });
        Check("Conservative OCR dictionary correction", () =>
        {
            Assert(dictionary.CorrectOcr("Sereivdipity")?.Word == "serendipity", "OCR correction"); Assert(dictionary.CorrectOcr("abc") == null, "Short words must not be guessed");
        });
        Check("Saved words / notes / persistence", () =>
        {
            store.ToggleWord(dictionary.Lookup("philosophy")); store.Words[0].Note = "爱智慧，保持好奇"; store.SaveWords();
            var reloaded = new Store(store.Root); Assert(reloaded.IsSaved("PHILOSOPHY") && reloaded.Words[0].Note.Contains("好奇"), "Save/reload failed");
            store.ToggleWord(dictionary.Lookup("philosophy")); Assert(store.Words.Count == 0, "Toggle removes saved entry");
        });
        Check("Windows DPAPI / settings persistence", () =>
        {
            var encrypted = Store.Protect("test-only-secret"); Assert(encrypted != "test-only-secret" && Store.Unprotect(encrypted) == "test-only-secret", "DPAPI roundtrip"); store.Settings.EncryptedApiKey = encrypted; store.SaveSettings(); Assert(!File.ReadAllText(Path.Combine(store.Root, "settings.json")).Contains("test-only-secret"), "No plaintext key"); Assert(Store.Unprotect("not-valid") == "", "Corrupt key safely rejected");
        });
        Check("Hotkey syntax and validation", () =>
        {
            Assert(Hotkeys.Parse("Ctrl+Alt+T").key == 84, "T key"); Assert(Hotkeys.Parse("Ctrl+Alt+Space").key == 32, "Space key"); var rejected = false; try { Hotkeys.Parse("T"); } catch { rejected = true; }
            Assert(rejected, "Unmodified shortcut rejected");
        });
        Check("Endpoint normalization / transport constraints", () =>
        {
            Assert(AiService.Endpoint("https://example.com/v1/").AbsolutePath == "/v1/chat/completions", "v1 path"); Assert(AiService.Endpoint("https://example.com/chat/completions").AbsolutePath == "/chat/completions", "Full path");
            foreach (var invalid in new[] { "http://example.com", "https://key@example.com", "https://example.com?key=x" }) { var rejected = false; try { AiService.Endpoint(invalid); } catch { rejected = true; } Assert(rejected, "Invalid endpoint accepted"); }
        });
        using var capture = new CaptureService();
        Check("Local OCR on light text image", () =>
        {
            using var bmp = Fixture(false); bmp.Save(Path.Combine(folder, "ocr-light.png"), ImageFormat.Png); var text = capture.Recognize(bmp, CancellationToken.None).GetAwaiter().GetResult(); Assert(text.Contains("Curiosity", StringComparison.OrdinalIgnoreCase) && text.Contains("learning", StringComparison.OrdinalIgnoreCase), "OCR: " + text);
        });
        Check("Local OCR on dark text image", () =>
        {
            using var bmp = Fixture(true); bmp.Save(Path.Combine(folder, "ocr-dark.png"), ImageFormat.Png); var text = capture.Recognize(bmp, CancellationToken.None).GetAwaiter().GetResult(); Assert(text.Contains("Curiosity", StringComparison.OrdinalIgnoreCase) && text.Contains("learning", StringComparison.OrdinalIgnoreCase), "Dark OCR: " + text);
        });
        Check("OpenAI-compatible request / translation / JSON explanation / error", () => ApiCheck(store, Assert).GetAwaiter().GetResult());
        var output = new { at = DateTimeOffset.Now, passed = results.Count - failed, failed, tests = results };
        File.WriteAllText(Path.Combine(folder, "test-results.json"), JsonSerializer.Serialize(output, Store.Json)); return failed == 0 ? 0 : 1;
    }
    private static Bitmap Fixture(bool dark)
    {
        var bitmap = new Bitmap(1000, 170); using var g = Graphics.FromImage(bitmap); g.Clear(dark ? Color.FromArgb(24, 32, 30) : Color.White); g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAliasGridFit; using var font = new Font("Georgia", 30); using var brush = new SolidBrush(dark ? Color.White : Color.Black); g.DrawString("Curiosity makes learning possible.", font, brush, new PointF(25, 48)); return bitmap;
    }
    private static async Task ApiCheck(Store store, Action<bool, string> assert)
    {
        var listener = new TcpListener(IPAddress.Loopback, 0); listener.Start(); var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(20)); var payloads = new List<string>();
        var server = Task.Run(async () =>
        {
            for (var i = 0; i < 3; i++)
            {
                using var socket = await listener.AcceptTcpClientAsync(cts.Token); using var stream = socket.GetStream();
                using var buffer = new MemoryStream(); var one = new byte[1];
                while (true) { var n = await stream.ReadAsync(one, cts.Token); if (n == 0) throw new Exception("Incomplete HTTP headers"); buffer.WriteByte(one[0]); var b = buffer.ToArray(); if (b.Length >= 4 && b[^4] == 13 && b[^3] == 10 && b[^2] == 13 && b[^1] == 10) break; if (b.Length > 20000) throw new Exception("Headers too large"); }
                var header = Encoding.ASCII.GetString(buffer.ToArray()); var length = int.Parse(header.Split("\r\n").First(x => x.StartsWith("Content-Length:", StringComparison.OrdinalIgnoreCase)).Split(':')[1].Trim());
                var body = new byte[length]; await stream.ReadExactlyAsync(body, cts.Token); payloads.Add(Encoding.UTF8.GetString(body));
                assert(header.Contains("POST /v1/chat/completions"), "API route"); assert(header.Contains("Bearer test-only-secret"), "Authorization header");
                var result = i == 0 ? "好奇心让学习成为可能。" : JsonSerializer.Serialize(new WordDetails { Roots = "词源测试", Usage = "用法测试", Examples = new() { new() { English = "A test.", Chinese = "一个测试。" } } });
                var response = i == 2 ? "{}" : JsonSerializer.Serialize(new { choices = new[] { new { message = new { content = result } } } }); var bytes = Encoding.UTF8.GetBytes(response);
                var status = i == 2 ? "401 Unauthorized" : "200 OK"; var responseHeader = Encoding.ASCII.GetBytes($"HTTP/1.1 {status}\r\nContent-Type: application/json\r\nContent-Length: {bytes.Length}\r\nConnection: close\r\n\r\n"); await stream.WriteAsync(responseHeader, cts.Token); await stream.WriteAsync(bytes, cts.Token);
            }
        }, cts.Token);
        try
        {
            store.Settings.BaseUrl = $"http://127.0.0.1:{port}/v1"; store.Settings.Model = "mock-model"; var ai = new AiService(store);
            var translation = await ai.Translate("Curiosity makes learning possible.", cts.Token); assert(translation.Contains("好奇心"), "Translation response");
            var details = await ai.Explain("curiosity", cts.Token); assert(details.Roots == "词源测试" && store.Details.ContainsKey("curiosity"), "JSON explanation/cache");
            var rejected = false; try { await ai.Translate("unauthorized", cts.Token); } catch (InvalidOperationException ex) { rejected = ex.Message.Contains("密钥"); }
            assert(rejected, "401 user error");
            await server; using var doc = JsonDocument.Parse(payloads[0]); assert(doc.RootElement.GetProperty("model").GetString() == "mock-model", "Model parameter"); assert(doc.RootElement.GetProperty("messages")[1].GetProperty("content").GetString() == "Curiosity makes learning possible.", "Exact text sent");
            store.AddTranslation(new TranslationRecord { Original = "test", Translated = translation, Provider = "Mock" }); assert(new Store(store.Root).Translations.Count == 1, "Translation history persisted");
        }
        finally { listener.Stop(); cts.Cancel(); }
    }
}
