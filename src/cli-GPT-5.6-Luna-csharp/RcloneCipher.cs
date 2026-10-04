using System.Security.Cryptography;
using System.Text;
using Org.BouncyCastle.Crypto;
using Org.BouncyCastle.Crypto.Engines;
using Org.BouncyCastle.Crypto.Generators;
using Org.BouncyCastle.Crypto.Macs;
using Org.BouncyCastle.Crypto.Parameters;

namespace RcloneEncrypt;

public enum FilenameEncoding { Base32, Base64 }

public sealed class RcloneCipher
{
    private static readonly byte[] DefaultSalt = [0xA8, 0x0D, 0xF4, 0x3A, 0x8F, 0xBD, 0x03, 0x08, 0xA7, 0xCA, 0xB8, 0x3E, 0x58, 0x1F, 0x86, 0xB1];
    private readonly byte[] dataKey;
    private readonly byte[] nameKey;
    private readonly byte[] nameTweak;
    private readonly FilenameEncoding encoding;

    public RcloneCipher(string password, string? salt, FilenameEncoding encoding = FilenameEncoding.Base32)
    {
        this.encoding = encoding;
        var material = SCrypt.Generate(Encoding.UTF8.GetBytes(password), string.IsNullOrEmpty(salt) ? DefaultSalt : Encoding.UTF8.GetBytes(salt), 16384, 8, 1, 80);
        dataKey = material[..32];
        nameKey = material[32..64];
        nameTweak = material[64..80];
        CryptographicOperations.ZeroMemory(material);
    }

    public string EncryptFileName(string name) => TransformName(name, encrypt: true);
    public string DecryptFileName(string name) => TransformName(name, encrypt: false);

    public byte[] EncryptData(byte[] plaintext)
    {
        using var output = new MemoryStream();
        output.Write("RCLONE\0\0"u8);
        var nonce = RandomNumberGenerator.GetBytes(24);
        output.Write(nonce);
        for (var offset = 0; offset < plaintext.Length; offset += 65536)
        {
            var length = Math.Min(65536, plaintext.Length - offset);
            var block = SecretBoxEncrypt(plaintext.AsSpan(offset, length), nonce);
            output.Write(block);
            IncrementNonce(nonce);
        }
        return output.ToArray();
    }

    public byte[] DecryptData(byte[] ciphertext)
    {
        if (ciphertext.Length < 32 || !ciphertext.AsSpan(0, 8).SequenceEqual("RCLONE\0\0"u8))
            throw new InvalidDataException("Input is not a valid rclone encrypted file.");
        var nonce = ciphertext.AsSpan(8, 24).ToArray();
        using var output = new MemoryStream();
        var offset = 32;
        while (offset < ciphertext.Length)
        {
            var length = Math.Min(65552, ciphertext.Length - offset);
            if (length < 17)
                throw new InvalidDataException("Encrypted block is truncated.");
            output.Write(SecretBoxDecrypt(ciphertext.AsSpan(offset, length), nonce));
            IncrementNonce(nonce);
            offset += length;
        }
        return output.ToArray();
    }

    private string TransformName(string name, bool encrypt)
    {
        var segments = name.Split('/');
        for (var i = 0; i < segments.Length; i++)
            segments[i] = encrypt ? EncryptSegment(segments[i]) : DecryptSegment(segments[i]);
        return string.Join('/', segments);
    }

    private string EncryptSegment(string plaintext)
    {
        if (plaintext.Length == 0) return string.Empty;
        var bytes = Encoding.UTF8.GetBytes(plaintext);
        var padding = 16 - bytes.Length % 16;
        Array.Resize(ref bytes, bytes.Length + padding);
        Array.Fill(bytes, (byte)padding, bytes.Length - padding, padding);
        return Encode(Eme.Transform(nameKey, nameTweak, bytes, true));
    }

