# Publishing TextBrowser (SimpleWeb Text Browser) to the Microsoft Store

Everything needed to build the Store package is in this folder:

| File | What it is |
|---|---|
| `make-assets.ps1` | Draws the icon (a green `>` prompt and block cursor on a dark screen) at every size (`Assets\`), plus `TextBrowser.ico` for the .exe and window |
| `AppxManifest.template.xml` | The package description (name, icons, capabilities) |
| `build-msix.ps1` | Publishes the app and builds `windows\dist\TextBrowser_<version>.msixbundle` (x64 + ARM64) |
| `StoreListing.md` | Every Store listing field, ready to copy into Partner Center |
| `StoreLogos\` | The 300Ã—300 app tile icon and 2160Ã—2160 box art for the listing (made by `make-assets.ps1`) |
| `StoreScreenshots\` | Four 1600Ã—900 screenshots for the listing: a Wikipedia article, *Alice's Adventures in Wonderland* in amber, a DuckDuckGo search, the home page |
| `capture-window.ps1` | Sizes the running app's window to 1600Ã—900 and saves it as a screenshot (`-Name 5-something`) |

## 1. Things only you can do (Partner Center)

The app is reserved in Partner Center as **SimpleWeb Text Browser** (Store ID `9MVZ3GBZG15T`).
The manifest's `DisplayName` (three places in `AppxManifest.template.xml`) must match that name,
and `build-msix.ps1` uses the values from the Product identity page:

- `Package/Identity/Name`: `AkwardSnail.SimpleWebTextBrowser`
- `Package/Identity/Publisher`: `CN=A55585C2-FE40-4ED1-9B77-46466AE9C532`
- `Package/Properties/PublisherDisplayName`: `aWkward Snail`

Copy these from Partner Center exactly if they ever change; never "correct" the spelling.

## 2. Build the Store package

From this folder, in PowerShell:

```powershell
.\build-msix.ps1
```

The result is `windows\dist\TextBrowser_2.0.0.0.msixbundle`. No signing needed: the Store signs it.
For updates, raise the version: `-Version 2.0.1.0` (the last number must stay 0).
The package is self-contained (it brings its own .NET). Pages are loaded by the WebView2 runtime
that comes with Windows 10 and 11.

## 3. Fill in the submission

- **Packages:** upload the `.msixbundle`.
- **Restricted capability `runFullTrust`:** Partner Center asks why. Answer:
  *"TextBrowser is a WPF desktop application packaged with MSIX; runFullTrust is required for desktop apps."*
- **Web browser policy (10.2.1):** TextBrowser loads pages with Microsoft Edge WebView2 (Chromium),
  as the policy requires for apps that browse the web. Mention this in the notes for certification.
- **Store listing:** copy each field from [`StoreListing.md`](StoreListing.md); upload the screenshots
  from `StoreScreenshots\` and the logos from `StoreLogos\`. Category **Productivity** (or Utilities & tools).
- **Privacy policy URL:** <https://awkwardsnail.com.au/privacy.html> (it already covers TextBrowser).
- **Age ratings:** answer the questionnaire. A web browser gives unrestricted internet access,
  so expect an older rating than the other apps.
- **Pricing and availability:** one low price, the markets you want.

## 4. Optional checks before submitting

- **Windows App Certification Kit** (the same tests the Store runs). Needs an administrator PowerShell:
  ```powershell
  & "C:\Program Files (x86)\Windows Kits\10\App Certification Kit\appcert.exe" test `
      -appxpackagepath "..\dist\TextBrowser_2.0.0.0.msixbundle" -reportoutputpath "$env:TEMP\wack.xml"
  ```
- **Install the package yourself.** With *Developer Mode* on (Settings â†’ System â†’ For developers),
  register the unpacked folder:
  ```powershell
  Add-AppxPackage -Register "..\dist\layout-x64\AppxManifest.xml"
  ```
  Remove the test install afterwards from Settings â†’ Apps, or with
  `Get-AppxPackage AkwardSnail.SimpleWebTextBrowser | Remove-AppxPackage`.
