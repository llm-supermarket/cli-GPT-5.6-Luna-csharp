using System.Diagnostics;
using System.Text;
using RcloneEncrypt;

namespace CliTests;

public sealed class RcloneCipherTests
{
    [Fact]
    public void EncryptsAndDecryptsWithoutSalt()
    {
        var cipher = new RcloneCipher("Testpassword1", string.Empty);
        var plain = Encoding.UTF8.GetBytes("abandon ability able about above absent");
        var encrypted = cipher.EncryptData(plain);

        Assert.Equal(plain, new RcloneCipher("Testpassword1", string.Empty).DecryptData(encrypted));
        Assert.Equal("kr9tu4e1da4u3nifdd99g9tf5o", cipher.EncryptFileName("TEST_FILE.txt"));
    }

    [Fact]
    public void EncryptsAndDecryptsWithSalt()
    {
        var cipher = new RcloneCipher("Testpassword1", "a custom salt");
        var plain = Encoding.UTF8.GetBytes("abstract absurd abuse access accident");
        var encrypted = cipher.EncryptData(plain);

        Assert.Equal(plain, new RcloneCipher("Testpassword1", "a custom salt").DecryptData(encrypted));
        Assert.Equal("TEST_FILE.txt", cipher.DecryptFileName(cipher.EncryptFileName("TEST_FILE.txt")));
    }

    [Fact]
    public void SupportsBase64FilenameEncoding()
    {
        var cipher = new RcloneCipher("Testpassword1", string.Empty, FilenameEncoding.Base64);
        Assert.Equal("Iyxcijgc9bp3o5Y0npW6xqUvwWNcc3MA4SadB0sR6cY", cipher.EncryptFileName("TEST_FILE BASE64.txt"));
        Assert.Equal("TEST_FILE BASE64.txt", cipher.DecryptFileName("Iyxcijgc9bp3o5Y0npW6xqUvwWNcc3MA4SadB0sR6cY"));
    }

    [Fact]
    public async Task CliAcceptsPasswordFlagAndWarns()
    {
        using var temp = new TemporaryDirectory();
        var input = Path.Combine(temp.Path, "TEST_FILE.txt");
        var output = Path.Combine(temp.Path, "encrypted");
        await File.WriteAllTextAsync(input, "abandon ability able");
        var result = await RunCliAsync($"encrypt -i \"{input}\" -o \"{output}\" --password Testpassword1");

        Assert.Equal(0, result.ExitCode);
        Assert.Contains("Warning", result.Error);
        Assert.True((await File.ReadAllBytesAsync(output)).AsSpan(0, 8).SequenceEqual("RCLONE\0\0"u8));
    }

    [Fact]
    public async Task CliPromptsForPasswordAndSalt()
    {
        using var temp = new TemporaryDirectory();
        var input = Path.Combine(temp.Path, "TEST_FILE.txt");
        var encrypted = Path.Combine(temp.Path, "encrypted");
        var output = Path.Combine(temp.Path, "decrypted");
        await File.WriteAllTextAsync(input, "abandon ability able");
        var encrypt = await RunCliAsync($"encrypt -i \"{input}\" -o \"{encrypted}\"", "Testpassword1\nsalt-value\n");
        var decrypt = await RunCliAsync($"decrypt -i \"{encrypted}\" -o \"{output}\"", "Testpassword1\nsalt-value\n");

        Assert.Equal(0, encrypt.ExitCode);
        Assert.Equal(0, decrypt.ExitCode);
        Assert.Equal(await File.ReadAllTextAsync(input), await File.ReadAllTextAsync(output));
    }

    private static async Task<ProcessResult> RunCliAsync(string arguments, string? standardInput = null)
    {
        var root = FindRepositoryRoot();
        var project = Path.Combine(root, "src", "cli-GPT-5.6-Luna-csharp", "bin", "Debug", "net10.0", "cli-GPT-5.6-Luna-csharp.dll");
        var process = new Process
        {
            StartInfo = new ProcessStartInfo("dotnet", $"\"{project}\" {arguments}")
            {
                RedirectStandardInput = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            }
        };
        process.Start();
        if (standardInput is not null) await process.StandardInput.WriteAsync(standardInput);
        process.StandardInput.Close();
        var output = await process.StandardOutput.ReadToEndAsync();
        var error = await process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync();
        return new(process.ExitCode, output, error);
    }

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "README.md"))) directory = directory.Parent;
        return directory?.FullName ?? throw new DirectoryNotFoundException("Repository root not found.");
    }

    private sealed record ProcessResult(int ExitCode, string Output, string Error);

    private sealed class TemporaryDirectory : IDisposable
    {
        public string Path { get; } = System.IO.Path.Combine(System.IO.Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        public TemporaryDirectory() => Directory.CreateDirectory(Path);
        public void Dispose() => Directory.Delete(Path, recursive: true);
    }
}
