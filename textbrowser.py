"""TEXTBROWSER - a text-only web browser with a 1980s terminal look.

Only the text of a page is shown: no images, video, audio, scripts or styles.
Hyperlinks are kept, numbered, and can be followed with the mouse or keyboard.

Uses only the Python standard library.  Run:   python textbrowser.py [url]
"""
import gzip
import html
import queue
import re
import sys
import threading
import zlib
import tkinter as tk
import tkinter.font as tkfont
from dataclasses import dataclass, field
from html.parser import HTMLParser
from urllib.error import HTTPError, URLError
from urllib.parse import quote_plus, urldefrag, urljoin, urlparse
from urllib.request import Request, urlopen

try:
    # Optional: verify HTTPS with the operating system's certificate store,
    # which copes with sites whose certificate chain Python can't complete.
    import truststore
    truststore.inject_into_ssl()
except ImportError:
    pass

APP = "TEXTBROWSER"
VERSION = "1.0"
USER_AGENT = "Mozilla/5.0 (compatible; TextBrowser/1.0; text-only)"
SEARCH_URL = "https://lite.duckduckgo.com/lite/?q="
MAX_BYTES = 5 * 1024 * 1024
SUPPORTED_SCHEMES = {"http", "https", "file", "about"}

BEZEL = "#1c1c1c"
THEMES = [
    ("GREEN", {"bg": "#020a02", "fg": "#33ff66", "bright": "#b6ffc8", "dim": "#1f9940"}),
    ("AMBER", {"bg": "#0d0700", "fg": "#ffb000", "bright": "#ffe0a0", "dim": "#a06e00"}),
    ("WHITE", {"bg": "#05070a", "fg": "#d8e0e8", "bright": "#ffffff", "dim": "#7a8590"}),
]

# --------------------------------------------------------------------------
# HTML -> text
# --------------------------------------------------------------------------

# Everything inside these elements is dropped (media, scripts, styling...).
SKIP_TAGS = {"script", "style", "noscript", "svg", "math", "video", "audio",
             "iframe", "canvas", "object", "embed", "template", "select", "map"}
HEADINGS = {"h1", "h2", "h3", "h4", "h5", "h6"}
# Block elements start on a new line; PARA_TAGS also get a blank line around them.
PARA_TAGS = HEADINGS | {"p", "pre", "blockquote", "table", "figure", "dl", "ul", "ol"}
BLOCK_TAGS = PARA_TAGS | {"address", "article", "aside", "body", "dd", "details",
                          "dialog", "div", "dt", "fieldset", "figcaption", "footer",
                          "form", "header", "hgroup", "li", "main", "nav", "section",
                          "summary", "tr", "caption", "center", "menu", "tbody",
                          "thead", "tfoot"}
STYLE_MAP = {**{h: "h" for h in HEADINGS},
             "b": "b", "strong": "b", "th": "b",
             "i": "i", "em": "i", "cite": "i",
             "code": "code", "kbd": "code", "samp": "code", "tt": "code", "pre": "code"}


@dataclass
class Page:
    url: str
    title: str = ""
    runs: list = field(default_factory=list)   # [(text, (tag, ...)), ...]
    links: list = field(default_factory=list)  # absolute URLs; link N is links[N-1]


