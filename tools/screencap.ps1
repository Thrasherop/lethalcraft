param([string]$out = "E:\claude\mods\lethal_minecraft\shots\sc.png", [int]$w = 960)
Add-Type -AssemblyName System.Drawing
Add-Type @"
using System; using System.Runtime.InteropServices;
public class Dpi { [DllImport("user32.dll")] public static extern bool SetProcessDPIAware(); [DllImport("user32.dll")] public static extern int GetSystemMetrics(int i); }
"@
[Dpi]::SetProcessDPIAware() | Out-Null
$W = [Dpi]::GetSystemMetrics(0); $H = [Dpi]::GetSystemMetrics(1)
$bmp = New-Object System.Drawing.Bitmap $W, $H
$g = [System.Drawing.Graphics]::FromImage($bmp)
$g.CopyFromScreen(0, 0, 0, 0, (New-Object System.Drawing.Size $W, $H))
$small = New-Object System.Drawing.Bitmap $bmp, $w, ([int]($w * $H / $W))
$small.Save($out, [System.Drawing.Imaging.ImageFormat]::Png)
