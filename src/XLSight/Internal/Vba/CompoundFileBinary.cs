using System.Buffers.Binary;
using System.Text;

namespace XLSight.Internal.Vba;

internal sealed class CompoundFileBinary
{
    private const uint EndOfChain = 0xFFFFFFFE;
    private const uint FreeSector = 0xFFFFFFFF;
    private const uint DifatSector = 0xFFFFFFFC;
    private const int HeaderSize = 512;
    private const int DirectoryEntrySize = 128;
    private const int MiniSectorSize = 64;
    private const int MaxFatEntries = 1_000_000;

    private readonly byte[] _data;
    private readonly int _sectorSize;
    private readonly int _miniStreamCutoffSize;
    private readonly uint[] _fat;
    private readonly uint[] _miniFat;
    private readonly byte[] _miniStream;
    private readonly List<DirectoryEntry> _directories;

    private CompoundFileBinary(
        byte[] data,
        int sectorSize,
        int miniStreamCutoffSize,
        uint[] fat,
        uint[] miniFat,
        byte[] miniStream,
        List<DirectoryEntry> directories)
    {
        _data = data;
        _sectorSize = sectorSize;
        _miniStreamCutoffSize = miniStreamCutoffSize;
        _fat = fat;
        _miniFat = miniFat;
        _miniStream = miniStream;
        _directories = directories;
    }

    public static CompoundFileBinary Read(Stream stream)
    {
        ArgumentNullException.ThrowIfNull(stream);

        using var memory = new MemoryStream();
        stream.CopyTo(memory);
        var data = memory.ToArray();
        if (data.Length < HeaderSize)
        {
            throw new VbaProjectParseException("Invalid CFB file: header is shorter than 512 bytes.");
        }

        var header = CfbHeader.Read(data);
        var difat = ReadDifat(data, header);
        var fat = ReadFat(data, header, difat);
        var directoryBytes = ReadRegularChain(data, header.SectorSize, fat, header.FirstDirectorySector, int.MaxValue);
        var directories = ReadDirectories(directoryBytes, header.SectorSize);
        if (directories.Count == 0)
        {
            throw new VbaProjectParseException("Invalid CFB file: root directory is missing.");
        }

        var root = directories[0];
        var miniStream = root.StartSector == EndOfChain
            ? []
            : ReadRegularChain(data, header.SectorSize, fat, root.StartSector, CheckedLength(root.Length, "root mini stream"));
        var miniFatBytes = header.FirstMiniFatSector == EndOfChain || header.MiniFatSectorCount == 0
            ? []
            : ReadRegularChain(
                data,
                header.SectorSize,
                fat,
                header.FirstMiniFatSector,
                checked((int)header.MiniFatSectorCount * header.SectorSize));
        var miniFat = ReadUInt32Array(miniFatBytes);

        return new CompoundFileBinary(
            data,
            header.SectorSize,
            CheckedLength(header.MiniStreamCutoffSize, "mini stream cutoff size"),
            fat,
            miniFat,
            miniStream,
            directories);
    }

    public byte[] GetStream(string name)
    {
        var entry = _directories.FirstOrDefault(d => string.Equals(d.Name, name, StringComparison.OrdinalIgnoreCase));
        if (entry is null)
        {
            throw new VbaProjectParseException($"VBA project CFB stream '{name}' was not found.");
        }

        var length = CheckedLength(entry.Length, entry.Name);
        if (length == 0)
        {
            return [];
        }

        if (entry.Length < (uint)_miniStreamCutoffSize)
        {
            return ReadMiniChain(entry.StartSector, length);
        }

        return ReadRegularChain(_data, _sectorSize, _fat, entry.StartSector, length);
    }

    private byte[] ReadMiniChain(uint startSector, int length)
    {
        var result = new List<byte>(Math.Min(length, 4096));
        var sector = startSector;
        var visited = new HashSet<uint>();

        while (sector != EndOfChain && result.Count < length)
        {
            if (!visited.Add(sector))
            {
                throw new VbaProjectParseException("Invalid CFB file: mini FAT chain contains a cycle.");
            }

            var offset = checked((int)sector * MiniSectorSize);
            if (offset < 0 || offset + MiniSectorSize > _miniStream.Length)
            {
                throw new VbaProjectParseException($"Invalid CFB file: mini sector {sector} points outside the mini stream.");
            }

            var toCopy = Math.Min(MiniSectorSize, length - result.Count);
            result.AddRange(_miniStream.AsSpan(offset, toCopy));
            sector = GetFatNext(_miniFat, sector, "mini FAT");
        }

        if (result.Count < length)
        {
            throw new VbaProjectParseException("Invalid CFB file: mini FAT chain ended before stream length was satisfied.");
        }

        return [.. result];
    }

    private static List<uint> ReadDifat(byte[] data, CfbHeader header)
    {
        var difat = new List<uint>(109);
        for (var offset = 76; offset < HeaderSize; offset += 4)
        {
            var sector = BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(offset, 4));
            if (sector != FreeSector)
            {
                difat.Add(sector);
            }
        }

        var nextDifatSector = header.FirstDifatSector;
        for (var i = 0u; i < header.DifatSectorCount && nextDifatSector != EndOfChain; i++)
        {
            var sectorBytes = GetSector(data, header.SectorSize, nextDifatSector);
            var entriesPerDifatSector = (header.SectorSize / 4) - 1;
            for (var j = 0; j < entriesPerDifatSector; j++)
            {
                var sector = BinaryPrimitives.ReadUInt32LittleEndian(sectorBytes.Slice(j * 4, 4));
                if (sector != FreeSector)
                {
                    difat.Add(sector);
                }
            }

            nextDifatSector = BinaryPrimitives.ReadUInt32LittleEndian(sectorBytes.Slice(header.SectorSize - 4, 4));
        }