class PageParser(HTMLParser):
    """Turns HTML into runs of styled text plus a numbered list of links."""

    def __init__(self, base_url):
        super().__init__(convert_charrefs=True)
        self.base = base_url
        self.title = ""
        self.links = []
        self.runs = []
        self._skip = 0           # depth inside SKIP_TAGS
        self._in_title = False
        self._pre = 0
        self._styles = []        # stack of open tags found in STYLE_MAP
        self._lists = []         # stack of list counters (None for bullet lists)
        self._link = None        # {"url": ..., "n": link number or None}
        self._bullet = ""        # list bullet waiting for the item's first text
        self._want_nl = 0        # newlines the next text must be preceded by
        self._trail_nl = 0       # newlines already at the end of the output
        self._last = "\n"

    # -- output helpers ----------------------------------------------------
    def _break(self, n):
        self._want_nl = max(self._want_nl, n)

    def _tags(self, text):
        tags = {STYLE_MAP[t] for t in self._styles}
        if self._link and text.strip():
            if self._link["n"] is None:          # number links only once they show text
                self.links.append(self._link["url"])
                self._link["n"] = len(self.links)
            tags |= {"link", f"L{self._link['n']}"}
        return tuple(tags)

    def _write(self, text, tags=None):
        if not text:
            return
        if self._want_nl:
            missing = self._want_nl - self._trail_nl
            if self.runs and missing > 0:
                self.runs.append(("\n" * missing, ()))
                self._trail_nl += missing
                self._last = "\n"
            self._want_nl = 0
        if self._bullet and text.strip():
            bullet, self._bullet = self._bullet, ""
            self.runs.append((bullet, ("dim",)))
        self.runs.append((text, self._tags(text) if tags is None else tags))
        stripped = text.rstrip("\n")
        self._trail_nl = self._trail_nl + len(text) if not stripped else len(text) - len(stripped)
        self._last = text[-1]

    # -- parser callbacks --------------------------------------------------
    def handle_starttag(self, tag, attrs):
        if tag in SKIP_TAGS:
            self._skip += 1
            return
        if self._skip:
            return
        a = dict(attrs)
        if tag == "title":
            self._in_title = True
            return
        if tag == "base" and a.get("href"):
            self.base = urljoin(self.base, a["href"])
            return

        if tag in BLOCK_TAGS:
            self._break(2 if tag in PARA_TAGS and not (tag in ("ul", "ol") and self._lists) else 1)
        if tag in STYLE_MAP:
            self._styles.append(tag)
        if tag == "pre":
            self._pre += 1
        elif tag in ("ul", "ol", "menu"):
            self._lists.append(0 if tag == "ol" else None)
        elif tag == "li":
            indent = "  " * max(0, len(self._lists) - 1)
            if self._lists and self._lists[-1] is not None:
                self._lists[-1] += 1
                bullet = f"{self._lists[-1]}. "
            else:
                bullet = "* "
            self._bullet = indent + bullet
        elif tag == "a":
            href = (a.get("href") or "").strip()
            if href and not self._link:
                self._link = {"url": urljoin(self.base, href), "n": None}
        elif tag == "img":
            # Images are never shown, but an image-only link still needs a label.
            alt = (a.get("alt") or "").strip()
            if self._link and self._link["n"] is None and alt:
                self._write(alt)
        elif tag == "br":
            self._write("\n", ())
        elif tag == "hr":
            self._break(1)
            self._write("-" * 48, ("dim",))
            self._break(1)
        elif tag in ("td", "th") and self._last not in " \n":
            self._write("  |  ", ("dim",))

    def handle_endtag(self, tag):
        if tag in SKIP_TAGS:
            self._skip = max(0, self._skip - 1)
            return
        if self._skip:
            return
        if tag == "title":
            self._in_title = False
            return
        if tag == "a" and self._link:
            n = self._link["n"]
            self._link = None
            if n is not None:
                self._write(f"[{n}]", ("linknum", f"L{n}"))
        elif tag in ("ul", "ol", "menu") and self._lists:
            self._lists.pop()
        elif tag == "pre":
            self._pre = max(0, self._pre - 1)
        if tag in STYLE_MAP and tag in self._styles:
            idx = len(self._styles) - 1 - self._styles[::-1].index(tag)
            del self._styles[idx]
        if tag in BLOCK_TAGS:
            self._break(2 if tag in PARA_TAGS else 1)

    def handle_data(self, data):
        if self._in_title:
            self.title += data
            return
        if self._skip:
            return
        if self._pre:
            self._write(data)
            return
        text = re.sub(r"\s+", " ", data)
        if self._want_nl or self._last in " \n":
            text = text.lstrip()
        if any(t in self._styles for t in ("h1", "h2")):
            text = text.upper()
        self._write(text)


def parse_html(source, url):
    parser = PageParser(url)
    parser.feed(source)
    parser.close()
    runs = parser.runs
    while runs and not runs[0][0].strip():
        runs.pop(0)
    title = re.sub(r"\s+", " ", parser.title).strip()
    return Page(url=url, title=title, runs=runs, links=parser.links)


# --------------------------------------------------------------------------
# Fetching
# --------------------------------------------------------------------------

