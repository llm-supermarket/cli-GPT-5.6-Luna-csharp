param([Parameter(Mandatory = $true)][string]$Version)
$ErrorActionPreference = 'Stop'

$repo = 'llm-supermarket/cli-GPT-5.6-Luna-csharp'
$base = "https://github.com/$repo/releases/download/v$Version"
$assets = @{
  'osx-arm64' = "$base/cli-GPT-5.6-Luna-csharp-osx-arm64.tar.gz"
  'osx-x64' = "$base/cli-GPT-5.6-Luna-csharp-osx-x64.tar.gz"
  'linux-arm64' = "$base/cli-GPT-5.6-Luna-csharp-linux-arm64.tar.gz"
  'linux-x64' = "$base/cli-GPT-5.6-Luna-csharp-linux-x64.tar.gz"
}
$hashes = @{}
foreach ($key in $assets.Keys) {
  $file = Join-Path $env:TEMP "$key-$Version.tar.gz"
  Invoke-WebRequest -Uri $assets[$key] -OutFile $file
  $hashes[$key] = (Get-FileHash $file -Algorithm SHA256).Hash.ToLowerInvariant()
}
$formula = @'
class CliGpt56LunaCsharp < Formula
  desc "Cross-platform rclone-compatible file encryption CLI"
  homepage "https://github.com/__REPO__"
  version "__VERSION__"

  on_macos do
    if Hardware::CPU.arm?
      url "__OSX_ARM64_URL__"
      sha256 "__OSX_ARM64_HASH__"
    else
      url "__OSX_X64_URL__"
      sha256 "__OSX_X64_HASH__"
    end
  end

  on_linux do
    if Hardware::CPU.arm?
      url "__LINUX_ARM64_URL__"
      sha256 "__LINUX_ARM64_HASH__"
    else
      url "__LINUX_X64_URL__"
      sha256 "__LINUX_X64_HASH__"
    end
  end

  def install
    bin.install "cli-GPT-5.6-Luna-csharp-osx-arm64" => "cli-GPT-5.6-Luna-csharp" if OS.mac? && Hardware::CPU.arm?
    bin.install "cli-GPT-5.6-Luna-csharp-osx-x64" => "cli-GPT-5.6-Luna-csharp" if OS.mac? && !Hardware::CPU.arm?
    bin.install "cli-GPT-5.6-Luna-csharp-linux-arm64" => "cli-GPT-5.6-Luna-csharp" if OS.linux? && Hardware::CPU.arm?
    bin.install "cli-GPT-5.6-Luna-csharp-linux-x64" => "cli-GPT-5.6-Luna-csharp" if OS.linux? && !Hardware::CPU.arm?
  end

  test do
    assert_match "cli-GPT-5.6-Luna-csharp #{version}", shell_output("#{bin}/cli-GPT-5.6-Luna-csharp --version")
  end
end
'@
$formula = $formula.Replace('__REPO__', $repo).Replace('__VERSION__', $Version)
$formula = $formula.Replace('__OSX_ARM64_URL__', $assets['osx-arm64']).Replace('__OSX_ARM64_HASH__', $hashes['osx-arm64'])
$formula = $formula.Replace('__OSX_X64_URL__', $assets['osx-x64']).Replace('__OSX_X64_HASH__', $hashes['osx-x64'])
$formula = $formula.Replace('__LINUX_ARM64_URL__', $assets['linux-arm64']).Replace('__LINUX_ARM64_HASH__', $hashes['linux-arm64'])
$formula = $formula.Replace('__LINUX_X64_URL__', $assets['linux-x64']).Replace('__LINUX_X64_HASH__', $hashes['linux-x64'])
Set-Content -Path 'Formula/cli-GPT-5.6-Luna-csharp.rb' -Value $formula -Encoding utf8
