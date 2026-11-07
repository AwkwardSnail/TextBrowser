# Publishing TextBrowser to the Microsoft Store

Everything needed to build the Store package is in this folder:

| File | What it is |
|---|---|
| `make-assets.ps1` | Draws the icon (a green `>` prompt and block cursor on a dark screen) at every size (`Assets\`), plus `TextBrowser.ico` for the .exe and window |
| `AppxManifest.template.xml` | The package description (name, icons, capabilities) |
| `build-msix.ps1` | Publishes the app and builds `windows\dist\TextBrowser_<version>.msixbundle` (x64 + ARM64) |

## 1. Things only you can do (Partner Center)

1. **Reserve the app name.** Apps and games → New product → MSIX or PWA app → enter **TextBrowser**.
   Store names are unique; if it's taken, pick another and change `DisplayName` (in both places)
   in `AppxManifest.template.xml` to match.
2. **Check the identity values.** Product management → Product identity. `build-msix.ps1` already
   uses Akward Snail's values:
   - `Package/Identity/Name`: `AkwardSnail.TextBrowser`
   - `Package/Identity/Publisher`: `CN=A55585C2-FE40-4ED1-9B77-46466AE9C532`
   - `Package/Properties/PublisherDisplayName`: `Akward Snail`

   If the page shows a different name, pass it with `-IdentityName`.

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
- **Store listing:** description (draft below), screenshots of the app, category
  **Productivity** (or Utilities & tools), a few search terms (text browser, retro, terminal, reader).
- **Privacy policy URL:** <https://awkwardsnail.com.au/privacy.html> (it already covers TextBrowser).
- **Age ratings:** answer the questionnaire. A web browser gives unrestricted internet access,
  so expect an older rating than the other apps.
- **Pricing and availability:** one low price, the markets you want.

### Draft listing text

> **The web, just the words.** TextBrowser shows web pages as plain text on a glowing
> 1980s-style terminal screen. No pictures, video, sound, adverts or pop-ups: only the text,
> the links and simple forms.
>
> - Links are numbered; click one, or type its number
> - Search, fill in text boxes, press buttons and choose from drop-down menus
> - Green, amber or white phosphor, with full screen for distraction-free reading
> - Images, video, audio and fonts are blocked before they download
> - No accounts, tracking or analytics

## 4. Optional checks before submitting

- **Windows App Certification Kit** (the same tests the Store runs). Needs an administrator PowerShell:
  ```powershell
  & "C:\Program Files (x86)\Windows Kits\10\App Certification Kit\appcert.exe" test `
      -appxpackagepath "..\dist\TextBrowser_2.0.0.0.msixbundle" -reportoutputpath "$env:TEMP\wack.xml"
  ```
- **Install the package yourself.** With *Developer Mode* on (Settings → System → For developers),
  register the unpacked folder:
  ```powershell
  Add-AppxPackage -Register "..\dist\layout-x64\AppxManifest.xml"
  ```
  Remove the test install afterwards from Settings → Apps, or with
  `Get-AppxPackage AkwardSnail.TextBrowser | Remove-AppxPackage`.
