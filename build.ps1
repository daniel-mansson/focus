$ErrorActionPreference = "Stop"

# Read version from focus.csproj
[xml]$csproj = Get-Content "focus/focus.csproj"
$version = (Select-Xml -Xml $csproj -XPath "//Version").Node.InnerText

if (-not $version) {
    throw "Could not read Version from focus/focus.csproj"
}

Write-Host "Building Focus v$version"

# Run dotnet publish (self-contained single-file)
Write-Host "`nPublishing focus.exe..."
dotnet publish focus/focus.csproj `
    -c Release `
    -r win-x64 `
    --self-contained true `
    -p:PublishSingleFile=true `
    -p:IncludeNativeLibrariesForSelfExtract=true

if ($LASTEXITCODE -ne 0) {
    throw "dotnet publish failed with exit code $LASTEXITCODE"
}

# Verify single-file output
$publishDir = "focus/bin/Release/net8.0-windows/win-x64/publish"
$publishedExe = Join-Path $publishDir "focus.exe"

if (-not (Test-Path $publishedExe)) {
    throw "Published exe not found at $publishedExe"
}

$fileCount = (Get-ChildItem $publishDir -File).Count
if ($fileCount -gt 1) {
    Write-Warning "Publish directory contains $fileCount files (expected 1). Native DLLs may not be embedded."
}

Write-Host "Published: $publishedExe"

# Compile installer with ISCC
Write-Host "`nCompiling installer..."
ISCC.exe /DMyAppVersion="$version" installer/focus.iss

if ($LASTEXITCODE -ne 0) {
    throw "ISCC.exe failed with exit code $LASTEXITCODE"
}

# Report result
$setupExe = "installer/output/Focus-Setup.exe"

if (-not (Test-Path $setupExe)) {
    throw "Installer was not created at $setupExe"
}

$sizeMB = [math]::Round((Get-Item $setupExe).Length / 1MB, 2)
Write-Host "`nBuild complete: $setupExe ($sizeMB MB)"
