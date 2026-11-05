using System.Windows;
using System.Windows.Documents;
using System.Windows.Media;

namespace TextBrowser;

/// <summary>Turns a Page into a FlowDocument of terminal text with clickable links.</summary>
public static class PageRenderer
{
    public static FlowDocument Render(Page page, Action<int> follow, Action<int> hover, Action unhover)
    {
        var doc = new FlowDocument
        {
            TextAlignment = TextAlignment.Left,
            PagePadding = new Thickness(4, 2, 4, 12),
            Background = Brushes.Transparent,
        };
        doc.SetResourceReference(TextElement.ForegroundProperty, "Fg");
        var para = new Paragraph { Margin = new Thickness(0) };
        doc.Blocks.Add(para);

        Hyperlink? link = null;
        foreach (var run in page.Runs)
        {
            InlineCollection target;
            if (run.Link > 0)
            {
                // Consecutive runs of the same link (including its [n]) share one Hyperlink.
                if (link is null || (int)link.Tag != run.Link)
                {
                    link = MakeLink(run.Link, follow, hover, unhover);
                    para.Inlines.Add(link);
                }
                target = link.Inlines;
            }
            else
            {
                link = null;
                target = para.Inlines;
            }

            var parts = run.Text.Split('\n');
            for (var i = 0; i < parts.Length; i++)
            {
                if (i > 0)
                    target.Add(new LineBreak());
                if (parts[i].Length > 0)
                    target.Add(Styled(parts[i], run.Tags, run.Link > 0));
            }
        }
        return doc;
    }

    static Hyperlink MakeLink(int n, Action<int> follow, Action<int> hover, Action unhover)
    {
        var link = new Hyperlink { Tag = n };
        link.Click += (_, _) => follow(n);
        link.MouseEnter += (_, _) => hover(n);
        link.MouseLeave += (_, _) => unhover();
        link.GotKeyboardFocus += (_, _) =>
        {
            hover(n);
            link.BringIntoView();
        };
        link.LostKeyboardFocus += (_, _) => unhover();
        return link;
    }

    static Run Styled(string text, string tags, bool inLink)
    {
        var run = new Run(text);
        foreach (var tag in tags.Split(' ', StringSplitOptions.RemoveEmptyEntries))
        {
            switch (tag)
            {
                case "h":
                case "b":
                    run.FontWeight = FontWeights.Bold;
                    if (!inLink)
                        run.SetResourceReference(TextElement.ForegroundProperty, "Bright");
                    break;
                case "i":
                    run.FontStyle = FontStyles.Italic;
                    break;
                case "code":
                    if (!inLink)
                        run.SetResourceReference(TextElement.ForegroundProperty, "Bright");
                    break;
                case "dim":
                    run.SetResourceReference(TextElement.ForegroundProperty, "Dim");
                    break;
                case "linknum":
                    // Inherit the link colour (so it inverts on hover) but drop the underline.
                    run.TextDecorations = [];
                    break;
            }
        }
        return run;
    }
}
