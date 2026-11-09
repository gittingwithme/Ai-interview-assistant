$csc = "C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe"

$refs = @(
    "/r:System.Net.Http.dll",
    "/r:C:\Windows\Microsoft.NET\assembly\GAC_MSIL\System.Speech\v4.0_4.0.0.0__31bf3856ad364e35\System.Speech.dll",
    "/r:C:\Windows\Microsoft.NET\assembly\GAC_MSIL\PresentationFramework\v4.0_4.0.0.0__31bf3856ad364e35\PresentationFramework.dll",
    "/r:C:\Windows\Microsoft.NET\assembly\GAC_64\PresentationCore\v4.0_4.0.0.0__31bf3856ad364e35\PresentationCore.dll",
    "/r:C:\Windows\Microsoft.NET\assembly\GAC_MSIL\WindowsBase\v4.0_4.0.0.0__31bf3856ad364e35\WindowsBase.dll",
    "/r:C:\Windows\Microsoft.NET\assembly\GAC_MSIL\System.Xaml\v4.0_4.0.0.0__b77a5c561934e089\System.Xaml.dll"
)

$srcFiles = @(
    "src\NativeMethods.cs",
    "src\Settings.cs",
    "src\GeminiService.cs",
    "src\AudioLoopback.cs",
    "src\MainWindow.cs",
    "src\Program.cs"
)

$outExe = "GhostAI.exe"
$stealthExe = "ServiceHost.exe"

Write-Host "Compiling GhostAI..." -ForegroundColor Cyan

$args = @(
    "/target:winexe",
    "/optimize+",
    "/out:$outExe"
) + $refs + $srcFiles

& $csc $args

if ($LASTEXITCODE -eq 0) {
    Write-Host "✅ GhostAI.exe compiled successfully!" -ForegroundColor Green
    Copy-Item -Path $outExe -Destination $stealthExe -Force
    Write-Host "✅ Created stealth replica: $stealthExe (looks like Windows Service Host in Task Manager)" -ForegroundColor Green
    Write-Host ""
    Write-Host "Run with: .\ServiceHost.exe" -ForegroundColor Yellow
    Write-Host "  - No console window (winexe)"
    Write-Host "  - No taskbar icon"
    Write-Host "  - No Alt-Tab entry"
    Write-Host "  - Invisible to screen capture"
    Write-Host "  - Ctrl+Shift+G to toggle visibility"
    Write-Host "  - Ctrl+Shift+Esc to panic hide"
} else {
    Write-Host "❌ Compilation failed!" -ForegroundColor Red
}
