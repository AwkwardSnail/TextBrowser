# Store screenshots: sizes the running TextBrowser window to exactly
# -Width x -Height pixels (on screen, without the drop shadow) and saves it as
# a PNG. Drive the app to the page you want first, then run:
#   .\capture-window.ps1 -Name 1-home
param(
    [string]$Name,
    [int]$Width = 1600,
    [int]$Height = 900,
    [string]$Out = (Join-Path $PSScriptRoot 'StoreScreenshots'),
    [switch]$SizeOnly
)
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing
Add-Type @'
using System;
using System.Runtime.InteropServices;
public static class Win {
    [StructLayout(LayoutKind.Sequential)] public struct RECT { public int L, T, R, B; }
    [DllImport("user32.dll")] public static extern IntPtr SetThreadDpiAwarenessContext(IntPtr ctx);
    [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr h, out RECT r);
    [DllImport("user32.dll")] public static extern bool SetWindowPos(IntPtr h, IntPtr after, int x, int y, int w, int cy, uint flags);
    [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr h);
    [DllImport("user32.dll")] public static extern bool ShowWindow(IntPtr h, int cmd);
    [DllImport("dwmapi.dll")] public static extern int DwmGetWindowAttribute(IntPtr h, int attr, out RECT r, int size);
    public static RECT Frame(IntPtr h) { RECT r; DwmGetWindowAttribute(h, 9, out r, 16); return r; }
}
'@
[void][Win]::SetThreadDpiAwarenessContext([IntPtr](-4))   # per-monitor v2: real pixels

$proc = Get-Process TextBrowser | Where-Object MainWindowHandle -ne 0 | Select-Object -First 1
if (-not $proc) { throw 'TextBrowser is not running.' }
$h = $proc.MainWindowHandle
[void][Win]::ShowWindow($h, 9)   # restore if maximised

# The visible frame is smaller than the window rect (invisible resize borders),
# so size the window, measure the frame, and correct.
$w = New-Object Win+RECT
[void][Win]::GetWindowRect($h, [ref]$w)
$f = [Win]::Frame($h)
$padW = ($w.R - $w.L) - ($f.R - $f.L); $padH = ($w.B - $w.T) - ($f.B - $f.T)
$left = $f.L - $w.L; $top = $f.T - $w.T
[void][Win]::SetWindowPos($h, [IntPtr]::Zero, 20 - $left, 20 - $top, $Width + $padW, $Height + $padH, 0x0004)
[void][Win]::SetForegroundWindow($h)
Start-Sleep -Milliseconds 800
$f = [Win]::Frame($h)
"Frame: $($f.R - $f.L) x $($f.B - $f.T) at $($f.L),$($f.T)"
if ($SizeOnly) { return }

New-Item -ItemType Directory -Force $Out | Out-Null
$bmp = New-Object System.Drawing.Bitmap ($f.R - $f.L), ($f.B - $f.T)
$g = [System.Drawing.Graphics]::FromImage($bmp)
$g.CopyFromScreen($f.L, $f.T, 0, 0, $bmp.Size)
$g.Dispose()
$path = Join-Path $Out "$Name.png"
$bmp.Save($path, [System.Drawing.Imaging.ImageFormat]::Png)
$bmp.Dispose()
"Saved $path"
