<#
  MacUI end-to-end tests.

  Builds MacUI, starts it fresh, drives it with real mouse/keyboard input and checks the results through
  the Windows API and UI Automation. Takes about 2 minutes. Don't touch the mouse or keyboard while it runs.

    powershell -ExecutionPolicy Bypass -File tests\run-tests.ps1 [-SkipBuild] [-Screenshots <folder>]

  It opens and closes a few File Explorer / Notepad windows of its own, and leaves MacUI running at the end.
#>
param([switch]$SkipBuild, [string]$Screenshots = "")

$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot
$exe = Join-Path $root "MacUI.exe"
$dataDir = Join-Path $env:APPDATA "MacUI"
Add-Type -AssemblyName System.Drawing, System.Windows.Forms, UIAutomationClient, UIAutomationTypes

Add-Type @'
using System; using System.Collections.Generic; using System.Runtime.InteropServices; using System.Text;
public static class T {
  public delegate bool P(IntPtr h, IntPtr l);
  [DllImport("user32.dll")] public static extern bool EnumWindows(P p, IntPtr l);
  [DllImport("user32.dll", CharSet=CharSet.Unicode)] public static extern int GetWindowText(IntPtr h, StringBuilder s, int n);
  [DllImport("user32.dll", CharSet=CharSet.Unicode)] public static extern int GetClassName(IntPtr h, StringBuilder s, int n);
  [DllImport("user32.dll", CharSet=CharSet.Unicode)] public static extern IntPtr FindWindow(string c, string t);
  [DllImport("user32.dll")] public static extern bool IsWindow(IntPtr h);
  [DllImport("user32.dll")] public static extern bool IsWindowVisible(IntPtr h);
  [DllImport("user32.dll")] public static extern bool IsIconic(IntPtr h);
  [DllImport("user32.dll")] public static extern bool IsZoomed(IntPtr h);
  [DllImport("user32.dll")] public static extern IntPtr GetWindow(IntPtr h, uint c);
  [DllImport("user32.dll")] public static extern int GetWindowLong(IntPtr h, int i);
  [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr h, out RECT r);
  [DllImport("user32.dll")] public static extern IntPtr GetForegroundWindow();
  [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr h);
  [DllImport("user32.dll")] public static extern bool ShowWindowAsync(IntPtr h, int c);
  [DllImport("user32.dll")] public static extern bool MoveWindow(IntPtr h, int x, int y, int w, int hh, bool r);
  [DllImport("user32.dll")] public static extern bool PostMessage(IntPtr h, uint m, IntPtr w, IntPtr l);
  [DllImport("user32.dll")] public static extern uint GetWindowThreadProcessId(IntPtr h, out uint pid);
  [DllImport("user32.dll")] public static extern bool SetCursorPos(int x, int y);
  [DllImport("user32.dll")] public static extern void mouse_event(uint f, int x, int y, uint d, UIntPtr e);
  [DllImport("user32.dll")] public static extern void keybd_event(byte vk, byte scan, uint flags, UIntPtr extra);
  [DllImport("user32.dll")] public static extern int GetSystemMetrics(int i);
  [DllImport("user32.dll")] public static extern bool SystemParametersInfo(uint a, uint p, ref RECT r, uint w);
  [DllImport("user32.dll")] public static extern bool SystemParametersInfo(uint a, uint p, ref ANIM r, uint w);
  [DllImport("user32.dll")] public static extern bool SetProcessDPIAware();
  [DllImport("dwmapi.dll")] public static extern int DwmGetWindowAttribute(IntPtr h, int a, out RECT r, int s);
  [DllImport("dwmapi.dll")] public static extern int DwmGetWindowAttribute(IntPtr h, int a, out int v, int s);
  public struct RECT { public int L, T, R, B; public override string ToString() { return L + "," + T + "," + R + "," + B; } }
  public struct ANIM { public uint cb; public int min; }

