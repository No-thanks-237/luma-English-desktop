using System;
using System.Collections.Generic;

namespace Luma.Core;

public sealed class Settings
{
    public string Trigger { get; set; } = "Alt";
    public int DwellMs { get; set; } = 450;
    public bool Enabled { get; set; } = true;
    public bool ShowBubble { get; set; } = true;
    public bool BubbleAutoHide { get; set; } = true;
    public bool BubblePositionSaved { get; set; }
    public bool StartAtLogin { get; set; }
    public bool ReduceMotion { get; set; }
    public bool ShowPhonetic { get; set; } = true;
    public bool ShowTranslation { get; set; } = true;
    public bool ShowDefinition { get; set; }
    public bool ShowRoots { get; set; } = true;
    public bool ShowExamples { get; set; } = true;
    public bool ShowUsage { get; set; } = true;
    public bool OcrFallback { get; set; } = true;
    public string Provider { get; set; } = "DeepSeek";
    public string BaseUrl { get; set; } = "https://api.deepseek.com";
    public string Model { get; set; } = "deepseek-flash";
    public string EncryptedApiKey { get; set; } = "";
    public string TargetLanguage { get; set; } = "简体中文";
    public string CaptureHotkey { get; set; } = "Ctrl+Alt+T";
    public string LookupHotkey { get; set; } = "Ctrl+Alt+W";
    public string PauseHotkey { get; set; } = "Ctrl+Alt+Space";
    public double BubbleX { get; set; } = -1;
    public double BubbleY { get; set; } = -1;
}

public sealed class WordEntry
{
    public string Word { get; set; } = "";
    public string Phonetic { get; set; } = "";
    public string Translation { get; set; } = "";
    public string Definition { get; set; } = "";
    public string Exchange { get; set; } = "";
    public string Tags { get; set; } = "";
    public string QueriedAs { get; set; } = "";
    public string Resolution { get; set; } = "原形";
    public bool Found { get; set; }
    public WordDetails? Details { get; set; }
    public RootInfo RootInfo { get; set; } = new();
}

public sealed class RootHint
{
    public string Form { get; set; } = "";
    public string Kind { get; set; } = "";
    public string Meaning { get; set; } = "";
    public string Origin { get; set; } = "";
    public List<string> Examples { get; set; } = new();
}

public sealed class RootInfo
{
    public List<RootHint> Hints { get; set; } = new();
    public EtymologyEntry? Online { get; set; }
}

public sealed class EtymologyEntry
{
    public string Word { get; set; } = "";
    public string Text { get; set; } = "";
    public string SourceUrl { get; set; } = "";
    public long Revision { get; set; }
    public DateTime FetchedAt { get; set; } = DateTime.Now;
    public string Source { get; set; } = "Wiktionary";
    public string License { get; set; } = "CC BY-SA 4.0";
}

public sealed class WordDetails
{
    public string Roots { get; set; } = "";
    public string Usage { get; set; } = "";
    public List<Example> Examples { get; set; } = new();
    public string Source { get; set; } = "AI 生成 · 请结合词典核对";
}

public sealed class Example
{
    public string English { get; set; } = "";
    public string Chinese { get; set; } = "";
}

public sealed class SavedWord
{
    public string Word { get; set; } = "";
    public string Translation { get; set; } = "";
    public string Note { get; set; } = "";
    public DateTime SavedAt { get; set; } = DateTime.Now;
}

public sealed class TranslationRecord
{
    public string Original { get; set; } = "";
    public string Translated { get; set; } = "";
    public string Provider { get; set; } = "";
    public DateTime At { get; set; } = DateTime.Now;
}

public sealed record CaptureResult(string Text, string Source, System.Drawing.Rectangle Bounds, float Confidence = 100);
