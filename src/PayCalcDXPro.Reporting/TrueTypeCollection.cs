using System.Buffers.Binary;

namespace PayCalcDXPro.Reporting;

/// <summary>
/// TrueType Collection（.ttc）から先頭フォントを単体の sfnt（.ttf相当）として取り出す。
/// PdfSharp の IFontResolver は単体フォントのバイト列を要求するため、Windows同梱の日本語フォント
/// （多くが .ttc）をそのまま渡せない問題への対処。
/// </summary>
public static class TrueTypeCollection
{
    /// <summary>バイト列がttcなら先頭フォントを単体sfntへ変換、ttfならそのまま返す。</summary>
    public static byte[] ToSingleFont(byte[] data)
    {
        if (data.Length < 12) return data;
        // 'ttcf' 以外（=既に単体フォント）はそのまま
        if (!(data[0] == (byte)'t' && data[1] == (byte)'t' && data[2] == (byte)'c' && data[3] == (byte)'f'))
            return data;

        var span = data.AsSpan();
        uint firstOffsetTable = BinaryPrimitives.ReadUInt32BigEndian(span.Slice(12, 4)); // 先頭フォントのOffset Table位置

        int p = (int)firstOffsetTable;
        uint sfntVersion = BinaryPrimitives.ReadUInt32BigEndian(span.Slice(p, 4));
        ushort numTables = BinaryPrimitives.ReadUInt16BigEndian(span.Slice(p + 4, 2));

        // テーブルディレクトリを読む（各16バイト：tag/checksum/offset/length）
        var entries = new (uint tag, uint checksum, uint offset, uint length)[numTables];
        int dir = p + 12;
        for (int i = 0; i < numTables; i++)
        {
            int e = dir + i * 16;
            entries[i] = (
                BinaryPrimitives.ReadUInt32BigEndian(span.Slice(e, 4)),
                BinaryPrimitives.ReadUInt32BigEndian(span.Slice(e + 4, 4)),
                BinaryPrimitives.ReadUInt32BigEndian(span.Slice(e + 8, 4)),
                BinaryPrimitives.ReadUInt32BigEndian(span.Slice(e + 12, 4)));
        }

        // 新しい単体フォントを組み立てる（ディレクトリ→テーブル本体、4バイト境界整列）
        int headerSize = 12 + numTables * 16;
        int bodySize = 0;
        foreach (var t in entries) bodySize += Align4((int)t.length);

        var outBuf = new byte[headerSize + bodySize];
        var outSpan = outBuf.AsSpan();

        BinaryPrimitives.WriteUInt32BigEndian(outSpan.Slice(0, 4), sfntVersion);
        BinaryPrimitives.WriteUInt16BigEndian(outSpan.Slice(4, 2), numTables);
        ushort entrySelector = (ushort)Math.Floor(Math.Log2(numTables));
        ushort searchRange = (ushort)(Math.Pow(2, entrySelector) * 16);
        ushort rangeShift = (ushort)(numTables * 16 - searchRange);
        BinaryPrimitives.WriteUInt16BigEndian(outSpan.Slice(6, 2), searchRange);
        BinaryPrimitives.WriteUInt16BigEndian(outSpan.Slice(8, 2), entrySelector);
        BinaryPrimitives.WriteUInt16BigEndian(outSpan.Slice(10, 2), rangeShift);

        int writeDir = 12;
        int writeBody = headerSize;
        foreach (var t in entries)
        {
            data.AsSpan((int)t.offset, (int)t.length).CopyTo(outSpan.Slice(writeBody, (int)t.length));

            BinaryPrimitives.WriteUInt32BigEndian(outSpan.Slice(writeDir, 4), t.tag);
            BinaryPrimitives.WriteUInt32BigEndian(outSpan.Slice(writeDir + 4, 4), t.checksum);
            BinaryPrimitives.WriteUInt32BigEndian(outSpan.Slice(writeDir + 8, 4), (uint)writeBody);
            BinaryPrimitives.WriteUInt32BigEndian(outSpan.Slice(writeDir + 12, 4), t.length);
            writeDir += 16;
            writeBody += Align4((int)t.length);
        }

        return outBuf;
    }

    private static int Align4(int n) => (n + 3) & ~3;
}