  public static string Title(IntPtr h) { var sb = new StringBuilder(512); GetWindowText(h, sb, 512); return sb.ToString(); }
  public static string Class(IntPtr h) { var sb = new StringBuilder(256); GetClassName(h, sb, 256); return sb.ToString(); }
  public static RECT Rect(IntPtr h) { RECT r; GetWindowRect(h, out r); return r; }
  public static RECT Frame(IntPtr h) { RECT r; if (DwmGetWindowAttribute(h, 9, out r, 16) != 0) GetWindowRect(h, out r); return r; }
  public static bool Cloaked(IntPtr h) { int c; return DwmGetWindowAttribute(h, 14, out c, 4) == 0 && c != 0; }
  public static bool HasSysMenu(IntPtr h) { return (GetWindowLong(h, -16) & 0x80000) != 0; }
  public static bool Topmost(IntPtr h) { return (GetWindowLong(h, -20) & 0x8) != 0; }
  public static RECT WorkArea() { var r = new RECT(); SystemParametersInfo(0x30, 0, ref r, 0); return r; }
  public static int MinAnimate() { var a = new ANIM { cb = 8 }; SystemParametersInfo(0x48, 8, ref a, 0); return a.min; }
  public static uint Pid(IntPtr h) { uint p; GetWindowThreadProcessId(h, out p); return p; }

  // Top-level windows whose title starts with the prefix (MacUI names its windows "MacUI ...").
  public static List<IntPtr> ByTitle(string prefix) {
    var list = new List<IntPtr>();
    EnumWindows((h, l) => { if (Title(h).StartsWith(prefix)) list.Add(h); return true; }, IntPtr.Zero);
    return list;
  }
  public static List<IntPtr> ByClass(string cls) {
    var list = new List<IntPtr>();
    EnumWindows((h, l) => { if (Class(h) == cls && IsWindowVisible(h) && !Cloaked(h)) list.Add(h); return true; }, IntPtr.Zero);
    return list;
  }
  // Normal app windows, front to back (what Alt+Tab would list).
  public static List<IntPtr> AppWindows() {
    var list = new List<IntPtr>();
    EnumWindows((h, l) => {
      if (!IsWindowVisible(h) || GetWindow(h, 4) != IntPtr.Zero || (GetWindowLong(h, -20) & 0x80) != 0 || Cloaked(h)) return true;
      string t = Title(h); if (t.Length == 0 || t.StartsWith("MacUI") || t == "Spotlight" || t == "Program Manager") return true;
      list.Add(h); return true; }, IntPtr.Zero);
    return list;
  }
  [DllImport("user32.dll")] public static extern IntPtr WindowFromPoint(POINT p);
  [DllImport("user32.dll")] public static extern IntPtr GetAncestor(IntPtr h, uint f);
  public struct POINT { public int X, Y; }
  // A point on the window's title bar that isn't covered by another window (x, or -1 if there is none).
  public static int FreeTitlePoint(IntPtr h) {
    RECT f = Frame(h);
    for (int x = f.L + 30; x < f.R - 160; x += 15) {
      IntPtr at = WindowFromPoint(new POINT { X = x, Y = f.T + 8 });
      if (at != IntPtr.Zero && GetAncestor(at, 2) == h) return x;
    }
    return -1;
  }
  public static void Click(int x, int y) { SetCursorPos(x, y); System.Threading.Thread.Sleep(60); mouse_event(2, 0, 0, 0, UIntPtr.Zero); mouse_event(4, 0, 0, 0, UIntPtr.Zero); }
  public static void Down(byte vk) { keybd_event(vk, 0, (vk == 0x5B ? 1u : 0u), UIntPtr.Zero); }
  public static void Up(byte vk) { keybd_event(vk, 0, (vk == 0x5B ? 1u : 0u) | 2u, UIntPtr.Zero); }
  public static void Tap(byte vk) { Down(vk); Up(vk); }
}
'@
[T]::SetProcessDPIAware() | Out-Null
$screenW = [T]::GetSystemMetrics(0); $screenH = [T]::GetSystemMetrics(1)

