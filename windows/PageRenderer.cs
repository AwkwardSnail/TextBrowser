using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;

namespace TextBrowser;

/// <summary>Turns a Page into a FlowDocument of terminal text with clickable links and form fields.</summary>
public static class PageRenderer
{
    /// <summary>Builds the document. Focusables maps each control's id to the element that takes
    /// keyboard focus, so focus can be put back after the page is redrawn.</summary>
    public static FlowDocument Render(Page page, IPageActions actions, double charWidth,
                                      out Dictionary<string, IInputElement> focusables)
    {
        focusables = [];
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
            if (run.Control is not null)
            {
                link = null;
                para.Inlines.Add(RenderControl(run.Control, actions, charWidth, focusables));
                continue;
            }

            InlineCollection target;
            if (run.Link > 0)
            {
                // Consecutive runs of the same link (including its [n]) share one Hyperlink.
                if (link is null || (int)link.Tag != run.Link)
                {
                    link = MakeLink(run.Link, page.Links[run.Link - 1], actions);
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

    static Hyperlink MakeLink(int n, string url, IPageActions actions)
    {
        var link = new Hyperlink { Tag = n };
        link.Click += (_, _) => actions.Follow(n);
        Describe(link, $"[{n}] {url}", actions);
        return link;
    }

    /// <summary>Show a description in the status bar while the element is hovered or focused.</summary>
    static void Describe(IInputElement element, string status, IPageActions actions)
    {
        element.MouseEnter += (_, _) => actions.Describe(status);
        element.MouseLeave += (_, _) => actions.EndDescribe();
        element.GotKeyboardFocus += (_, _) =>
        {
            actions.Describe(status);
            (element as FrameworkContentElement)?.BringIntoView();
        };
        element.LostKeyboardFocus += (_, _) => actions.EndDescribe();
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

    // ------------------------------------------------------------------
    // Form controls
    // ------------------------------------------------------------------

    static Inline RenderControl(PageControl c, IPageActions actions, double charWidth,
                                Dictionary<string, IInputElement> focusables)
    {
        switch (c.Kind)
        {
            case "button":
            case "checkbox":
            case "radio":
            {
                // Drawn like a link so it inverts on hover/focus and Enter presses it.
                var text = c.Kind switch
                {
                    "checkbox" => c.Checked ? "[X]" : "[ ]",
                    "radio" => c.Checked ? "(*)" : "( )",
                    _ => $"[ {c.Label} ]",
                };
                var button = new Hyperlink(new Run(text)) { TextDecorations = [] };
                if (c.Kind == "button")
                    button.FontWeight = FontWeights.Bold;
                button.Click += (_, _) => actions.Click(c.Id);
                Describe(button, c.Kind == "button" ? $"BUTTON: {c.Label.ToUpperInvariant()}"
                                 : c.Checked ? "SELECTED - CLICK TO CHANGE" : "NOT SELECTED - CLICK TO CHANGE", actions);
                focusables[c.Id] = button;
                return button;
            }

            case "select":
            {
                var combo = new ComboBox
                {
                    ItemsSource = c.Options,
                    SelectedIndex = c.Index,
                    MinWidth = 8 * charWidth,
                    MaxDropDownHeight = 320,
                };
                combo.SetResourceReference(FrameworkElement.StyleProperty, "FieldCombo");
                // Tell the page only once a choice is final, not while arrowing through a closed list.
                var committed = c.Index;
                void Commit()
                {
                    if (combo.SelectedIndex >= 0 && combo.SelectedIndex != committed)
                    {
                        committed = combo.SelectedIndex;
                        actions.Select(c.Id, committed);
                    }
                }
                combo.SelectionChanged += (_, _) =>
                {
                    if (!combo.IsDropDownOpen)
                        Commit();
                };
                combo.DropDownClosed += (_, _) => Commit();
                Describe(combo, "DROP-DOWN: CLICK, OR USE UP/DOWN TO CHOOSE", actions);
                focusables[c.Id] = combo;
                return Container(combo);
            }

            default:
                return TextField(c, actions, charWidth, focusables);
        }
    }

    static Inline TextField(PageControl c, IPageActions actions, double charWidth,
                            Dictionary<string, IInputElement> focusables)
    {
        var multiLine = c.Kind == "textarea";
        var columns = Math.Clamp(c.Size > 0 ? c.Size : multiLine ? 60 : 30, 8, 72);
        var placeholder = new TextBlock
        {
            Text = c.Placeholder,
            IsHitTestVisible = false,
            Margin = new Thickness(3, 0, 0, 0),
            VerticalAlignment = multiLine ? VerticalAlignment.Top : VerticalAlignment.Center,
        };
        placeholder.SetResourceReference(TextBlock.ForegroundProperty, "Dim");

        Control box;
        if (c.Kind == "password")
        {
            var password = new PasswordBox { Password = c.Value };
            password.SetResourceReference(FrameworkElement.StyleProperty, "FieldPassword");
            placeholder.Visibility = c.Value.Length > 0 ? Visibility.Hidden : Visibility.Visible;
            password.PasswordChanged += (_, _) =>
            {
                placeholder.Visibility = password.Password.Length > 0 ? Visibility.Hidden : Visibility.Visible;
                actions.SetText(c.Id, password.Password);
            };
            password.KeyDown += (_, e) =>
            {
                if (e.Key == Key.Enter)
                {
                    actions.Submit(c.Id, password.Password);
                    e.Handled = true;
                }
            };
            box = password;
        }
        else
        {
            var text = new TextBox { Text = c.Value };
            text.SetResourceReference(FrameworkElement.StyleProperty, "FieldBox");
            if (multiLine)
            {
                text.AcceptsReturn = true;
                text.TextWrapping = TextWrapping.Wrap;
                text.Height = 4 * text.FontSize * 1.35;
            }
            placeholder.Visibility = c.Value.Length > 0 ? Visibility.Hidden : Visibility.Visible;
            text.TextChanged += (_, _) =>
            {
                placeholder.Visibility = text.Text.Length > 0 ? Visibility.Hidden : Visibility.Visible;
                actions.SetText(c.Id, text.Text);
            };
            if (!multiLine)
            {
                text.KeyDown += (_, e) =>
                {
                    if (e.Key == Key.Enter)
                    {
                        actions.Submit(c.Id, text.Text);
                        e.Handled = true;
                    }
                };
            }
            box = text;
        }
        Describe(box, multiLine ? "TEXT FIELD: TYPE YOUR TEXT" : "TEXT FIELD: TYPE, THEN PRESS ENTER TO SUBMIT", actions);
        focusables[c.Id] = box;

        var field = new Grid { Width = columns * charWidth + 8 };
        field.Children.Add(box);
        field.Children.Add(placeholder);
        return Container(field);
    }

    static InlineUIContainer Container(UIElement element)
    {
        if (element is FrameworkElement fe)
            fe.Margin = new Thickness(2, 1, 2, 1);
        return new InlineUIContainer(element) { BaselineAlignment = BaselineAlignment.Center };
    }
}