    private string DecryptSegment(string ciphertext)
    {
        if (ciphertext.Length == 0) return string.Empty;
        var bytes = Decode(ciphertext);
        if (bytes.Length == 0 || bytes.Length % 16 != 0)
            throw new InvalidDataException("Encrypted filename is not a valid rclone filename.");
        var plaintext = Eme.Transform(nameKey, nameTweak, bytes, false);
        var padding = plaintext[^1];
        if (padding is 0 or > 16 || plaintext[^padding..].Any(value => value != padding))
            throw new InvalidDataException("Could not decrypt filename; check the password, salt, and encoding.");
        try { return new UTF8Encoding(false, true).GetString(plaintext, 0, plaintext.Length - padding); }
        catch (DecoderFallbackException) { throw new InvalidDataException("Decrypted filename is not valid UTF-8."); }
    }

    private string Encode(byte[] bytes) => encoding switch
    {
        FilenameEncoding.Base32 => bytes.ToBase32String().TrimEnd('=').ToLowerInvariant(),
        FilenameEncoding.Base64 => Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_'),
        _ => throw new ArgumentOutOfRangeException()
    };

    private byte[] Decode(string value) => encoding switch
    {
        FilenameEncoding.Base32 => Base32Extensions.FromBase32String(value),
        FilenameEncoding.Base64 => Convert.FromBase64String(value.Replace('-', '+').Replace('_', '/').PadRight((value.Length + 3) / 4 * 4, '=')),
        _ => throw new ArgumentOutOfRangeException()
    };

    private byte[] SecretBoxEncrypt(ReadOnlySpan<byte> plaintext, byte[] nonce)
    {
        var encrypted = new byte[plaintext.Length];
        var engine = new XSalsa20Engine();
        engine.Init(true, new ParametersWithIV(new KeyParameter(dataKey), nonce));
        engine.ProcessBytes(new byte[32], 0, 32, new byte[32], 0);
        engine.ProcessBytes(plaintext.ToArray(), 0, plaintext.Length, encrypted, 0);
        var mac = new Poly1305();
        mac.Init(new KeyParameter(GetPolyKey(nonce)));
        mac.BlockUpdate(encrypted, 0, encrypted.Length);
        var result = new byte[16 + encrypted.Length];
        mac.DoFinal(result, 0);
        encrypted.CopyTo(result, 16);
        return result;
    }

    private byte[] SecretBoxDecrypt(ReadOnlySpan<byte> input, byte[] nonce)
    {
        var expected = input[..16].ToArray();
        var encrypted = input[16..].ToArray();
        var mac = new Poly1305();
        mac.Init(new KeyParameter(GetPolyKey(nonce)));
        mac.BlockUpdate(encrypted, 0, encrypted.Length);
        var actual = new byte[16];
        mac.DoFinal(actual, 0);
        if (!CryptographicOperations.FixedTimeEquals(expected, actual))
            throw new InvalidDataException("Could not decrypt file; check the password and salt.");
        var plaintext = new byte[encrypted.Length];
        var engine = new XSalsa20Engine();
        engine.Init(true, new ParametersWithIV(new KeyParameter(dataKey), nonce));
        engine.ProcessBytes(new byte[32], 0, 32, new byte[32], 0);
        engine.ProcessBytes(encrypted, 0, encrypted.Length, plaintext, 0);
        return plaintext;
    }

    private byte[] GetPolyKey(byte[] nonce)
    {
        var key = new byte[32];
        var engine = new XSalsa20Engine();
        engine.Init(true, new ParametersWithIV(new KeyParameter(dataKey), nonce));
        engine.ProcessBytes(new byte[32], 0, 32, key, 0);
        return key;
    }

    private static void IncrementNonce(byte[] nonce)
    {
        for (var i = 0; i < nonce.Length; i++)
            if (++nonce[i] != 0) break;
    }
}

