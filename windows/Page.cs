namespace TextBrowser;

/// <summary>A piece of page text. Tags are space-separated styles (h, b, i, code, dim, linknum);
/// Link is the link number it belongs to, or 0. A form control has empty Text and a Control.</summary>
public sealed record TextRun(string Text, string Tags, int Link, PageControl? Control = null);

/// <summary>A form control on the page. Kind is text, password, textarea, select, button,
/// checkbox or radio; Id finds the element again in the live page (its data-tb-id).</summary>
public sealed record PageControl(
    string Id, string Kind, string Value, string Placeholder, int Size,
    IReadOnlyList<string> Options, int Index, string Label, bool Checked);

/// <summary>A page reduced to text: styled runs plus its links (link N is Links[N - 1]).</summary>
public sealed class Page
{
    public required string Url { get; init; }
    public string Title { get; init; } = "";
    public List<TextRun> Runs { get; init; } = [];
    public List<string> Links { get; init; } = [];
    /// <summary>Element ids on the page and the index of the run where each one starts.</summary>
    public Dictionary<string, int> Anchors { get; init; } = [];

    /// <summary>A page built locally, for errors and other notices.</summary>
    public static Page Message(string url, string title, params string[] lines)
    {
        var runs = new List<TextRun> { new(title + "\n\n", "h", 0) };
        runs.AddRange(lines.Select(line => new TextRun(line + "\n\n", "", 0)));
        return new Page { Url = url, Title = title, Runs = runs };
    }
}

/// <summary>What the rendered page can ask the window to do.</summary>
public interface IPageActions
{
    void Follow(int link);
    void Describe(string status);       // show in the status bar while hovered or focused
    void EndDescribe();
    void Click(string id);
    void SetText(string id, string value);
    void Submit(string id, string value);
    void Select(string id, int index);
}
