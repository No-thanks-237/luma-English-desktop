using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Luma.Core;

public sealed class Store
{
    public string Root { get; }
    public Settings Settings { get; private set; }
    public List<SavedWord> Words { get; }
    public List<TranslationRecord> Translations { get; }
    public Dictionary<string, WordDetails> Details { get; }
    public Dictionary<string, EtymologyEntry> Etymologies { get; }
    public static readonly JsonSerializerOptions Json = new() { WriteIndented = true, PropertyNameCaseInsensitive = true };
    public event Action? Changed;
    public string? LoadWarning { get; private set; }
    public Store(string? root = null)
    {
        Root = root ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Luma");
        Directory.CreateDirectory(Root);
        Settings = Read("settings.json", new Settings());
        Words = Read("words.json", new List<SavedWord>());
        Translations = Read("translations.json", new List<TranslationRecord>());
        Details = Read("details.json", new Dictionary<string, WordDetails>());
        Etymologies = Read("etymologies.json", new Dictionary<string, EtymologyEntry>());
    }
    private T Read<T>(string name, T fallback)
    {
        var path = Path.Combine(Root, name);
        if (!File.Exists(path)) return fallback;
        try { return JsonSerializer.Deserialize<T>(File.ReadAllText(path), Json) ?? fallback; }
        catch (Exception ex) when (ex is JsonException or IOException)
        {
            LoadWarning = "一份本地数据未能读取，已保留原文件。";
            try { File.Copy(path, path + ".backup-" + DateTime.Now.ToString("yyyyMMddHHmmss"), false); } catch { }
            return fallback;
        }
    }
    public void Write<T>(string name, T value)
    {
        var path = Path.Combine(Root, name);
        var temp = path + ".tmp";
        File.WriteAllText(temp, JsonSerializer.Serialize(value, Json), new UTF8Encoding(false));
        File.Move(temp, path, true);
    }
    public void SaveSettings() { Write("settings.json", Settings); Changed?.Invoke(); }
    public void ReplaceSettings(Settings value) { Settings = JsonSerializer.Deserialize<Settings>(JsonSerializer.Serialize(value), Json)!; SaveSettings(); }
    public void SaveWords() { Write("words.json", Words); Changed?.Invoke(); }
    public bool IsSaved(string word) => Words.Any(x => x.Word.Equals(word, StringComparison.OrdinalIgnoreCase));
    public void ToggleWord(WordEntry entry)
    {
        var existing = Words.FirstOrDefault(x => x.Word.Equals(entry.Word, StringComparison.OrdinalIgnoreCase));
        if (existing != null) Words.Remove(existing);
        else Words.Insert(0, new SavedWord { Word = entry.Word, Translation = entry.Translation });
        SaveWords();
    }
    public void CacheDetails(string word, WordDetails details)
    {
        Details[word.ToLowerInvariant()] = details;
        Write("details.json", Details);
    }
    public void AddTranslation(TranslationRecord record)
    {
        Translations.Insert(0, record);
        if (Translations.Count > 100) Translations.RemoveRange(100, Translations.Count - 100);
        Write("translations.json", Translations);
        Changed?.Invoke();
    }
    public void CacheEtymology(EtymologyEntry entry)
    {
        Etymologies[entry.Word.ToLowerInvariant()] = entry;
        Write("etymologies.json", Etymologies);
    }
    public static string Protect(string value) => string.IsNullOrEmpty(value) ? "" : Convert.ToBase64String(ProtectedData.Protect(Encoding.UTF8.GetBytes(value), null, DataProtectionScope.CurrentUser));
    public static string Unprotect(string value)
    {
        if (string.IsNullOrEmpty(value)) return "";
        try { return Encoding.UTF8.GetString(ProtectedData.Unprotect(Convert.FromBase64String(value), null, DataProtectionScope.CurrentUser)); }
        catch { return ""; }
    }
}