internal static class Eme
{
    public static byte[] Transform(byte[] key, byte[] tweak, byte[] input, bool encrypt)
    {
        var block = Aes.Create();
        block.Key = key;
        var count = input.Length / 16;
        var l = new byte[count][];
        var li = new byte[16];
        block.EncryptEcb(new byte[16], li, PaddingMode.None);
        for (var i = 0; i < count; i++) { li = Double(li); l[i] = li.ToArray(); }
        var c = new byte[input.Length];
        for (var i = 0; i < count; i++)
        {
            var mixed = Xor(input.AsSpan(i * 16, 16), l[i]);
            TransformBlock(block, mixed, c.AsSpan(i * 16, 16), encrypt);
        }
        var mp = Xor(c.AsSpan(0, 16), tweak);
        for (var i = 1; i < count; i++) XorInPlace(mp, c.AsSpan(i * 16, 16));
        var mc = new byte[16]; TransformBlock(block, mp, mc, encrypt);
        var m = Xor(mp, mc);
        for (var i = 1; i < count; i++) { m = Double(m); XorInPlace(c.AsSpan(i * 16, 16), m); }
        var first = Xor(mc, tweak);
        for (var i = 1; i < count; i++) XorInPlace(first, c.AsSpan(i * 16, 16));
        first.CopyTo(c, 0);
        for (var i = 0; i < count; i++) { TransformBlock(block, c.AsSpan(i * 16, 16), c.AsSpan(i * 16, 16), encrypt); XorInPlace(c.AsSpan(i * 16, 16), l[i]); }
        return c;
    }

    private static void TransformBlock(Aes aes, ReadOnlySpan<byte> input, Span<byte> output, bool encrypt)
    {
        var transformed = encrypt ? aes.EncryptEcb(input, PaddingMode.None) : aes.DecryptEcb(input, PaddingMode.None);
        transformed.CopyTo(output);
    }

    private static byte[] Double(ReadOnlySpan<byte> input)
    {
        var output = new byte[16];
        output[0] = (byte)(input[0] << 1);
        if ((input[15] & 0x80) != 0) output[0] ^= 135;
        for (var i = 1; i < 16; i++) output[i] = (byte)((input[i] << 1) | (input[i - 1] >> 7));
        return output;
    }

    private static byte[] Xor(ReadOnlySpan<byte> left, ReadOnlySpan<byte> right) { var output = new byte[left.Length]; for (var i = 0; i < output.Length; i++) output[i] = (byte)(left[i] ^ right[i]); return output; }
    private static void XorInPlace(Span<byte> left, ReadOnlySpan<byte> right) { for (var i = 0; i < left.Length; i++) left[i] ^= right[i]; }
}

internal static class Base32Extensions
{
    private const string Alphabet = "0123456789ABCDEFGHIJKLMNOPQRSTUV";
    public static string ToBase32String(this byte[] data)
    {
        var result = new StringBuilder((data.Length + 4) / 5 * 8);
        for (var i = 0; i < data.Length; i += 5)
        {
            var remaining = Math.Min(5, data.Length - i);
            ulong value = 0;
            for (var j = 0; j < remaining; j++) value = (value << 8) | data[i + j];
            value <<= (5 - remaining) * 8;
            for (var j = 0; j < 8; j++) result.Append(Alphabet[(int)((value >> (35 - j * 5)) & 31)]);
        }
        return result.ToString()[..((data.Length * 8 + 4) / 5)];
    }

    public static byte[] FromBase32String(string value)
    {
        var normalized = value.ToUpperInvariant();
        var output = new byte[normalized.Length * 5 / 8];
        var buffer = 0;
        var bits = 0;
        var count = 0;
        foreach (var character in normalized)
        {
            var digit = Alphabet.IndexOf(character);
            if (digit < 0) throw new FormatException("Invalid base32 filename.");
            buffer = (buffer << 5) | digit;
            bits += 5;
            if (bits < 8) continue;
            output[count++] = (byte)(buffer >> (bits - 8));
            bits -= 8;
            buffer &= (1 << bits) - 1;
        }
        return output;
    }
}
