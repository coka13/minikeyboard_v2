$ErrorActionPreference = 'Stop'
Push-Location $PSScriptRoot
try {
    New-Item -ItemType Directory -Path .tools -Force | Out-Null
    & "$env:WINDIR\Microsoft.NET\Framework64\v4.0.30319\csc.exe" /nologo /target:exe /platform:x86 /out:.tools\LiveInspectorTests.exe /reference:System.Windows.Forms.dll /reference:System.Drawing.dll .\src\LiveInspector.cs .\tests\LiveInspectorTests.cs
    if ($LASTEXITCODE -ne 0) { throw 'Test compilation failed.' }
    & .\.tools\LiveInspectorTests.exe
    if ($LASTEXITCODE -ne 0) { throw 'Live inspector tests failed.' }
} finally { Pop-Location }
