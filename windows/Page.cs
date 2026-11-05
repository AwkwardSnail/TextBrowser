namespace TextBrowser;

/// <summary>A piece of page text. Tags are space-separated styles (h, b, i, code, dim, linknum);
/// Link is the link number it belongs to, or 0.</summary>
public sealed record TextRun(string Text, string Tags, int Link);

/// <summary>A page reduced to text: styled runs plus its links (link N is Links[N - 1]).</summary>
public sealed class Page
{
    public required string Url { get; init; }
    public string Title { get; init; } = "";
    public List<TextRun> Runs { get; init; } = [];
    public List<string> Links { get; init; } = [];

    /// <summary>A page built locally, for errors and other notices.</summary>
    public static Page Message(string url, string title, params string[] lines)
    {
        var runs = new List<TextRun> { new(title + "\n\n", "h", 0) };
        runs.AddRange(lines.Select(line => new TextRun(line + "\n\n", "", 0)));
        return new Page { Url = url, Title = title, Runs = runs };
    }
}