# ---------------- tiny test framework ----------------
$results = New-Object System.Collections.Generic.List[object]
$section = ""
function Section($name) { $script:section = $name; Write-Host "`n== $name ==" -ForegroundColor Cyan }
function Check($name, [bool]$ok, $detail = "") {
  $results.Add([pscustomobject]@{ Section = $script:section; Test = $name; Pass = $ok; Detail = "$detail" })
  if ($ok) { Write-Host "  PASS  $name" -ForegroundColor Green } else { Write-Host "  FAIL  $name  $detail" -ForegroundColor Red }
}
function WaitFor([scriptblock]$condition, [int]$timeoutMs = 3000) {
  $sw = [Diagnostics.Stopwatch]::StartNew()
  while ($sw.ElapsedMilliseconds -lt $timeoutMs) { if (& $condition) { return $true }; Start-Sleep -Milliseconds 50 }
  return [bool](& $condition)
}
function Shot($name) {
  if (-not $Screenshots) { return }
  New-Item -ItemType Directory -Force $Screenshots | Out-Null
  $b = New-Object System.Drawing.Bitmap $screenW, $screenH; $g = [System.Drawing.Graphics]::FromImage($b)
  $g.CopyFromScreen(0, 0, 0, 0, $b.Size); $g.Dispose(); $b.Save((Join-Path $Screenshots "$name.png")); $b.Dispose()
}

# ---------------- MacUI's own windows ----------------
function Visible($title) { $w = [T]::ByTitle($title) | Where-Object { [T]::IsWindowVisible($_) -and [T]::Rect($_).L -gt -30000 }; return @($w).Count -gt 0 }
function LightsFor($h) { [T]::ByTitle("MacUI Window Buttons $h") | Where-Object { [T]::Rect($_).L -gt -30000 } | Select-Object -First 1 }
function CoverFor($h) { [T]::ByTitle("MacUI Button Cover $h") | Where-Object { [T]::Rect($_).L -gt -30000 } | Select-Object -First 1 }
function AllLights { [T]::ByTitle("MacUI Window Buttons ") | Where-Object { [T]::Rect($_).L -gt -30000 } }
function LightsAtTopRight($h) {
  $l = LightsFor $h; if (-not $l) { return $false }
  $r = [T]::Rect($l); $f = [T]::Frame($h)
  return ($r.R -le $f.R) -and ($r.R -ge $f.R - 20) -and ($r.T -ge $f.T - 2) -and ($r.B -le $f.T + 60)
}
# Lights of OTHER windows showing inside this window's area (= buttons floating on top of the wrong window).
function ForeignLightsInside($h) {
  $f = [T]::Frame($h)
  @(AllLights | Where-Object { -not ([T]::Title($_) -eq "MacUI Window Buttons $h") } | Where-Object { $r = [T]::Rect($_); $r.L -ge $f.L -and $r.R -le $f.R -and $r.T -ge $f.T -and $r.B -le $f.B })
}
function ClickLight($h, $index) {   # 0 = red, 1 = yellow, 2 = green
  $l = LightsFor $h; if (-not $l) { return }
  $r = [T]::Rect($l); [T]::Click($r.L + 10 + 20 * $index, $r.T + 10)
}
function DockIcon($name) {
  $dock = [T]::ByTitle("MacUI Dock") | Select-Object -First 1
  $A = [System.Windows.Automation.AutomationElement]
  $cond = New-Object System.Windows.Automation.PropertyCondition($A::NameProperty, $name)
  return $A::FromHandle($dock).FindFirst([System.Windows.Automation.TreeScope]::Descendants, $cond)
}
function RevealDock { [T]::SetCursorPos([int]($screenW / 2), $screenH - 1) | Out-Null; Start-Sleep -Milliseconds 450 }
function NewWindow($cls, [scriptblock]$start) {
  $before = @([T]::ByClass($cls))
  & $start
  $new = $null
  WaitFor { $script:new = @([T]::ByClass($cls)) | Where-Object { $before -notcontains $_ } | Select-Object -First 1; [bool]$script:new } 8000 | Out-Null
  return $script:new
}
function Focus($h) {
  # Clicking a free spot of the title bar brings a window to the front without pressing anything inside it;
  # if other windows cover the whole title bar, minimize + restore it instead.
  $x = [T]::FreeTitlePoint($h)
  if ($x -ge 0) { $f = [T]::Frame($h); [T]::Click($x, $f.T + 8); Start-Sleep -Milliseconds 400 }
  else { [T]::ShowWindowAsync($h, 6) | Out-Null; Start-Sleep -Milliseconds 700; [T]::ShowWindowAsync($h, 9) | Out-Null; Start-Sleep -Milliseconds 900 }
}
function RectOf($w) { if ($w) { "$([T]::Rect($w))" } else { "none" } }
function Raise($h) {
  # Minimize + restore always brings a window to the very front, even when other windows cover its title bar.
  [T]::ShowWindowAsync($h, 6) | Out-Null; Start-Sleep -Milliseconds 700
  [T]::ShowWindowAsync($h, 9) | Out-Null; Start-Sleep -Milliseconds 900
  if ([T]::GetForegroundWindow() -ne $h) { Focus $h; Start-Sleep -Milliseconds 500 }
}
# Every MacUI button window must belong to a window that still exists, and no window may have two sets.
function Duplicates {
  $ids = @([T]::ByTitle("MacUI Window Buttons ") | ForEach-Object { ([T]::Title($_) -split ' ')[-1] })
  @($ids | Group-Object | Where-Object Count -gt 1 | ForEach-Object Name)
}
function Orphans {
  @([T]::ByTitle("MacUI Window Buttons ") | ForEach-Object { [long](([T]::Title($_) -split ' ')[-1]) } | Where-Object { -not [T]::IsWindow([IntPtr]$_) -or -not [T]::IsWindowVisible([IntPtr]$_) })
}
# The app's own minimize/maximize/close: 2-3 same-sized buttons in a row touching the top-right corner.
function OwnButtonRow($rects, $frameRight) {
  foreach ($first in ($rects | Where-Object { [Math]::Abs($_.Right - $frameRight) -le 4 } | Sort-Object Height -Descending)) {
    $neighbour = $rects | Where-Object { [Math]::Abs($_.Right - $first.Left) -le 8 -and [Math]::Abs($_.Height - $first.Height) -le 4 -and [Math]::Abs($_.Top - $first.Top) -le 4 } | Select-Object -First 1
    if ($neighbour) { return $first }
  }
  return $null
}
function FillsMonitor($h) {
  $f = [T]::Frame($h); $m = [System.Windows.Forms.Screen]::FromHandle($h).Bounds
  return $f.L -le $m.Left -and $f.T -le $m.Top -and $f.R -ge $m.Right -and $f.B -ge $m.Bottom
}
function LogLines { $p = Join-Path $dataDir "log.txt"; if (Test-Path $p) { @(Get-Content $p) } else { @() } }

