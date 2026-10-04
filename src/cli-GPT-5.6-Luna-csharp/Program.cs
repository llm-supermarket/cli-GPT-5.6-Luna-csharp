using System.Reflection;
using RcloneEncrypt;

return await Cli.RunAsync(args);

internal static class Cli
{
    public static async Task<int> RunAsync(string[] args)
    {
        try
        {
            if (args is ["--version"] or ["-v"])
            {
                var version = Assembly.GetExecutingAssembly().GetName().Version ?? new Version(0, 1, 0);
                Console.WriteLine($"cli-GPT-5.6-Luna-csharp {version.Major}.{version.Minor}.{version.Build}");
                return 0;
            }

            if (args.Length == 0 || args is ["--help"] or ["-h"])
            {
                PrintHelp();
                return args.Length == 0 ? 1 : 0;
            }

            var options = CliOptions.Parse(args);
            var password = options.Password ?? Prompt("Password", secret: true);
            var salt = options.Salt ?? Prompt("Salt (optional)", secret: false);
            if (options.Password is not null)
            {
                Console.Error.WriteLine("Warning: --password can be exposed in shell history and process listings. Prefer an environment variable or interactive prompt, and wipe this command from your history if you used it.");
            }

            var cipher = new RcloneCipher(password, salt, options.FilenameEncoding);
            var input = Path.GetFullPath(options.InputFile);
            var output = options.OutputFile is null
                ? Path.Combine(Path.GetDirectoryName(input) ?? Directory.GetCurrentDirectory(),
                    options.Operation == Operation.Encrypt ? cipher.EncryptFileName(Path.GetFileName(input)) : cipher.DecryptFileName(Path.GetFileName(input)))
                : Path.GetFullPath(options.OutputFile);

            var bytes = await File.ReadAllBytesAsync(input);
            var transformed = options.Operation == Operation.Encrypt ? cipher.EncryptData(bytes) : cipher.DecryptData(bytes);
            await File.WriteAllBytesAsync(output, transformed);
            Console.WriteLine(output);
            return 0;
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidDataException or IOException or UnauthorizedAccessException)
        {
            Console.Error.WriteLine($"Error: {ex.Message}");
            return 2;
        }
    }

    private static string Prompt(string label, bool secret)
    {
        Console.Error.Write($"{label}: ");
        if (!secret || Console.IsInputRedirected)
            return Console.ReadLine() ?? string.Empty;

        var chars = new List<char>();
        while (true)
        {
            var key = Console.ReadKey(intercept: true);
            if (key.Key == ConsoleKey.Enter)
                break;
            if (key.Key == ConsoleKey.Backspace && chars.Count > 0)
            {
                chars.RemoveAt(chars.Count - 1);
                continue;
            }
            if (!char.IsControl(key.KeyChar))
                chars.Add(key.KeyChar);
        }
        Console.Error.WriteLine();
        return new string(chars.ToArray());
    }

    private static void PrintHelp() => Console.WriteLine("""
        cli-GPT-5.6-Luna-csharp encrypts and decrypts files using the rclone crypt format.

        Usage:
          cli-GPT-5.6-Luna-csharp <encrypt|decrypt> -i <file> [-o <file>]
            [--password <password>] [--salt <salt>] [--filename-encoding <base32|base64>]

        Options:
          -i, --input-file       Input file (required)
          -o, --output-file      Output file (defaults to the transformed filename beside input)
          --password             Password (unsafe in shell history/process listings; prefer a prompt)
          --salt                 Optional rclone password salt
          --filename-encoding    base32 (default) or base64
          -h, --help             Show help
          -v, --version          Show version
        """);
}

internal enum Operation { Encrypt, Decrypt }

internal sealed record CliOptions(Operation Operation, string InputFile, string? OutputFile, string? Password, string? Salt, FilenameEncoding FilenameEncoding)
{
    public static CliOptions Parse(string[] args)
    {
        if (args.Length == 0 || !Enum.TryParse<Operation>(args[0], true, out var operation))
            throw new ArgumentException("The first argument must be encrypt or decrypt.");

        string? input = null, output = null, password = null, salt = null, encoding = null;
        for (var i = 1; i < args.Length; i++)
        {
            var value = args[i] switch
            {
                "-i" or "--input-file" => Next(args, ref i, "input file"),
                "-o" or "--output-file" => Next(args, ref i, "output file"),
                "--password" => Next(args, ref i, "password"),
                "--salt" => Next(args, ref i, "salt"),
                "--filename-encoding" => Next(args, ref i, "filename encoding"),
                _ => throw new ArgumentException($"Unknown option '{args[i]}'.")
            };
            switch (args[i - (value is null ? 0 : 1)])
            {
                case "-i": case "--input-file": input = value; break;
                case "-o": case "--output-file": output = value; break;
                case "--password": password = value; break;
                case "--salt": salt = value; break;
                case "--filename-encoding": encoding = value; break;
            }
        }
        if (string.IsNullOrWhiteSpace(input))
            throw new ArgumentException("-i or --input-file is required.");
        if (!Enum.TryParse<FilenameEncoding>(encoding ?? "base32", true, out var filenameEncoding))
            throw new ArgumentException("Filename encoding must be base32 or base64.");
        return new(operation, input, output, password, salt ?? string.Empty, filenameEncoding);
    }

    private static string Next(string[] args, ref int index, string name) =>
        ++index < args.Length && !args[index].StartsWith('-') ? args[index] : throw new ArgumentException($"Missing {name} value.");
}
