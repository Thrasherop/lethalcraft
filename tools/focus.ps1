Add-Type @"
using System; using System.Runtime.InteropServices;
public class F {
 [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr h);
 [DllImport("user32.dll")] public static extern IntPtr GetForegroundWindow();
 [DllImport("user32.dll")] public static extern void keybd_event(byte vk, byte scan, uint flags, UIntPtr extra);
 [DllImport("user32.dll")] public static extern bool ShowWindow(IntPtr h, int c);
}
"@
$g = Get-Process "Lethal Company" -ErrorAction SilentlyContinue | Where-Object { $_.MainWindowHandle -ne 0 } | Select-Object -First 1
if (-not $g) { "no game"; exit 1 }
[F]::keybd_event(0x12, 0, 0, [UIntPtr]::Zero)   # alt down
[F]::keybd_event(0x12, 0, 2, [UIntPtr]::Zero)   # alt up
[F]::ShowWindow($g.MainWindowHandle, 9) | Out-Null
[F]::SetForegroundWindow($g.MainWindowHandle) | Out-Null
Start-Sleep -Milliseconds 200
if ([F]::GetForegroundWindow() -eq $g.MainWindowHandle) { "focused" } else { "NOT focused" }
