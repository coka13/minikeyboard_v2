$ErrorActionPreference = 'Stop'
$compiler = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
if (-not (Test-Path -LiteralPath $compiler)) {
    throw 'The .NET Framework 4 C# compiler is required (included with Windows .NET Framework).'
}
Push-Location $PSScriptRoot
try {
    & $compiler /nologo /target:winexe /platform:x86 /optimize+ '/out:Mini Keyboard Studio.exe' /win32manifest:.\src\Studio.manifest /reference:System.Windows.Forms.dll /reference:System.Drawing.dll .\src\Studio.cs .\src\LiveInspector.cs
    if ($LASTEXITCODE -ne 0) { throw 'Build failed.' }
    Copy-Item -LiteralPath 'MINI KeyBoard.exe.config' -Destination 'Mini Keyboard Studio.exe.config'
    Write-Host 'Built Mini Keyboard Studio.exe'
} finally {
    Pop-Location
}