# ================================================================
Section "Build"
if (-not $SkipBuild) {
  if (Get-Process MacUI -ErrorAction SilentlyContinue) { & $exe --quit; Start-Sleep 2 }
  $buildOutput = cmd /c "`"$root\build.cmd`"" 2>&1
  Check "build.cmd compiles without errors" ($LASTEXITCODE -eq 0) ($buildOutput -join " ")
}
Check "MacUI.exe exists" (Test-Path $exe)

Section "Startup"
if (Get-Process MacUI -ErrorAction SilentlyContinue) { & $exe --quit; Start-Sleep 2 }
$logBefore = (LogLines).Count
$originalMinAnimate = [T]::MinAnimate()
Start-Process $exe
$started = WaitFor { Visible "MacUI Menu Bar" } 8000
Check "starts and shows the menu bar" $started
Start-Sleep 2
$proc = Get-Process MacUI -ErrorAction SilentlyContinue
Check "exactly one MacUI process" (@($proc).Count -eq 1) "count=$(@($proc).Count)"
Check "process is responding" ([bool]($proc -and $proc.Responding))
$tray = [T]::FindWindow("Shell_TrayWnd", [NullString]::Value)
Check "Windows taskbar is hidden" (WaitFor { -not [T]::IsWindowVisible($tray) } 3000)
Start-Sleep 1.5
Check "Windows taskbar stays hidden" (-not [T]::IsWindowVisible($tray))
$bar = [T]::ByTitle("MacUI Menu Bar") | Select-Object -First 1; $br = [T]::Rect($bar)
Check "menu bar spans the top of the main screen" ($br.L -eq 0 -and $br.T -eq 0 -and $br.R -eq $screenW -and ($br.B - $br.T) -ge 20 -and ($br.B - $br.T) -le 40) "$br"
Check "menu bar is always on top" ([T]::Topmost($bar))
Check "maximized windows start below the menu bar" ([T]::WorkArea().T -eq $br.B) "workArea=$([T]::WorkArea()) bar=$br"
Check "Windows' minimize animation is off while MacUI runs" ([T]::MinAnimate() -eq 0)

# A second copy must refuse to start (it shows a message box and exits).
$second = Start-Process $exe -PassThru; Start-Sleep 2
Check "a second copy doesn't create another menu bar" (@([T]::ByTitle("MacUI Menu Bar")).Count -eq 1)
if (-not $second.HasExited) { Stop-Process -Id $second.Id -Force }

Section "Dock"
$dock = [T]::ByTitle("MacUI Dock") | Select-Object -First 1
Check "Dock window exists and is always on top" ($dock -and [T]::Topmost($dock))
[T]::SetCursorPos([int]($screenW / 2), [int]($screenH / 2)) | Out-Null
foreach ($name in "Launchpad", "Finder", "Trash") { Check "Dock has '$name'" ([bool](DockIcon $name)) }
$hiddenTop = { $i = DockIcon "Finder"; $i -and $i.Current.BoundingRectangle.Top -ge $screenH - 4 }
Check "Dock hides itself when the mouse is away" (WaitFor $hiddenTop 4000) "top=$((DockIcon 'Finder').Current.BoundingRectangle.Top)"
RevealDock
Check "Dock slides up when the mouse touches the bottom edge" (WaitFor { $i = DockIcon "Finder"; $i.Current.BoundingRectangle.Bottom -le $screenH - 6 } 1500)
Shot "dock-revealed"
[T]::SetCursorPos([int]($screenW / 2), [int]($screenH / 2)) | Out-Null
Check "Dock hides again after the mouse leaves" (WaitFor $hiddenTop 3000)

Section "Window buttons: standard window (File Explorer)"
$ex = NewWindow "CabinetWClass" { Start-Process explorer.exe -ArgumentList "C:\Windows" }
Check "test window opened" ([bool]$ex)
if ($ex) {
  [T]::MoveWindow($ex, 200, 160, 1000, 620, $true) | Out-Null
  Focus $ex
  Check "Windows' own buttons removed (system menu off)" (WaitFor { -not [T]::HasSysMenu($ex) } 3000)
  Check "macOS buttons at the top-right" (WaitFor { LightsAtTopRight $ex } 3000) "lights=$(RectOf (LightsFor $ex)) frame=$([T]::Frame($ex))"
  Check "exactly one set of macOS buttons for the window" (@([T]::ByTitle("MacUI Window Buttons $ex")).Count -eq 1)
  Check "no colour patch needed on a standard title bar" (-not (CoverFor $ex))
  Check "no other window's buttons on top of it" ((ForeignLightsInside $ex).Count -eq 0)
  Shot "explorer-normal"

  ClickLight $ex 2
  Check "green button maximizes" (WaitFor { [T]::IsZoomed($ex) } 2000)
  Check "buttons follow to the new top-right" (WaitFor { LightsAtTopRight $ex } 2000) "lights=$(RectOf (LightsFor $ex)) frame=$([T]::Frame($ex))"
  Start-Sleep -Milliseconds 400
  Check "maximized: only its own buttons visible on it" ((ForeignLightsInside $ex).Count -eq 0) "foreign=$((ForeignLightsInside $ex | ForEach-Object { [T]::Title($_) }) -join ', ')"
  Check "maximized: Windows' buttons stay removed" (-not [T]::HasSysMenu($ex))
  Check "maximized: buttons sit below the menu bar" ((LightsFor $ex) -and [T]::Rect((LightsFor $ex)).T -ge $br.B)
  Shot "explorer-maximized"

  ClickLight $ex 2
  Check "green button again restores the size" (WaitFor { -not [T]::IsZoomed($ex) } 2000)
  Check "buttons move back with the window" (WaitFor { LightsAtTopRight $ex } 2000)

  [T]::ShowWindowAsync($ex, 3) | Out-Null
  Check "maximize from Windows (Win+Up / title bar) also keeps one set" ((WaitFor { [T]::IsZoomed($ex) -and (LightsAtTopRight $ex) } 2500) -and (ForeignLightsInside $ex).Count -eq 0)
  [T]::ShowWindowAsync($ex, 9) | Out-Null; Start-Sleep -Milliseconds 600

  $genieSeen = $false
  ClickLight $ex 1
  $sw = [Diagnostics.Stopwatch]::StartNew()
  while ($sw.ElapsedMilliseconds -lt 900) { if (Visible "MacUI Animation") { $genieSeen = $true; break }; Start-Sleep -Milliseconds 20 }
  Check "yellow button minimizes" (WaitFor { [T]::IsIconic($ex) } 2000)
  Check "genie animation plays" $genieSeen
  Check "buttons disappear while minimized" (WaitFor { -not (LightsFor $ex) } 1500)
  Start-Sleep 1
  Check "animation window cleans up afterwards" (-not (Visible "MacUI Animation"))

  [T]::ShowWindowAsync($ex, 9) | Out-Null
  Check "restored window gets its buttons back" (WaitFor { (-not [T]::IsIconic($ex)) -and (LightsAtTopRight $ex) } 3000)

  Focus $ex
  ClickLight $ex 0
  Check "red button closes the window" (WaitFor { -not [T]::IsWindow($ex) } 3000)
  Check "its buttons are removed with it" (WaitFor { @([T]::ByTitle("MacUI Window Buttons $ex")).Count -eq 0 } 2000)
}

Section "Window buttons: Notepad (modern title bar)"
$np = NewWindow "Notepad" { Start-Process notepad.exe }
if ($np) {
  [T]::MoveWindow($np, 260, 200, 900, 560, $true) | Out-Null
  Focus $np
  Check "Windows' own buttons removed" (WaitFor { -not [T]::HasSysMenu($np) } 3000)
  Check "macOS buttons at the top-right" (WaitFor { LightsAtTopRight $np } 3000)
  ClickLight $np 2
  Check "green maximizes and buttons follow" ((WaitFor { [T]::IsZoomed($np) -and (LightsAtTopRight $np) } 2500) -and (ForeignLightsInside $np).Count -eq 0)
  ClickLight $np 2; Start-Sleep -Milliseconds 500
  Shot "notepad"
} else { Check "Notepad window opened" $false "skipped (Notepad opened as a tab or not installed)" }

Section "Window buttons: apps that draw their own title bar"
$A = [System.Windows.Automation.AutomationElement]
$btnCond = New-Object System.Windows.Automation.PropertyCondition($A::ControlTypeProperty, [System.Windows.Automation.ControlType]::Button)
$customClasses = "Chrome_WidgetWin_1", "MozillaWindowClass", "CASCADIA_HOSTING_WINDOW_CLASS"
$tested = 0
foreach ($h in [T]::AppWindows()) {
  if ($customClasses -notcontains [T]::Class($h) -or [T]::IsIconic($h)) { continue }
  $name = (Get-Process -Id ([T]::Pid($h))).ProcessName
  if (FillsMonitor $h) { Write-Host "  skip  $name (fills the whole monitor, e.g. a full-screen video)"; continue }
  Raise $h
  $f = [T]::Frame($h)
  if ([T]::IsZoomed($h)) { $wa = [System.Windows.Forms.Screen]::FromHandle($h).WorkingArea; $f.T = [Math]::Max($f.T, $wa.Top); $f.R = [Math]::Min($f.R, $wa.Right) }
  $rects = @($A::FromHandle($h).FindAll([System.Windows.Automation.TreeScope]::Descendants, $btnCond) | ForEach-Object { $_.Current.BoundingRectangle } |
    Where-Object { $_.Width -ge 8 -and $_.Bottom -gt $f.T -and $_.Top -ge $f.T - 16 -and $_.Bottom -le $f.T + 80 })
  $edge = OwnButtonRow $rects $f.R
  $lightsOk = LightsAtTopRight $h
  Check "$name : macOS buttons at the top-right" $lightsOk "frame=$f lights=$(RectOf (LightsFor $h))"
  $cover = CoverFor $h
  if ($edge -or [T]::Class($h) -eq "CASCADIA_HOSTING_WINDOW_CLASS") {
    $c = if ($cover) { [T]::Rect($cover) } else { $null }
    $covered = $c -and $c.R -ge $f.R - 1 -and $c.T -le $f.T + 2 -and (-not $edge -or ($c.B -ge $edge.Bottom - 2 -and $c.L -le $edge.Left))
    Check "$name : its own window buttons are covered" $covered "cover=$c closeButton=$edge"
  }
  Check "$name : no other window's buttons on top of it" ((ForeignLightsInside $h).Count -eq 0)
  if ($Screenshots) {
    $b = New-Object System.Drawing.Bitmap 420, 60; $g = [System.Drawing.Graphics]::FromImage($b); $g.CopyFromScreen($f.R - 420, $f.T, 0, 0, $b.Size); $g.Dispose()
    New-Item -ItemType Directory -Force $Screenshots | Out-Null; $b.Save((Join-Path $Screenshots "corner-$name-$h.png")); $b.Dispose()
  }
  $tested++
}
Check "found apps with their own title bar to test" ($tested -gt 0) "tested=$tested"

Section "Spotlight"
[T]::Tap(0x5B)
Check "Windows key opens Spotlight" (WaitFor { Visible "Spotlight" } 2000)
Start-Sleep -Milliseconds 300
$fgProc = (Get-Process -Id ([T]::Pid([T]::GetForegroundWindow()))).ProcessName
Check "Windows key gives Spotlight the focus, not the Start menu" ($fgProc -eq "MacUI") "foreground=$fgProc"
Shot "spotlight"
[T]::Tap(0x1B)
Check "Esc closes Spotlight" (WaitFor { -not (Visible "Spotlight") } 1500)

[T]::Down(0x12); [T]::Tap(0x20); [T]::Up(0x12)
Check "Alt+Space opens Spotlight" (WaitFor { Visible "Spotlight" } 2000)
$spot = [T]::ByTitle("Spotlight") | Where-Object { [T]::IsWindowVisible($_) } | Select-Object -First 1
$texts = @()
if ($spot) {
  # Typed through UI Automation so the keyboard layout doesn't matter.
  $ec = New-Object System.Windows.Automation.PropertyCondition($A::ControlTypeProperty, [System.Windows.Automation.ControlType]::Edit)
  $edit = $A::FromHandle($spot).FindFirst([System.Windows.Automation.TreeScope]::Descendants, $ec)
  $edit.GetCurrentPattern([System.Windows.Automation.ValuePattern]::Pattern).SetValue("2+3*4")
  Start-Sleep -Milliseconds 600
  $tc = New-Object System.Windows.Automation.PropertyCondition($A::ControlTypeProperty, [System.Windows.Automation.ControlType]::Text)
  $texts = @($A::FromHandle($spot).FindAll([System.Windows.Automation.TreeScope]::Descendants, $tc) | ForEach-Object { $_.Current.Name })
}
Check "Spotlight calculates 2+3*4 = 14" ($texts -contains "= 14") "texts=$($texts -join ' | ')"
[T]::Tap(0x1B)
Check "Spotlight closes again" (WaitFor { -not (Visible "Spotlight") } 1500)

$dialogsBefore = @([T]::ByClass("#32770"))
[T]::Down(0x5B); [T]::Tap(0x52); [T]::Up(0x5B)
$run = $null
Check "Windows-key shortcuts still work (Win+R opens Run)" (WaitFor { $script:run = @([T]::ByClass("#32770")) | Where-Object { $dialogsBefore -notcontains $_ } | Select-Object -First 1; [bool]$script:run } 3000)
Check "...and don't open Spotlight" (-not (Visible "Spotlight"))
if ($run -and $run -ne [IntPtr]::Zero) { [T]::PostMessage($run, 0x10, [IntPtr]::Zero, [IntPtr]::Zero) | Out-Null }
Start-Sleep -Milliseconds 500

Section "App switcher (Alt+Tab)"
$ex1 = NewWindow "CabinetWClass" { Start-Process explorer.exe -ArgumentList "C:\Windows" }
$ex2 = NewWindow "CabinetWClass" { Start-Process explorer.exe -ArgumentList "C:\Users" }
if ($ex1 -and $ex2) { [T]::MoveWindow($ex1, 150, 150, 900, 600, $true) | Out-Null; [T]::MoveWindow($ex2, 450, 300, 900, 600, $true) | Out-Null; Focus $ex2 }
$fgBefore = [T]::GetForegroundWindow()
[T]::Down(0x12); [T]::Tap(0x09); Start-Sleep -Milliseconds 500
Check "Alt+Tab shows the macOS app switcher" (Visible "MacUI App Switcher")
Shot "switcher"
[T]::Up(0x12)
Check "letting go of Alt closes it" (WaitFor { -not (Visible "MacUI App Switcher") } 1500)
Check "...and switches to another app" (WaitFor { [T]::GetForegroundWindow() -ne $fgBefore } 1500)

if ($ex2) { Focus $ex2 }
[T]::Down(0x12); [T]::Tap(0x09); Start-Sleep -Milliseconds 300
# Step back to the File Explorer entry (the frontmost app) and ask for its windows.
[T]::Down(0x10); [T]::Tap(0x09); [T]::Up(0x10); Start-Sleep -Milliseconds 200
[T]::Tap(0x28)
Check "Down arrow in the switcher shows the app's windows" (WaitFor { Visible "MacUI Windows" } 2000)
[T]::Up(0x12)
Shot "picker-from-switcher"
[T]::Tap(0x1B)
Check "Esc closes the window previews" (WaitFor { -not (Visible "MacUI Windows") } 1500)

Section "Dock: app with several windows"
$finder = DockIcon "Finder"
RevealDock
$finder = DockIcon "Finder"
if ($finder) {
  $r = $finder.Current.BoundingRectangle
  [T]::Click([int]($r.Left + $r.Width / 2), [int]($r.Top + $r.Height / 2))
  Check "clicking Finder with several windows shows window previews" (WaitFor { Visible "MacUI Windows" } 2000)
  Shot "picker-from-dock"
  [T]::Tap(0x1B)
  Check "previews close with Esc" (WaitFor { -not (Visible "MacUI Windows") } 1500)
}
foreach ($w in $ex1, $ex2) { if ($w) { [T]::PostMessage($w, 0x10, [IntPtr]::Zero, [IntPtr]::Zero) | Out-Null } }
if ($np -and [T]::IsWindow($np)) { [T]::PostMessage($np, 0x10, [IntPtr]::Zero, [IntPtr]::Zero) | Out-Null }
Start-Sleep 1

Section "Leftovers"
Start-Sleep 1
Check "no window has two sets of macOS buttons" ((Duplicates).Count -eq 0) "duplicates for: $((Duplicates) -join ', ')"
Check "no buttons left over from closed windows" ((Orphans).Count -eq 0) "orphans for: $((Orphans) -join ', ')"
$lightsCount = @([T]::ByTitle("MacUI Window Buttons ")).Count; $coverCount = @([T]::ByTitle("MacUI Button Cover ")).Count
Check "every button set has exactly one cover window" ($lightsCount -eq $coverCount) "lights=$lightsCount covers=$coverCount"

Section "Errors"
$newLog = @(LogLines | Select-Object -Skip $logBefore)
Check "no errors logged during the tests" ($newLog.Count -eq 0) (($newLog | Select-Object -First 3) -join " ")
$proc = Get-Process MacUI -ErrorAction SilentlyContinue
Check "still running and responding" ([bool]($proc -and $proc.Responding))

Section "Quit restores Windows"
$keep = NewWindow "CabinetWClass" { Start-Process explorer.exe -ArgumentList "C:\Windows" }
Start-Sleep 1.5
& $exe --quit
Check "MacUI quits" (WaitFor { -not (Get-Process MacUI -ErrorAction SilentlyContinue) } 5000)
Check "Windows taskbar is back" ([T]::IsWindowVisible([T]::FindWindow("Shell_TrayWnd", [NullString]::Value)))
Check "menu bar space is given back" ([T]::WorkArea().T -eq 0) "workArea=$([T]::WorkArea())"
if ($keep) { Check "Windows' own buttons are back on open windows" ([T]::HasSysMenu($keep)); [T]::PostMessage($keep, 0x10, [IntPtr]::Zero, [IntPtr]::Zero) | Out-Null }
Check "minimize animation setting restored" ([T]::MinAnimate() -eq $originalMinAnimate -or [T]::MinAnimate() -eq 1) "now=$([T]::MinAnimate()) before=$originalMinAnimate"
Check "no MacUI windows left behind" (@([T]::ByTitle("MacUI")).Count -eq 0)

# Leave MacUI running for the user.
Start-Process $exe
WaitFor { Visible "MacUI Menu Bar" } 8000 | Out-Null

# ---------------- summary ----------------
$failed = @($results | Where-Object { -not $_.Pass })
Write-Host ""
Write-Host ("{0} tests, {1} passed, {2} failed" -f $results.Count, ($results.Count - $failed.Count), $failed.Count) -ForegroundColor $(if ($failed.Count) { "Red" } else { "Green" })
foreach ($f in $failed) { Write-Host ("  FAIL [{0}] {1}  {2}" -f $f.Section, $f.Test, $f.Detail) -ForegroundColor Red }
exit $failed.Count
