// Runs inside the hidden Chromium page after it loads. Walks the rendered DOM
// and returns only the visible text, as styled runs, plus a numbered link list.
// Result: { url, title, runs: [[text, "tags", linkNumber, control?], ...], links: [url, ...] }
// or { url, title, nonText: contentType } for documents that aren't text.
// Form controls become runs with empty text and a control object; the element is
// marked with data-tb-id so the app can type into it, click it or change it later.
(() => {
  const contentType = document.contentType || "text/html";
  if (!/^text\/|[+\/](xml|json)$/.test(contentType)) {
    return { url: location.href, title: document.title, nonText: contentType };
  }

  // Everything inside these elements is dropped (media, scripts...).
  const SKIP = new Set(["SCRIPT", "STYLE", "NOSCRIPT", "SVG", "MATH", "VIDEO", "AUDIO",
    "IFRAME", "CANVAS", "OBJECT", "EMBED", "TEMPLATE", "HEAD"]);
  const CONTROLS = new Set(["INPUT", "TEXTAREA", "SELECT", "BUTTON"]);
  const TEXT_TYPES = new Set(["text", "search", "email", "url", "tel", "number", "password",
    "date", "time", "datetime-local", "month", "week"]);
  const HEADINGS = new Set(["H1", "H2", "H3", "H4", "H5", "H6"]);
  // Block elements start on a new line; PARA also get a blank line around them.
  const PARA = new Set([...HEADINGS, "P", "PRE", "BLOCKQUOTE", "TABLE", "FIGURE", "DL", "UL", "OL"]);
  const BLOCK = new Set([...PARA, "ADDRESS", "ARTICLE", "ASIDE", "BODY", "DD", "DETAILS",
    "DIALOG", "DIV", "DT", "FIELDSET", "FIGCAPTION", "FOOTER", "FORM", "HEADER", "HGROUP",
    "LI", "MAIN", "NAV", "SECTION", "SUMMARY", "TR", "CAPTION", "CENTER", "MENU", "TBODY",
    "THEAD", "TFOOT"]);
  const STYLE = { B: "b", STRONG: "b", TH: "b", I: "i", EM: "i", CITE: "i",
    CODE: "code", KBD: "code", SAMP: "code", TT: "code", PRE: "code" };
  for (const h of HEADINGS) STYLE[h] = "h";

  const runs = [], links = [], styles = [], lists = [];
  let wantNl = 0;      // newlines the next text must be preceded by
  let trailNl = 0;     // newlines already at the end of the output
  let last = "\n";
  let bullet = "";     // list bullet waiting for the item's first text
  let link = null;     // { url, n, el } while inside a link
  let pre = 0, upper = 0;

  const brk = n => { wantNl = Math.max(wantNl, n); };

  function tagsFor(text) {
    let n = 0;
    if (link && text.trim()) {
      if (!link.n) {             // number links only once they show text
        links.push(link.url);
        link.n = links.length;
      }
      n = link.n;
    }
    return [[...new Set(styles)].join(" "), n];
  }

  function write(text, fixed, control) {
    if (!text && !control) return;
    if (wantNl) {
      const missing = wantNl - trailNl;
      if (runs.length && missing > 0) {
        runs.push(["\n".repeat(missing), "", 0]);
        trailNl += missing;
        last = "\n";
      }
      wantNl = 0;
    }
    if (bullet && (control || text.trim())) {
      runs.push([bullet, "dim", 0]);
      bullet = "";
    }
    if (control) {
      runs.push(["", "", 0, control]);
      trailNl = 0;
      last = "#";
      return;
    }
    const [tags, n] = fixed || tagsFor(text);
    runs.push([text, tags, n]);
    const stripped = text.replace(/\n+$/, "");
    trailNl = stripped ? text.length - stripped.length : trailNl + text.length;
    last = text[text.length - 1];
  }

  function start(tag, el) {
    if (BLOCK.has(tag)) {
      const nestedList = (tag === "UL" || tag === "OL") && lists.length;
      brk(PARA.has(tag) && !nestedList ? 2 : 1);
    }
    if (STYLE[tag]) styles.push(STYLE[tag]);
    if (tag === "H1" || tag === "H2") upper++;
    switch (tag) {
      case "PRE":
        pre++;
        break;
      case "UL": case "OL": case "MENU":
        lists.push(tag === "OL" ? 0 : null);
        break;
      case "LI": {
        const indent = "  ".repeat(Math.max(0, lists.length - 1));
        const top = lists.length - 1;
        bullet = indent + (top >= 0 && lists[top] !== null ? `${++lists[top]}. ` : "* ");
        break;
      }
      case "A": {
        const href = typeof el.href === "string" ? el.href : "";
        if (href && !link && !href.startsWith("javascript:")) link = { url: href, n: 0, el };
        break;
      }
      case "IMG": {
        // Images are never shown, but an image-only link still needs a label.
        const alt = (el.alt || "").trim();
        if (link && !link.n && alt) write(alt);
        break;
      }
      case "BR":
        write("\n", ["", 0]);
        break;
      case "HR":
        brk(1);
        write("-".repeat(48), ["dim", 0]);
        brk(1);
        break;
      case "TD": case "TH":
        if (!wantNl && last !== " " && last !== "\n") write("  |  ", ["dim", 0]);
        break;
    }
  }

  function end(tag, el) {
    if (tag === "A" && link && link.el === el) {
      const n = link.n;
      link = null;
      if (n) write(`[${n}]`, ["linknum", n]);
    } else if ((tag === "UL" || tag === "OL" || tag === "MENU") && lists.length) {
      lists.pop();
    } else if (tag === "PRE") {
      pre = Math.max(0, pre - 1);
    }
    if (STYLE[tag]) {
      const i = styles.lastIndexOf(STYLE[tag]);
      if (i >= 0) styles.splice(i, 1);
    }
    if (tag === "H1" || tag === "H2") upper--;
    if (BLOCK.has(tag)) brk(PARA.has(tag) ? 2 : 1);
  }

  function text(data) {
    if (pre) {
      write(data);
      return;
    }
    let t = data.replace(/\s+/g, " ");
    if (wantNl || last === " " || last === "\n") t = t.replace(/^ /, "");
    if (upper > 0) t = t.toUpperCase();
    write(t);
  }

  const clean = s => (s || "").replace(/\s+/g, " ").trim();

  function control(tag, el) {
    // Fully transparent controls are usually tricks behind custom menus; skip them.
    if (el.disabled || !el.checkVisibility({ checkOpacity: true, checkVisibilityCSS: true })) return;
    const type = tag === "INPUT" ? (el.type || "text").toLowerCase() : tag.toLowerCase();
    const placeholder = clean(el.placeholder || el.getAttribute("aria-label"));
    let c;
    if (tag === "SELECT") {
      c = { kind: "select", options: [...el.options].map(o => clean(o.text)), index: el.selectedIndex };
    } else if (tag === "TEXTAREA") {
      c = { kind: "textarea", value: el.value, placeholder, size: el.cols || 0 };
    } else if (TEXT_TYPES.has(type)) {
      c = { kind: type === "password" ? "password" : "text", value: el.value, placeholder,
            size: el.hasAttribute("size") ? el.size : 0 };
    } else if (type === "checkbox" || type === "radio") {
      c = { kind: type, checked: el.checked };
    } else if (tag === "BUTTON" || ["submit", "button", "reset", "image"].includes(type)) {
      const label = clean(tag === "BUTTON" ? el.innerText : el.value || el.alt)
        || clean(el.getAttribute("aria-label") || el.title) || (type === "submit" ? "Submit" : "");
      if (!label) return;                 // icon-only button with no name: nothing to show
      c = { kind: "button", label };
    } else {
      return;                             // hidden fields, file uploads, sliders...
    }
    window.__tbNextId = window.__tbNextId || 0;
    if (!el.dataset.tbId) el.dataset.tbId = String(++window.__tbNextId);
    c.id = el.dataset.tbId;
    if (!wantNl && last !== " " && last !== "\n") write(" ", ["", 0]);
    write("", null, c);
  }

  function walk(node) {
    if (node.nodeType === Node.TEXT_NODE) {
      text(node.data);
      return;
    }
    if (node.nodeType !== Node.ELEMENT_NODE) return;
    const tag = node.tagName.toUpperCase();
    if (SKIP.has(tag)) return;
    // Skip anything the page hides (closed menus, pop-ups...). display:contents
    // elements have no box of their own but their children are still shown.
    if (node.checkVisibility && !node.checkVisibility({ checkVisibilityCSS: true })
        && getComputedStyle(node).display !== "contents") return;
    if (CONTROLS.has(tag)) {
      control(tag, node);
      return;
    }
    start(tag, node);
    for (const child of node.childNodes) walk(child);
    end(tag, node);
  }

  if (document.body) walk(document.body);
  while (runs.length && !runs[0][0].trim()) runs.shift();
  return {
    url: location.href,
    title: (document.title || "").replace(/\s+/g, " ").trim(),
    runs,
    links,
  };
})()
