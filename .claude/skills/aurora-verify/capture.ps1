# Captures a window's client area, or a client-relative Region "x,y,w,h", to a PNG.
param(
    [Parameter(Mandatory = $true)] [string] $Process,
    [Parameter(Mandatory = $true)] [string] $Out,
    [string] $Region
)

Add-Type -AssemblyName System.Drawing
Add-Type @"
using System;
using System.Runtime.InteropServices;
public static class AuroraCapture
{
    [StructLayout(LayoutKind.Sequential)] public struct RECT { public int Left, Top, Right, Bottom; }
    [StructLayout(LayoutKind.Sequential)] public struct POINT { public int X, Y; }
    [DllImport("user32.dll")] public static extern bool SetProcessDPIAware();
    [DllImport("user32.dll")] public static extern bool SetWindowPos(IntPtr hWnd, IntPtr after, int x, int y, int cx, int cy, uint flags);
    [DllImport("user32.dll")] public static extern bool GetClientRect(IntPtr hWnd, out RECT rect);
    [DllImport("user32.dll")] public static extern bool ClientToScreen(IntPtr hWnd, ref POINT point);
}
"@

[AuroraCapture]::SetProcessDPIAware() | Out-Null

$proc = Get-Process -Name $Process -ErrorAction Stop | Where-Object { $_.MainWindowHandle -ne [IntPtr]::Zero } | Select-Object -First 1
if (-not $proc) { throw "No window for process '$Process'" }
$hwnd = $proc.MainWindowHandle
$Out = $ExecutionContext.SessionState.Path.GetUnresolvedProviderPathFromPSPath($Out)

$client = New-Object AuroraCapture+RECT
[AuroraCapture]::GetClientRect($hwnd, [ref]$client) | Out-Null
$origin = New-Object AuroraCapture+POINT
[AuroraCapture]::ClientToScreen($hwnd, [ref]$origin) | Out-Null

if ($Region) {
    $r = $Region -split ',' | ForEach-Object { [int]$_ }
    $x = $origin.X + $r[0]; $y = $origin.Y + $r[1]; $w = $r[2]; $h = $r[3]
} else {
    $x = $origin.X; $y = $origin.Y; $w = $client.Right; $h = $client.Bottom
}

# SWP_NOSIZE | SWP_NOMOVE | SWP_SHOWWINDOW
$flags = 0x43
[AuroraCapture]::SetWindowPos($hwnd, [IntPtr](-1), 0, 0, 0, 0, $flags) | Out-Null
try {
    Start-Sleep -Milliseconds 150
    $bitmap = New-Object System.Drawing.Bitmap $w, $h
    $graphics = [System.Drawing.Graphics]::FromImage($bitmap)
    $graphics.CopyFromScreen($x, $y, 0, 0, $bitmap.Size)
    $bitmap.Save($Out, [System.Drawing.Imaging.ImageFormat]::Png)
    $graphics.Dispose()
    $bitmap.Dispose()
}
finally {
    [AuroraCapture]::SetWindowPos($hwnd, [IntPtr](-2), 0, 0, 0, 0, $flags) | Out-Null
}
"$Out ${w}x${h}"
