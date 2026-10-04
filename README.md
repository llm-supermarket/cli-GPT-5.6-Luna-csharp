# rclone-encrypt-GPT-5.6-Luna
A small CLI tool that encrypts and decrypts using the rclone encryption defaults.

`cli-GPT-5.6-Luna-csharp` is a self-contained C#/.NET 10 command-line tool. It does not require .NET or another runtime to be installed when using a release binary.

## Installation

### Homebrew (macOS/Linux)

```bash
brew tap llm-supermarket/cli-GPT-5.6-Luna-csharp https://github.com/llm-supermarket/cli-GPT-5.6-Luna-csharp
brew install cli-GPT-5.6-Luna-csharp
```

### Scoop (Windows)

```bash
scoop bucket add cli-GPT-5.6-Luna-csharp https://github.com/llm-supermarket/cli-GPT-5.6-Luna-csharp
scoop install cli-GPT-5.6-Luna-csharp
```

## Usage

The input file is required. If `--output-file` is omitted, the output is written beside the input using the transformed filename.

```bash
cli-GPT-5.6-Luna-csharp encrypt \
  --input-file TEST_FILE.txt

cli-GPT-5.6-Luna-csharp decrypt \
  --input-file kr9tu4e1da4u3nifdd99g9tf5o \
  --output-file TEST_FILE.txt
```

When `--password` and `--salt` are omitted, the CLI prompts for them. The password prompt is hidden in an interactive terminal. Salt is optional; press Enter to use rclone's built-in default salt.

```bash
cli-GPT-5.6-Luna-csharp encrypt \
  -i TEST_FILE.txt \
  -o encrypted-file \
  --password "$RCLONE_PASSWORD" \
  --salt "$RCLONE_SALT"
```

Passing a password on the command line can expose it in shell history and process listings. Prefer an environment variable or the interactive prompt, and wipe the command from your history if you used `--password`.

Filename encoding defaults to rclone's lowercase unpadded base32 encoding. Base64 is also supported when the backing store is case-sensitive:

```bash
cli-GPT-5.6-Luna-csharp encrypt \
  -i TEST_FILE.txt \
  -o encrypted-file \
  --password "$RCLONE_PASSWORD" \
  --filename-encoding base64
```

Supported options:

| Option | Description |
| --- | --- |
| `-i`, `--input-file` | Input file. Required. |
| `-o`, `--output-file` | Output file. Defaults to the transformed filename beside the input. |
| `--password` | Password. If omitted, prompt securely. |
| `--salt` | Optional rclone password salt. |
| `--filename-encoding` | `base32` by default or `base64`. |

The content format is rclone crypt compatible: Scrypt key derivation, XSalsa20-Poly1305 authenticated chunks, and deterministic EME/AES filename encryption.

## Building From Source

Requires the .NET 10 SDK.

```bash
git clone https://github.com/llm-supermarket/cli-GPT-5.6-Luna-csharp
cd cli-GPT-5.6-Luna-csharp
dotnet build
dotnet test
dotnet run --project src/cli-GPT-5.6-Luna-csharp -- --help
```

To produce a runtime-independent single-file binary:

```bash
dotnet publish src/cli-GPT-5.6-Luna-csharp \
  --configuration Release \
  --runtime linux-x64 \
  --self-contained true \
  -p:PublishSingleFile=true \
  -p:IncludeNativeLibrariesForSelfExtract=true
```

## Releases

Pushing a `vX.Y.Z` tag triggers the GitHub Actions release workflow. It publishes self-contained single-file binaries for Linux, macOS, and Windows, creates a GitHub Release, and updates the Scoop manifest and Homebrew formula with their SHA-256 hashes.
