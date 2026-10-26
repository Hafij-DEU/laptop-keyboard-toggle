# Builds ShiftFix.exe with the C# compiler that ships with Windows (.NET Framework 4).
$ErrorActionPreference = 'Stop'
Set-Location $PSScriptRoot
Add-Type -AssemblyName System.Drawing

# Exe icon: the same blue Shift arrow the tray uses.
$bmp = New-Object System.Drawing.Bitmap 32, 32
$g = [System.Drawing.Graphics]::FromImage($bmp)
$g.SmoothingMode = 'AntiAlias'
$g.FillEllipse((New-Object System.Drawing.SolidBrush ([System.Drawing.Color]::FromArgb(53, 132, 228))), 1, 1, 30, 30)
$pts = @((16,5),(27,17),(21,17),(21,26),(11,26),(11,17),(5,17)) | ForEach-Object { New-Object System.Drawing.Point $_[0], $_[1] }
$g.FillPolygon([System.Drawing.Brushes]::White, [System.Drawing.Point[]]$pts)
$g.Dispose()
$fs = [System.IO.File]::Create("$PSScriptRoot\ShiftFix.ico")
[System.Drawing.Icon]::FromHandle($bmp.GetHicon()).Save($fs)
$fs.Close()

$csc = "$env:WINDIR\Microsoft.NET\Framework64\v4.0.30319\csc.exe"
& $csc /nologo /target:winexe /platform:x64 /optimize+ /win32icon:ShiftFix.ico /out:ShiftFix.exe `
    /r:System.Windows.Forms.dll /r:System.Drawing.dll ShiftFix.cs
if ($LASTEXITCODE -ne 0) { throw "build failed" }
Get-Item ShiftFix.exe | Select-Object Name, Length
