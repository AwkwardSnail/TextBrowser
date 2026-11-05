using System.Globalization;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Threading;
using Microsoft.Web.WebView2.Core;

namespace TextBrowser;

public partial class MainWindow : Window
{
    public const string Version = "2.0";
    const string AppName = "TEXTBROWSER";
    const string SearchUrl = "https://lite.duckduckgo.com/lite/?q=";

    static readonly (string Name, string Bg, string Fg, string Bright, string Dim)[] Themes =
    [
        ("GREEN", "#020a02", "#33ff66", "#b6ffc8", "#1f9940"),
        ("AMBER", "#0d0700", "#ffb000", "#ffe0a0", "#a06e00"),
        ("WHITE", "#05070a", "#d8e0e8", "#ffffff", "#7a8590"),
    ];

    static readonly string ExtractScript = LoadExtractScript();

    // The page engine: a Chromium (Edge WebView2) instance that is never shown.
    CoreWebView2Environment? _env;
    CoreWebView2Controller? _controller;
    CoreWebView2? _web;

    readonly List<string> _history = [];
    int _pos = -1;
    Page? _page;

    ulong _activeNav;          // navigation whose result we will show; 0 = none
    bool _pushPending;         // add the next page shown to history (vs. replace)
    string? _loadingUrl;
    string? _internalUrl;      // about: page being shown via NavigateToString
    readonly string _startUrl;

    int _theme;
    bool _crt = true;
    bool _fullScreen;
    WindowState _windowedState;
    readonly DropShadowEffect _glow;
    ScrollViewer? _scroll;
    readonly DispatcherTimer _spinner;
    readonly DispatcherTimer _caretBlink;
    int _tick;