def fetch(url):
    req = Request(url, headers={
        "User-Agent": USER_AGENT,
        "Accept": "text/html,application/xhtml+xml,text/plain;q=0.9,*/*;q=0.5",
        "Accept-Language": "en",
    })
    with urlopen(req, timeout=20) as r:
        data = r.read(MAX_BYTES)
        final = r.geturl()
        ctype = r.headers.get_content_type()
        charset = r.headers.get_content_charset()
        encoding = (r.headers.get("Content-Encoding") or "").lower()
    if encoding == "gzip":
        data = gzip.decompress(data)
    elif encoding == "deflate":
        try:
            data = zlib.decompress(data)
        except zlib.error:
            data = zlib.decompress(data, -zlib.MAX_WBITS)
    return final, ctype, charset, data


def decode(data, charset):
    if not charset:
        m = re.search(rb"""<meta[^>]+charset=["']?([\w-]+)""", data[:4096], re.I)
        charset = m.group(1).decode("ascii") if m else "utf-8"
    try:
        return data.decode(charset, errors="replace")
    except LookupError:
        return data.decode("utf-8", errors="replace")


def message_page(url, title, body_html):
    return parse_html(f"<title>{html.escape(title)}</title>"
                      f"<h1>{html.escape(title)}</h1>{body_html}", url)


def load_page(url):
    """Runs on a worker thread: fetch a URL and turn it into a Page."""
    if url.startswith("about:"):
        return parse_html(INTERNAL_PAGES.get(url, INTERNAL_PAGES["about:blank"]), url)
    try:
        final, ctype, charset, data = fetch(url)
    except HTTPError as e:
        return message_page(url, f"ERROR {e.code}",
                            f"<p>THE SERVER REPLIED: {html.escape(str(e.reason))}</p>"
                            f"<p>URL: {html.escape(url)}</p>")
    except URLError as e:
        return message_page(url, "CONNECTION FAILED",
                            f"<p>{html.escape(str(e.reason))}</p><p>URL: {html.escape(url)}</p>")
    except Exception as e:  # timeouts, bad gzip, etc.
        return message_page(url, "TRANSMISSION ERROR",
                            f"<p>{html.escape(type(e).__name__)}: {html.escape(str(e))}</p>")

    if ctype in ("text/html", "application/xhtml+xml"):
        page = parse_html(decode(data, charset), final)
    elif ctype.startswith("text/") or ctype in ("application/json", "application/xml"):
        page = Page(url=final, title=final.rsplit("/", 1)[-1], runs=[(decode(data, charset), ())])
    else:
        page = message_page(final, "NON-TEXT CONTENT",
                            f"<p>THIS DOCUMENT IS OF TYPE <b>{html.escape(ctype)}</b>.</p>"
                            "<p>THIS TERMINAL DISPLAYS TEXT ONLY.</p>")
    if not page.title:
        page.title = urlparse(final).netloc or final
    return page


def normalize(entry):
    """Turn whatever was typed into the URL bar into a URL (or a search)."""
    if re.match(r"^[a-zA-Z][\w+.-]*:", entry) and not re.match(r"^[\w.-]+:\d+", entry):
        return entry
    if " " not in entry and ("." in entry or entry.startswith("localhost")):
        return ("http://" if entry.startswith("localhost") else "https://") + entry
    return SEARCH_URL + quote_plus(entry)


INTERNAL_PAGES = {
    "about:blank": "<title>BLANK</title>",
    "about:home": f"""<title>HOME</title>
<pre>
 +------------------------------------------------------+
 |                                                      |
 |     T E X T B R O W S E R          VERSION {VERSION:<6}    |
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
""",
    "about:help": """<title>HELP</title>
<h1>Keyboard commands</h1>
<pre>
  ENTER (in URL bar)   GO TO ADDRESS / SEARCH / LINK NUMBER
  CTRL+L  or  F6       JUMP TO THE URL BAR
  TAB / SHIFT+TAB      SELECT NEXT / PREVIOUS LINK
  ENTER (on page)      FOLLOW SELECTED LINK
  ALT+LEFT, BACKSPACE  BACK
  ALT+RIGHT            FORWARD
  ALT+HOME             HOME PAGE
  F5                   RELOAD
  ESC                  CANCEL LOADING
  F1                   THIS HELP PAGE
  F2                   CHANGE PHOSPHOR COLOUR (GREEN / AMBER / WHITE)
  UP/DOWN, PGUP/PGDN,
  SPACE, HOME, END     SCROLL
</pre>
<p>Images, video, audio, scripts and styling are never loaded or shown.
Forms are not supported; use the URL bar to search.</p>
<p><a href="about:home">Back to the home page</a></p>
""",
}


