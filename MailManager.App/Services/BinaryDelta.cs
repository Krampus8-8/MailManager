using System.IO;
using System.IO.Compression;

namespace MailManager.App.Services;

internal static class BinaryDelta
{
    private const int BlockSize = 1024;
    private const ulong Multiplier = 257;
    private const uint Format = 0x31444D4D; // MMD1
    private const int Literal = -1;

    internal static string AssetName(byte[] sourceDigest) =>
        $"MailManager-from-{Convert.ToHexStringLower(sourceDigest)}.delta";

    internal static byte[] Create(byte[] source, byte[] target)
    {
        using var output = new MemoryStream();
        using (var compressed = new BrotliStream(output, CompressionLevel.Optimal, leaveOpen: true))
        using (var writer = new BinaryWriter(compressed))
        {
            writer.Write(Format);
            var blocks = new Dictionary<ulong, int>(source.Length / BlockSize);
            for (var offset = 0; offset <= source.Length - BlockSize; offset += BlockSize)
                blocks.TryAdd(Hash(source.AsSpan(offset, BlockSize)), offset);

            WriteOperations(writer, source, target, blocks);
        }
        return output.ToArray();
    }

    private static void WriteOperations(BinaryWriter writer, byte[] source, byte[] target, Dictionary<ulong, int> blocks)
    {
        var highestPower = 1UL;
        for (var i = 1; i < BlockSize; i++)
            highestPower = unchecked(highestPower * Multiplier);

        // Scan each target offset so matching blocks can be reused even when insertions shift their position.
        var position = 0;
        var literalStart = 0;
        var lastWindow = target.Length - BlockSize;
        var hash = lastWindow >= 0 ? Hash(target.AsSpan(0, BlockSize)) : 0;
        while (position <= lastWindow)
        {
            if (blocks.TryGetValue(hash, out var from) &&
                source.AsSpan(from, BlockSize).SequenceEqual(target.AsSpan(position, BlockSize)))
            {
                WriteLiteral(writer, target.AsSpan(literalStart, position - literalStart));
                var length = BlockSize;
                var available = Math.Min(source.Length - from, target.Length - position);
                while (length <= available - BlockSize &&
                    source.AsSpan(from + length, BlockSize).SequenceEqual(target.AsSpan(position + length, BlockSize)))
                    length += BlockSize;
                while (length < available && source[from + length] == target[position + length])
                    length++;

                writer.Write(from);
                writer.Write(length);
                position += length;
                literalStart = position;
                if (position <= lastWindow)
                    hash = Hash(target.AsSpan(position, BlockSize));
                continue;
            }

            if (position < lastWindow)
                hash = unchecked((hash - target[position] * highestPower) * Multiplier + target[position + BlockSize]);
            position++;
        }
        WriteLiteral(writer, target.AsSpan(literalStart));
    }

    private static ulong Hash(ReadOnlySpan<byte> bytes)
    {
        var hash = 0UL;
        foreach (var value in bytes)
            hash = unchecked(hash * Multiplier + value);
        return hash;
    }

    private static void WriteLiteral(BinaryWriter writer, ReadOnlySpan<byte> literal)
    {
        if (literal.IsEmpty)
            return;

        writer.Write(Literal);
        writer.Write(literal.Length);
        writer.Write(literal);
    }

    internal static byte[] Apply(byte[] source, byte[] patch, long expectedSize)
    {
        using var input = new MemoryStream(patch);
        using var compressed = new BrotliStream(input, CompressionMode.Decompress);
        using var reader = new BinaryReader(compressed);
        if (reader.ReadUInt32() != Format)
            throw new InvalidDataException("Unsupported delta format.");

        var result = new byte[checked((int)expectedSize)];
        var written = 0;
        while (written < result.Length)
        {
            var offset = reader.ReadInt32();
            var length = reader.ReadInt32();
            if (length <= 0 || length > result.Length - written ||
                (offset != Literal && (offset < 0 || offset > source.Length - length)))
                throw new InvalidDataException("Delta operation is out of bounds.");

            var destination = result.AsSpan(written, length);
            if (offset == Literal)
                compressed.ReadExactly(destination);
            else
                source.AsSpan(offset, length).CopyTo(destination);
            written += length;
        }
        if (compressed.ReadByte() != -1)
            throw new InvalidDataException("Delta has unexpected trailing operations.");

        return result;
    }
}