        return difat;
    }

    private static uint[] ReadFat(byte[] data, CfbHeader header, List<uint> difat)
    {
        var entries = new List<uint>(Math.Min(MaxFatEntries, difat.Count * header.SectorSize / 4));
        foreach (var fatSector in difat.Take(CheckedLength(header.FatSectorCount, "FAT sector count")))
        {
            if (fatSector >= DifatSector)
            {
                continue;
            }

            var sector = GetSector(data, header.SectorSize, fatSector);
            for (var i = 0; i < sector.Length; i += 4)
            {
                entries.Add(BinaryPrimitives.ReadUInt32LittleEndian(sector.Slice(i, 4)));
                if (entries.Count > MaxFatEntries)
                {
                    throw new VbaProjectParseException("Invalid CFB file: FAT table is too large.");
                }
            }
        }

        return [.. entries];
    }

    private static byte[] ReadRegularChain(byte[] data, int sectorSize, uint[] fat, uint startSector, int maxLength)
    {
        if (startSector == EndOfChain)
        {
            return [];
        }

        var result = new List<byte>(Math.Min(maxLength, sectorSize * 4));
        var sector = startSector;
        var visited = new HashSet<uint>();

        while (sector != EndOfChain && result.Count < maxLength)
        {
            if (!visited.Add(sector))
            {
                throw new VbaProjectParseException("Invalid CFB file: FAT chain contains a cycle.");
            }

            var sectorBytes = GetSector(data, sectorSize, sector);
            var toCopy = Math.Min(sectorSize, maxLength - result.Count);
            result.AddRange(sectorBytes[..toCopy]);
            sector = GetFatNext(fat, sector, "FAT");
        }

        return [.. result];
    }

    private static uint GetFatNext(uint[] fat, uint sector, string tableName)
    {
        if (sector >= fat.Length)
        {
            throw new VbaProjectParseException($"Invalid CFB file: sector {sector} is outside the {tableName}.");
        }

        return fat[sector];
    }

    private static ReadOnlySpan<byte> GetSector(byte[] data, int sectorSize, uint sector)
    {
        var offset = checked(HeaderSize + ((int)sector * sectorSize));
        if (offset < HeaderSize || offset + sectorSize > data.Length)
        {
            throw new VbaProjectParseException($"Invalid CFB file: sector {sector} points outside the file.");
        }

        return data.AsSpan(offset, sectorSize);
    }

    private static List<DirectoryEntry> ReadDirectories(byte[] directoryBytes, int sectorSize)
    {
        var directories = new List<DirectoryEntry>();
        for (var offset = 0; offset + DirectoryEntrySize <= directoryBytes.Length; offset += DirectoryEntrySize)
        {
            var entry = directoryBytes.AsSpan(offset, DirectoryEntrySize);
            var nameLength = BinaryPrimitives.ReadUInt16LittleEndian(entry.Slice(64, 2));
            if (nameLength is < 2 or > 64)
            {
                continue;
            }

            var nameBytes = entry[..(nameLength - 2)];
            var name = Encoding.Unicode.GetString(nameBytes).TrimEnd('\0');
            if (string.IsNullOrEmpty(name))
            {
                continue;
            }

            var startSector = BinaryPrimitives.ReadUInt32LittleEndian(entry.Slice(116, 4));
            var length = sectorSize == 512
                ? BinaryPrimitives.ReadUInt32LittleEndian(entry.Slice(120, 4))
                : BinaryPrimitives.ReadUInt64LittleEndian(entry.Slice(120, 8));
            directories.Add(new DirectoryEntry(name, startSector, length));
        }

        return directories;
    }

    private static uint[] ReadUInt32Array(byte[] bytes)
    {
        var entries = new uint[bytes.Length / 4];
        for (var i = 0; i < entries.Length; i++)
        {
            entries[i] = BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(i * 4, 4));
        }

        return entries;
    }

    private static int CheckedLength(ulong value, string name)
    {
        if (value > int.MaxValue)
        {
            throw new VbaProjectParseException($"Invalid CFB file: {name} length exceeds supported bounds.");
        }

        return (int)value;
    }

    private sealed record DirectoryEntry(string Name, uint StartSector, ulong Length);

    private sealed record CfbHeader(
        int SectorSize,
        uint FirstDirectorySector,
        uint MiniStreamCutoffSize,
        uint FirstMiniFatSector,
        uint MiniFatSectorCount,
        uint FirstDifatSector,
        uint DifatSectorCount,
        uint FatSectorCount)
    {
        public static CfbHeader Read(byte[] data)
        {
            if (BinaryPrimitives.ReadUInt64LittleEndian(data.AsSpan(0, 8)) != 0xE11AB1A1E011CFD0)
            {
                throw new VbaProjectParseException("Invalid CFB file: OLE signature was not found.");
            }

            var sectorShift = BinaryPrimitives.ReadUInt16LittleEndian(data.AsSpan(30, 2));
            var sectorSize = sectorShift switch
            {
                0x0009 => 512,
                0x000C => 4096,
                _ => throw new VbaProjectParseException($"Invalid CFB file: unsupported sector shift 0x{sectorShift:X4}.")
            };

            var miniSectorShift = BinaryPrimitives.ReadUInt16LittleEndian(data.AsSpan(32, 2));
            if (miniSectorShift != 0x0006)
            {
                throw new VbaProjectParseException($"Invalid CFB file: unsupported mini sector shift 0x{miniSectorShift:X4}.");
            }

            return new CfbHeader(
                sectorSize,
                BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(48, 4)),
                BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(56, 4)),
                BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(60, 4)),
                BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(64, 4)),
                BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(68, 4)),
                BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(72, 4)),
                BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(44, 4)));
        }
    }
}
