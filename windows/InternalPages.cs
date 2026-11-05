namespace TextBrowser;

/// <summary>The built-in about: pages. They are HTML so they go through the same text extraction.</summary>
public static class InternalPages
{
    public const string Home = "about:home";
    public const string Help = "about:help";

    public static string Get(string url) => url switch
    {
        Home => HomeHtml,
        Help => HelpHtml,
        _ => "<title>BLANK</title>",
    };

    static readonly string HomeHtml = $"""
        <title>HOME</title>
        <pre>
         +------------------------------------------------------+
         |                                                      |
         |     T E X T B R O W S E R          VERSION {MainWindow.Version,-6}    |
         |     ----------------------------------------         |
         |     TEXT-ONLY HYPERTEXT TERMINAL                     |
         |                                                      |
         +------------------------------------------------------+

         MEMORY CHECK ............ 640K OK
         GRAPHICS ADAPTER ........ NOT REQUIRED
         MODEM ................... CONNECTED
         READY.
        </pre>
        <p>Type an address (or search words) at the <b>URL&gt;</b> prompt above and press ENTER.
        Type a link number, like <b>3</b>, to follow link [3].</p>
        <h3>Text-friendly destinations</h3>
        <ul>
        <li><a href="https://text.npr.org/">NPR News - text edition</a></li>
        <li><a href="https://lite.cnn.com/">CNN Lite</a></li>
        <li><a href="https://news.ycombinator.com/">Hacker News</a></li>
        <li><a href="https://en.wikipedia.org/wiki/Special:Random">A random Wikipedia article</a></li>
        <li><a href="https://www.gutenberg.org/">Project Gutenberg - free books</a></li>
        <li><a href="https://lite.duckduckgo.com/lite/">DuckDuckGo Lite search</a></li>
        </ul>
        <p><a href="about:help">Keyboard commands</a></p>
        """;

    const string HelpHtml = """
        <title>HELP</title>
        <h1>Keyboard commands</h1>
        <pre>
          ENTER (in URL bar)   GO TO ADDRESS / SEARCH / LINK NUMBER
          0-9 (on page)        START TYPING A LINK NUMBER
          CTRL+L  or  F6       JUMP TO THE URL BAR
          TAB / SHIFT+TAB      SELECT NEXT / PREVIOUS LINK
          ENTER (on a link)    FOLLOW THE LINK
          ALT+LEFT, BACKSPACE  BACK
          ALT+RIGHT            FORWARD
          ALT+HOME             HOME PAGE
          F5                   RELOAD
          ESC                  CANCEL LOADING
          F1                   THIS HELP PAGE
          F2                   CHANGE PHOSPHOR COLOUR (GREEN / AMBER / WHITE)
          F3                   CRT EFFECTS ON / OFF (GLOW AND SCANLINES)
          UP/DOWN, PGUP/PGDN,
          SPACE, HOME, END     SCROLL
        </pre>
        <p>Pages are loaded by the Microsoft Edge engine, but images, video, audio and fonts are
        blocked and never downloaded. Only the text and links are shown.
        Forms are not supported; use the URL bar to search.</p>
        <p><a href="about:home">Back to the home page</a></p>
        """;
}
