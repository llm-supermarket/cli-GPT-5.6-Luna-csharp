param([Parameter(Mandatory = $true)][string]$Version)
$ErrorActionPreference = 'Stop'

$repo = 'llm-supermarket/cli-GPT-5.6-Luna-csharp'
$base = "https://github.com/$repo/releases/download/v$Version"
$asset = "$base/cli-GPT-5.6-Luna-csharp-win-x64.zip"
$zip = Join-Path $env:TEMP "cli-GPT-5.6-Luna-csharp-$Version.zip"
Invoke-WebRequest -Uri $asset -OutFile $zip
$hash = (Get-FileHash $zip -Algorithm SHA256).Hash.ToLowerInvariant()
$manifest = [ordered]@{
  homepage = "https://github.com/$repo"
  description = 'Cross-platform rclone-compatible file encryption CLI'
  version = $Version
  architecture = [ordered]@{ '64bit' = [ordered]@{
    url = $asset
    hash = $hash
    extract_dir = ''
    bin = 'cli-GPT-5.6-Luna-csharp-win-x64.exe'
  }}
} | ConvertTo-Json -Depth 5
Set-Content -Path 'cli-GPT-5.6-Luna-csharp.json' -Value $manifest -Encoding utf8