# --------------------------------------------------------------------------
# The terminal window
# --------------------------------------------------------------------------

def pick_font(root):
    families = set(tkfont.families(root))
    for name, size in (("VT323", 20), ("IBM Plex Mono", 13), ("Cascadia Mono", 13),
                       ("Consolas", 14), ("Lucida Console", 13), ("Courier New", 13)):
        if name in families:
            return name, size
    return "TkFixedFont", 13


class Browser:
    def __init__(self, root, start_url):
        self.root = root
        self.history = []
        self.pos = -1
        self.page = None
        self.results = queue.Queue()
        self.token = 0          # bumped per navigation so stale results are dropped
        self.loading = None     # URL being loaded, or None
        self.tick = 0
        self.theme = 0
        self.selected = None    # link number highlighted by Tab or the mouse

        family, size = pick_font(root)
        self.font = tkfont.Font(root, family=family, size=size)
        self.font_bold = tkfont.Font(root, family=family, size=size, weight="bold")
        self.font_italic = tkfont.Font(root, family=family, size=size, slant="italic")

        root.title(APP)
        root.geometry("1000x720")
        root.minsize(520, 320)
        root.configure(bg=BEZEL)
        self._build()
        self._bind()
        self.apply_theme()
        self.go(start_url)
        self._poll()

    # -- layout ------------------------------------------------------------
    def _build(self):
        f = self.font
        self.screen = tk.Frame(self.root, padx=14, pady=10, highlightthickness=2)
        self.screen.pack(fill="both", expand=True, padx=18, pady=18)

        self.header = tk.Label(self.screen, anchor="w", font=self.font_bold, padx=6)
        self.header.pack(fill="x")

        row = tk.Frame(self.screen)
        row.pack(fill="x", pady=(6, 4))
        self.prompt = tk.Label(row, text="URL>", font=self.font_bold)
        self.prompt.pack(side="left")
        self.entry = tk.Entry(row, font=f, relief="flat", borderwidth=0,
                              highlightthickness=0, insertwidth=10,
                              insertontime=530, insertofftime=530)
        self.entry.pack(side="left", fill="x", expand=True, padx=(8, 0))

        self.rule = tk.Frame(self.screen, height=1)
        self.rule.pack(fill="x", pady=(0, 4))

        self.text = tk.Text(self.screen, font=f, wrap="word", relief="flat",
                            borderwidth=0, highlightthickness=0, padx=6, pady=6,
                            cursor="arrow", spacing1=1, spacing3=1, undo=False,
                            yscrollcommand=self._on_scroll)
        self.text.pack(fill="both", expand=True)
        self.text.configure(state="disabled")

        bar = tk.Frame(self.screen)
        bar.pack(fill="x", pady=(6, 0))
        self.status = tk.Label(bar, anchor="w", font=f, padx=6)
        self.status.pack(side="left", fill="x", expand=True)
        self.scrollpos = tk.Label(bar, anchor="e", font=f, padx=6, width=6)
        self.scrollpos.pack(side="right")
        self.statusbar = bar

    def apply_theme(self):
        name, c = THEMES[self.theme]
        bg, fg, bright, dim = c["bg"], c["fg"], c["bright"], c["dim"]
        self.screen.configure(bg=bg, highlightbackground=dim, highlightcolor=dim)
        for w in (self.prompt.master, self.prompt, self.statusbar):
            w.configure(bg=bg)
        self.prompt.configure(fg=fg)
        self.rule.configure(bg=dim)
        for w in (self.header, self.status, self.scrollpos):
            w.configure(bg=fg, fg=bg)           # inverse-video bars
        self.entry.configure(bg=bg, fg=bright, insertbackground=fg,
                             selectbackground=fg, selectforeground=bg)
        t = self.text
        t.configure(bg=bg, fg=fg, insertbackground=bg,
                    selectbackground=fg, selectforeground=bg,
                    inactiveselectbackground=dim)
        t.tag_configure("h", foreground=bright, font=self.font_bold)
        t.tag_configure("b", foreground=bright, font=self.font_bold)
        t.tag_configure("i", font=self.font_italic)
        t.tag_configure("code", foreground=bright)
        t.tag_configure("dim", foreground=dim)
        t.tag_configure("link", foreground=bright, underline=True)
        t.tag_configure("linknum", foreground=dim)
        t.tag_configure("hover", background=fg, foreground=bg, underline=False)
        t.tag_raise("hover")

    # -- key and mouse bindings -------------------------------------------
    def _bind(self):
        r, t = self.root, self.text
        self.entry.bind("<Return>", lambda e: self.submit())
        for seq in ("<Control-l>", "<Control-L>", "<F6>"):
            r.bind(seq, self.focus_url)
        r.bind("<Alt-Left>", lambda e: self.back())
        r.bind("<Alt-Right>", lambda e: self.forward())
        r.bind("<Alt-Home>", lambda e: self.go("about:home"))
        r.bind("<F1>", lambda e: self.go("about:help"))
        r.bind("<F2>", lambda e: self.next_theme())
        r.bind("<F5>", lambda e: self.reload())
        r.bind("<Escape>", lambda e: self.stop())

        scroll = {"<Up>": (-1, "units"), "<Down>": (1, "units"),
                  "<Prior>": (-1, "pages"), "<Next>": (1, "pages"),
                  "<space>": (1, "pages"), "<Shift-space>": (-1, "pages")}
        for seq, (n, what) in scroll.items():
            t.bind(seq, lambda e, n=n, what=what: (t.yview_scroll(n, what), "break")[1])
        t.bind("<Home>", lambda e: (t.yview_moveto(0), "break")[1])
        t.bind("<End>", lambda e: (t.yview_moveto(1), "break")[1])
        t.bind("<Tab>", lambda e: self.cycle_link(1))
        t.bind("<Shift-Tab>", lambda e: self.cycle_link(-1))
        t.bind("<Return>", lambda e: self.follow(self.selected) if self.selected else None)
        t.bind("<BackSpace>", lambda e: self.back())
        t.bind("<Button-1>", lambda e: t.focus_set(), add=True)

        t.tag_bind("link", "<Motion>", self._link_hover)
        t.tag_bind("linknum", "<Motion>", self._link_hover)
        t.tag_bind("link", "<Leave>", self._link_leave)
        t.tag_bind("linknum", "<Leave>", self._link_leave)
        t.tag_bind("link", "<ButtonRelease-1>", self._link_click)
        t.tag_bind("linknum", "<ButtonRelease-1>", self._link_click)

    def _link_at_mouse(self):
        for tag in self.text.tag_names("current"):
            if tag[0] == "L" and tag[1:].isdigit():
                return int(tag[1:])
        return None

    def _link_hover(self, _event):
        n = self._link_at_mouse()
        if n and n != self.selected:
            self.select_link(n, scroll=False)
        self.text.configure(cursor="hand2")

    def _link_leave(self, _event):
        self.text.configure(cursor="arrow")
        self.select_link(None)

    def _link_click(self, _event):
        if self.text.tag_ranges("sel"):     # the user was selecting text, not clicking
            return
        n = self._link_at_mouse()
        if n:
            self.follow(n)

    # -- links -------------------------------------------------------------
    def select_link(self, n, scroll=True):
        self.text.tag_remove("hover", "1.0", "end")
        self.selected = n
        if n is None:
            self.show_page_status()
            return
        ranges = self.text.tag_ranges(f"L{n}")
        for i in range(0, len(ranges), 2):
            self.text.tag_add("hover", ranges[i], ranges[i + 1])
        if scroll and ranges:
            self.text.see(ranges[0])
        self.set_status(f"[{n}] {self.page.links[n - 1]}")

    def cycle_link(self, step):
        if self.page and self.page.links:
            count = len(self.page.links)
            if self.selected is None:
                n = 1 if step > 0 else count
            else:
                n = (self.selected - 1 + step) % count + 1
            self.select_link(n)
        return "break"

    def follow(self, n):
        if not self.page or not 1 <= n <= len(self.page.links):
            self.set_status(f"NO SUCH LINK: {n}")
            return "break"
        self.go(self.page.links[n - 1])
        return "break"

    # -- navigation --------------------------------------------------------
    def submit(self):
        entry = self.entry.get().strip()
        if not entry:
            return
        if entry.lstrip("#").isdigit():
            self.follow(int(entry.lstrip("#")))
        else:
            self.go(normalize(entry))

    def go(self, url, push=True):
        url, fragment = urldefrag(url)
        if fragment and self.page and url == self.page.url:
            return                              # in-page anchor: nothing to fetch
        scheme = urlparse(url).scheme.lower()
        if scheme not in SUPPORTED_SCHEMES:
            self.set_status(f"UNSUPPORTED PROTOCOL: {scheme.upper()}:")
            return
        self.token += 1
        self.loading = url
        self.select_link(None)
        self._set_entry(url)
        token = self.token
        threading.Thread(target=lambda: self.results.put((token, push, load_page(url))),
                         daemon=True).start()

    def back(self):
        if self.pos > 0:
            self.pos -= 1
            self.go(self.history[self.pos], push=False)
        return "break"

    def forward(self):
        if self.pos < len(self.history) - 1:
            self.pos += 1
            self.go(self.history[self.pos], push=False)
        return "break"

    def reload(self):
        if self.page:
            self.go(self.page.url, push=False)

    def stop(self):
        if self.loading:
            self.token += 1
            self.loading = None
            self._set_entry(self.page.url if self.page else "")
            self.set_status("*** LOADING CANCELLED ***")
        self.text.focus_set()

    def _poll(self):
        """Pick up finished page loads (Tk must only be touched on this thread)."""
        try:
            while True:
                token, push, page = self.results.get_nowait()
                if token == self.token:
                    self.loading = None
                    self.show(page, push)
        except queue.Empty:
            pass
        if self.loading:
            self.tick += 1
            spinner = "|/-\\"[self.tick // 3 % 4]
            host = urlparse(self.loading).netloc or self.loading
            self.set_status(f"{spinner} CONNECTING TO {host.upper()} ...")
        self.root.after(50, self._poll)

    def show(self, page, push):
        if push:
            del self.history[self.pos + 1:]
            self.history.append(page.url)
            self.pos = len(self.history) - 1
        elif 0 <= self.pos < len(self.history):
            self.history[self.pos] = page.url   # keep redirects in history
        self.page = page
        self.selected = None
        self._set_entry(page.url)
        self.header.configure(text=f"{APP} v{VERSION}  ::  {page.title.upper()}")
        self.root.title(f"{page.title} - {APP}")

        t = self.text
        t.configure(state="normal")
        t.delete("1.0", "end")
        for i in range(0, len(page.runs), 400):
            chunk = page.runs[i:i + 400]
            t.insert("end", *[x for run in chunk for x in run])
        t.configure(state="disabled")
        t.yview_moveto(0)
        t.focus_set()
        self.show_page_status()

    # -- small helpers -----------------------------------------------------
    def next_theme(self):
        self.theme = (self.theme + 1) % len(THEMES)
        self.apply_theme()
        self.set_status(f"PHOSPHOR: {THEMES[self.theme][0]}")

    def focus_url(self, _event=None):
        self.entry.focus_set()
        self.entry.select_range(0, "end")
        self.entry.icursor("end")
        return "break"

    def _set_entry(self, text):
        self.entry.delete(0, "end")
        self.entry.insert(0, text)

    def set_status(self, msg):
        self.status.configure(text=msg)

    def show_page_status(self):
        if self.loading:
            return
        n = len(self.page.links) if self.page else 0
        self.set_status(f"{n} LINKS  -  TAB/CLICK TO SELECT  -  F1 HELP  -  ALT+LEFT BACK")

    def _on_scroll(self, first, last):
        first, last = float(first), float(last)
        if first <= 0 and last >= 1:
            pos = "ALL"
        elif first <= 0:
            pos = "TOP"
        elif last >= 1:
            pos = "BOT"
        else:
            pos = f"{int(last * 100)}%"
        self.scrollpos.configure(text=pos)


def main():
    start = normalize(sys.argv[1]) if len(sys.argv) > 1 else "about:home"
    root = tk.Tk()
    Browser(root, start)
    root.mainloop()


if __name__ == "__main__":
    main()
