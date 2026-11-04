# TEXTBROWSER

A text-only web browser with a 1980s terminal look: phosphor text on a black screen,
an inverse-video status bar and a blinking block cursor. It shows only a page's text.
It never loads images, video, audio, scripts or styling. Hyperlinks still work and are
numbered like `Example[3]`.

## Run

```bash
python textbrowser.py
```

```bash
python textbrowser.py en.wikipedia.org/wiki/VT100
```

It needs only the Python standard library (with tkinter). The optional `truststore`
package (`pip install -r requirements.txt`) fixes HTTPS certificate errors on some sites.

## Using it

- Type an address, search words or a link number at the `URL>` prompt, then press Enter.
- Click a link, or press Tab / Shift+Tab to select one and Enter to follow it.
- Alt+Left / Backspace: back. Alt+Right: forward. F5: reload. Esc: cancel loading.
- Ctrl+L: jump to the URL bar. F1: help. F2: switch phosphor colour (green, amber, white).
- Arrow keys, Page Up/Down, Space, Home and End scroll the page.

Searches go to DuckDuckGo Lite. Forms are not supported.