    public MainWindow(string? startUrl)
    {
        InitializeComponent();
        _startUrl = string.IsNullOrWhiteSpace(startUrl) ? InternalPages.Home : Normalize(startUrl);
        _glow = (DropShadowEffect)Screen.Effect;

        _spinner = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(120) };
        _spinner.Tick += (_, _) => ShowLoadingStatus();
        _caretBlink = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(530) };
        _caretBlink.Tick += (_, _) => BlockCaret.Opacity = BlockCaret.Opacity > 0 ? 0 : 1;

        UrlBox.KeyDown += (_, e) =>
        {
            if (e.Key == Key.Enter)
            {
                Submit();
                e.Handled = true;
            }
        };
        UrlBox.SelectionChanged += (_, _) => QueueCaretUpdate();
        UrlBox.TextChanged += (_, _) => QueueCaretUpdate();
        UrlBox.GotKeyboardFocus += (_, _) => QueueCaretUpdate();
        UrlBox.LostKeyboardFocus += (_, _) => QueueCaretUpdate();
        UrlBox.SizeChanged += (_, _) => QueueCaretUpdate();

        PreviewKeyDown += OnPreviewKeyDown;
        PreviewTextInput += OnPreviewTextInput;
        Loaded += OnLoaded;
        Closed += (_, _) => _controller?.Close();
    }

    static string LoadExtractScript()
    {
        using var stream = typeof(MainWindow).Assembly.GetManifestResourceStream("Extract.js")!;
        using var reader = new System.IO.StreamReader(stream);
        return reader.ReadToEnd();
    }

    // ------------------------------------------------------------------
    // Page engine setup
    // ------------------------------------------------------------------

    async void OnLoaded(object sender, RoutedEventArgs e)
    {
        ApplyTheme();
        try
        {
            var dataFolder = System.IO.Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "TextBrowser", "WebView2");
            _env = await CoreWebView2Environment.CreateAsync(null, dataFolder);
            _controller = await _env.CreateCoreWebView2ControllerAsync(new WindowInteropHelper(this).Handle);
        }
        catch (Exception ex) when (ex is WebView2RuntimeNotFoundException or COMException)
        {
            ShowPage(Page.Message("about:error", "PAGE ENGINE NOT FOUND",
                "TEXTBROWSER NEEDS THE MICROSOFT EDGE WEBVIEW2 RUNTIME, WHICH COMES WITH WINDOWS 11.",
                "INSTALL IT FROM MICROSOFT, THEN RESTART TEXTBROWSER.",
                $"DETAILS: {ex.Message}"));
            return;
        }

        _controller.IsVisible = false;
        _controller.Bounds = new System.Drawing.Rectangle(0, 0, 1280, 900);   // lay pages out as a desktop
        _web = _controller.CoreWebView2;
        _web.IsMuted = true;
        _web.Settings.AreDevToolsEnabled = false;
        _web.Settings.AreDefaultScriptDialogsEnabled = false;
        _web.Settings.IsStatusBarEnabled = false;
        _web.Settings.IsGeneralAutofillEnabled = false;
        _web.Settings.IsPasswordAutosaveEnabled = false;

        // Text only: images, video, audio and fonts are refused before they download.
        foreach (var kind in new[] { CoreWebView2WebResourceContext.Image, CoreWebView2WebResourceContext.Media,
                                     CoreWebView2WebResourceContext.Font })
            _web.AddWebResourceRequestedFilter("*", kind);
        _web.WebResourceRequested += (_, args) =>
            args.Response = _env.CreateWebResourceResponse(null, 403, "Blocked", "");

        _web.NewWindowRequested += (_, args) => args.Handled = true;   // no pop-ups
        _web.NavigationStarting += OnNavigationStarting;
        _web.NavigationCompleted += OnNavigationCompleted;
        _web.DownloadStarting += OnDownloadStarting;

        Go(_startUrl);
    }

    // ------------------------------------------------------------------
    // Navigation
    // ------------------------------------------------------------------

    void Go(string url, bool push = true)
    {
        var hash = url.IndexOf('#');
        if (hash >= 0 && _page is not null && url[..hash] == _page.Url)
            return;                                         // in-page anchor: nothing to load
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri))
        {
            SetStatus($"BAD ADDRESS: {url}");
            return;
        }
        var scheme = uri.Scheme.ToLowerInvariant();
        if (scheme is not ("http" or "https" or "file" or "about"))
        {
            SetStatus($"UNSUPPORTED PROTOCOL: {scheme.ToUpperInvariant()}:");
            return;
        }
        if (_web is null)
            return;

        _pushPending = push;
        _loadingUrl = url;
        _internalUrl = scheme == "about" ? url : null;
        SetUrlBox(url);
        _spinner.Start();
        if (_internalUrl is not null)
            _web.NavigateToString(InternalPages.Get(url));
        else
            _web.Navigate(url);
    }

    void OnNavigationStarting(object? sender, CoreWebView2NavigationStartingEventArgs e)
    {
        if (e.IsRedirected)
            return;
        _activeNav = e.NavigationId;
        if (_loadingUrl is null)
        {
            // Started by the page itself (a script or meta refresh), not by us.
            _internalUrl = null;
            _loadingUrl = e.Uri;
            _spinner.Start();
        }
    }

    async void OnNavigationCompleted(object? sender, CoreWebView2NavigationCompletedEventArgs e)
    {
        var nav = e.NavigationId;
        if (nav != _activeNav)
            return;
        if (!e.IsSuccess)
        {
            if (e.WebErrorStatus == CoreWebView2WebErrorStatus.OperationCanceled)
                return;
            var url = _loadingUrl ?? _web!.Source;
            ShowPage(Page.Message(url, "CONNECTION FAILED",
                $"ERROR: {Regex.Replace(e.WebErrorStatus.ToString(), "(?<=[a-z])(?=[A-Z])", " ").ToUpperInvariant()}",
                $"URL: {url}"));
            return;
        }

        // Give scripts a moment to put their text on the page; if there is very
        // little text, the page is probably still building itself, so look again.
        await Task.Delay(350);
        if (nav != _activeNav)
            return;
        var page = await Extract();
        if (_internalUrl is null && page is not null && page.Runs.Sum(r => r.Text.Length) < 400)
        {
            await Task.Delay(1500);
            if (nav != _activeNav)
                return;
            page = await Extract() ?? page;
        }
        if (nav == _activeNav && page is not null)
            ShowPage(page);
    }

    void OnDownloadStarting(object? sender, CoreWebView2DownloadStartingEventArgs e)
    {
        e.Cancel = true;
        _activeNav = 0;
        var op = e.DownloadOperation;
        ShowPage(Page.Message(op.Uri, "NON-TEXT CONTENT",
            $"THIS ADDRESS IS A FILE DOWNLOAD ({op.MimeType}).",
            "THIS TERMINAL DISPLAYS TEXT ONLY, SO NOTHING WAS DOWNLOADED."));
    }

    async Task<Page?> Extract()
    {
        string json;
        try
        {
            json = await _web!.ExecuteScriptAsync(ExtractScript);
        }
        catch (Exception)
        {
            return null;                                    // page went away mid-script
        }
        if (json == "null")
            return null;

        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        var url = _internalUrl ?? root.GetProperty("url").GetString() ?? "";
        var title = root.GetProperty("title").GetString() ?? "";
        if (root.TryGetProperty("nonText", out var type))
            return Page.Message(url, "NON-TEXT CONTENT",
                $"THIS DOCUMENT IS OF TYPE {type.GetString()?.ToUpperInvariant()}.",
                "THIS TERMINAL DISPLAYS TEXT ONLY.");

        var runs = root.GetProperty("runs").EnumerateArray()
            .Select(r => new TextRun(r[0].GetString() ?? "", r[1].GetString() ?? "", r[2].GetInt32()))
            .ToList();
        var links = root.GetProperty("links").EnumerateArray().Select(l => l.GetString() ?? "").ToList();
        if (title.Length == 0)
            title = Uri.TryCreate(url, UriKind.Absolute, out var uri) && uri.Host.Length > 0 ? uri.Host : url;
        return new Page { Url = url, Title = title, Runs = runs, Links = links };
    }

    void ShowPage(Page page)
    {
        _spinner.Stop();
        _loadingUrl = null;
        if (_pushPending)
        {
            _history.RemoveRange(_pos + 1, _history.Count - _pos - 1);
            _history.Add(page.Url);
            _pos = _history.Count - 1;
        }
        else if (_pos >= 0 && _pos < _history.Count)
        {
            _history[_pos] = page.Url;                      // keep redirects in history
        }
        _pushPending = false;

        _page = page;
        SetUrlBox(page.Url);
        HeaderText.Text = $"{AppName} v{Version}  ::  {page.Title.ToUpperInvariant()}";
        Title = $"{page.Title} - TextBrowser";

        Viewer.Document = PageRenderer.Render(page, Follow, HoverLink, ShowPageStatus);
        if (_scroll is null)
        {
            Viewer.ApplyTemplate();
            _scroll = Viewer.Template.FindName("PART_ContentHost", Viewer) as ScrollViewer;
            if (_scroll is not null)
                _scroll.ScrollChanged += (_, _) => UpdateScrollText();
        }
        _scroll?.ScrollToTop();
        Viewer.Focus();
        ShowPageStatus();
    }

    void Submit()
    {
        var text = UrlBox.Text.Trim();
        if (text.Length == 0)
            return;
        var number = text.TrimStart('#');
        if (number.Length > 0 && number.All(char.IsAsciiDigit) && int.TryParse(number, out var n))
            Follow(n);
        else
            Go(Normalize(text));
    }

    /// <summary>Turn whatever was typed into the URL bar into a URL (or a search).</summary>
    static string Normalize(string entry)
    {
        entry = entry.Trim();
        if (Regex.IsMatch(entry, @"^[a-zA-Z][\w+.-]*:") && !Regex.IsMatch(entry, @"^[\w.-]+:\d+"))
            return entry;
        if (!entry.Contains(' ') && (entry.Contains('.') || entry.StartsWith("localhost")))
            return (entry.StartsWith("localhost") ? "http://" : "https://") + entry;
        return SearchUrl + Uri.EscapeDataString(entry);
    }

    void Follow(int n)
    {
        if (_page is null || n < 1 || n > _page.Links.Count)
        {
            SetStatus($"NO SUCH LINK: {n}");
            return;
        }
        Go(_page.Links[n - 1]);
    }

    void Back()
    {
        if (_pos > 0)
            Go(_history[--_pos], push: false);
    }

    void Forward()
    {
        if (_pos < _history.Count - 1)
            Go(_history[++_pos], push: false);
    }

    void Reload()
    {
        if (_page is not null)
            Go(_page.Url, push: false);
    }

    void Stop()
    {
        if (_loadingUrl is not null)
        {
            _activeNav = 0;
            _web?.Stop();
            _spinner.Stop();
            _loadingUrl = null;
            SetUrlBox(_page?.Url ?? "");
            SetStatus("*** LOADING CANCELLED ***");
        }
        Viewer.Focus();
    }

    // ------------------------------------------------------------------
    // Keyboard
    // ------------------------------------------------------------------

    void OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        var key = e.Key == Key.System ? e.SystemKey : e.Key;
        var mods = Keyboard.Modifiers;
        var inUrlBox = UrlBox.IsKeyboardFocusWithin;
        Action? action = (key, mods) switch
        {
            (Key.L, ModifierKeys.Control) or (Key.F6, _) => FocusUrlBox,
            (Key.Left, ModifierKeys.Alt) => Back,
            (Key.Right, ModifierKeys.Alt) => Forward,
            (Key.Home, ModifierKeys.Alt) => () => Go(InternalPages.Home),
            (Key.F1, _) => () => Go(InternalPages.Help),
            (Key.F2, _) => NextTheme,
            (Key.F3, _) => ToggleCrt,
            (Key.F5, _) => Reload,
            (Key.F11, _) or (Key.Enter, ModifierKeys.Alt) => ToggleFullScreen,
            (Key.Escape, _) when _fullScreen => ToggleFullScreen,
            (Key.Escape, _) => Stop,
            _ when inUrlBox || _scroll is null => null,
            (Key.Back, _) => Back,
            (Key.Up, ModifierKeys.None) => _scroll.LineUp,
            (Key.Down, ModifierKeys.None) => _scroll.LineDown,
            (Key.PageUp, _) or (Key.Space, ModifierKeys.Shift) => _scroll.PageUp,
            (Key.PageDown, _) or (Key.Space, ModifierKeys.None) => _scroll.PageDown,
            (Key.Home, ModifierKeys.None) => _scroll.ScrollToTop,
            (Key.End, ModifierKeys.None) => _scroll.ScrollToBottom,
            _ => null,
        };
        if (action is not null)
        {
            action();
            e.Handled = true;
        }
    }

    void OnPreviewTextInput(object sender, TextCompositionEventArgs e)
    {
        // Typing a digit on the page starts a link number in the URL bar.
        if (!UrlBox.IsKeyboardFocusWithin && e.Text.Length == 1 && char.IsAsciiDigit(e.Text[0]))
        {
            UrlBox.Text = e.Text;
            UrlBox.Focus();
            UrlBox.CaretIndex = UrlBox.Text.Length;
            e.Handled = true;
        }
    }

    void FocusUrlBox()
    {
        UrlBox.Focus();
        UrlBox.SelectAll();
    }

    // ------------------------------------------------------------------
    // Look and feel
    // ------------------------------------------------------------------

    void ApplyTheme()
    {
        var t = Themes[_theme];
        Resources["Bg"] = MakeBrush(t.Bg);
        Resources["Fg"] = MakeBrush(t.Fg);
        Resources["Bright"] = MakeBrush(t.Bright);
        Resources["Dim"] = MakeBrush(t.Dim);
        _glow.Color = (Color)ColorConverter.ConvertFromString(t.Fg);
    }

    static SolidColorBrush MakeBrush(string hex)
    {
        var brush = new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex));
        brush.Freeze();
        return brush;
    }

    void NextTheme()
    {
        _theme = (_theme + 1) % Themes.Length;
        ApplyTheme();
        SetStatus($"PHOSPHOR: {Themes[_theme].Name}");
    }

    void ToggleCrt()
    {
        _crt = !_crt;
        Screen.Effect = _crt ? _glow : null;
        ScanlineLayer.Visibility = VignetteLayer.Visibility = _crt ? Visibility.Visible : Visibility.Collapsed;
        SetStatus(_crt ? "CRT EFFECTS: ON" : "CRT EFFECTS: OFF");
    }

    /// <summary>Fill the whole screen (no title bar or taskbar), or go back to the window.</summary>
    void ToggleFullScreen()
    {
        _fullScreen = !_fullScreen;
        if (_fullScreen)
        {
            _windowedState = WindowState;
            WindowState = WindowState.Normal;       // re-maximising without a border covers the taskbar
            WindowStyle = WindowStyle.None;
            ResizeMode = ResizeMode.NoResize;
            WindowState = WindowState.Maximized;
            SetStatus("FULL SCREEN  -  PRESS ESC TO RETURN TO THE WINDOW");
        }
        else
        {
            WindowStyle = WindowStyle.SingleBorderWindow;
            ResizeMode = ResizeMode.CanResize;
            WindowState = _windowedState;
            ShowPageStatus();
        }
    }

    void QueueCaretUpdate() => Dispatcher.BeginInvoke(UpdateCaret, DispatcherPriority.Render);

    /// <summary>Draw the block cursor under the character at the caret, and keep it blinking.</summary>
    void UpdateCaret()
    {
        if (!UrlBox.IsKeyboardFocused || UrlBox.SelectionLength > 0)
        {
            BlockCaret.Visibility = Visibility.Collapsed;
            _caretBlink.Stop();
            return;
        }
        var rect = UrlBox.GetRectFromCharacterIndex(UrlBox.CaretIndex);
        if (rect.IsEmpty)
            return;
        var charWidth = new FormattedText("M", CultureInfo.InvariantCulture, FlowDirection.LeftToRight,
            new Typeface(UrlBox.FontFamily, UrlBox.FontStyle, UrlBox.FontWeight, UrlBox.FontStretch),
            UrlBox.FontSize, Brushes.Black, VisualTreeHelper.GetDpi(this).PixelsPerDip).Width;
        Canvas.SetLeft(BlockCaret, rect.Left);
        Canvas.SetTop(BlockCaret, rect.Top);
        BlockCaret.Width = charWidth;
        BlockCaret.Height = rect.Height;
        BlockCaret.Opacity = 1;
        BlockCaret.Visibility = Visibility.Visible;
        _caretBlink.Stop();
        _caretBlink.Start();
    }

    void SetUrlBox(string text)
    {
        UrlBox.Text = text;
        UrlBox.CaretIndex = text.Length;
    }

    void SetStatus(string message) => StatusText.Text = message;

    void ShowPageStatus()
    {
        if (_loadingUrl is not null)
            return;
        var n = _page?.Links.Count ?? 0;
        SetStatus($"{n} LINKS  -  TAB OR CLICK TO SELECT  -  TYPE A NUMBER TO JUMP  -  F1 HELP");
    }

    void HoverLink(int n)
    {
        if (_page is not null && n <= _page.Links.Count)
            SetStatus($"[{n}] {_page.Links[n - 1]}");
    }

    void ShowLoadingStatus()
    {
        if (_loadingUrl is null)
            return;
        var host = Uri.TryCreate(_loadingUrl, UriKind.Absolute, out var uri) && uri.Host.Length > 0
            ? uri.Host : _loadingUrl;
        SetStatus($"{"|/-\\"[_tick++ % 4]} CONNECTING TO {host.ToUpperInvariant()} ...");
    }

    void UpdateScrollText()
    {
        if (_scroll is null)
            return;
        ScrollText.Text = _scroll.ScrollableHeight <= 0 ? "ALL"
            : _scroll.VerticalOffset <= 0 ? "TOP"
            : _scroll.VerticalOffset >= _scroll.ScrollableHeight - 0.5 ? "BOT"
            : $"{(int)((_scroll.VerticalOffset + _scroll.ViewportHeight) / _scroll.ExtentHeight * 100)}%";
    }
}
