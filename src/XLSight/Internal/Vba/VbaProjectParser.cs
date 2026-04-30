using System.Buffers.Binary;
using System.Runtime.InteropServices;
using System.Text;
using XLSight.Analysis;

namespace XLSight.Internal.Vba;

internal static class VbaProjectParser
{
    private const string VbaParseWarning = "vba.parse.warning";
    private const string VbaModuleWarning = "vba.module.warning";

    public static VbaProjectInfo Parse(Stream vbaProjectBin)
    {
        ArgumentNullException.ThrowIfNull(vbaProjectBin);

        var cfb = CompoundFileBinary.Read(vbaProjectBin);
        byte[] dir = VbaCompressedStream.Decompress(cfb.GetStream("dir"));
        var reader = new DirReader(dir);
        var warnings = new List<AnalysisWarning>();

        int? codePage = ReadProjectInformation(ref reader);
        Encoding encoding = ResolveEncoding(codePage, warnings);
        List<VbaReferenceInfo> references = ReadReferences(ref reader, encoding);
        List<ModuleRecord> moduleRecords = ReadModules(ref reader, encoding);
        List<VbaModuleInfo> modules = ReadModuleInfos(cfb, moduleRecords, warnings);

        return new VbaProjectInfo
        {
            Modules = modules,
            References = references,
            CodePage = codePage,
            EncodingName = encoding.WebName,
            Warnings = warnings,
        };
    }

    public static byte[] ReadModuleSourceBytes(Stream vbaProjectBin, string moduleName)
    {
        ArgumentNullException.ThrowIfNull(vbaProjectBin);
        ArgumentException.ThrowIfNullOrWhiteSpace(moduleName);

        var (cfb, module, _) = LocateModule(vbaProjectBin, moduleName);
        return ReadModuleSourceBytes(cfb, module);
    }

    public static string ReadModuleSource(Stream vbaProjectBin, string moduleName)
    {
        ArgumentNullException.ThrowIfNull(vbaProjectBin);
        ArgumentException.ThrowIfNullOrWhiteSpace(moduleName);

        var (cfb, module, encoding) = LocateModule(vbaProjectBin, moduleName);
        return encoding.GetString(ReadModuleSourceBytes(cfb, module));
    }

    private static (CompoundFileBinary Cfb, ModuleRecord Module, Encoding Encoding) LocateModule(
        Stream vbaProjectBin,
        string moduleName)
    {
        var cfb = CompoundFileBinary.Read(vbaProjectBin);
        byte[] dir = VbaCompressedStream.Decompress(cfb.GetStream("dir"));
        var reader = new DirReader(dir);
        int? codePage = ReadProjectInformation(ref reader);
        Encoding encoding = ResolveEncoding(codePage, []);
        _ = ReadReferences(ref reader, encoding);
        ModuleRecord module = ReadModules(ref reader, encoding)
            .FirstOrDefault(m => string.Equals(m.Name, moduleName, StringComparison.OrdinalIgnoreCase))
            ?? throw new VbaProjectParseException($"VBA module '{moduleName}' was not found.");
        return (cfb, module, encoding);
    }

    private static int? ReadProjectInformation(ref DirReader reader)
    {
        reader.Skip(10); // PROJECTSYSKIND
        if (reader.TryPeekUInt16(out ushort recordId) && recordId == 0x004A)
        {
            reader.Skip(10); // PROJECTCOMPATVERSION
        }

        reader.Skip(20); // PROJECTLCID and PROJECTLCIDINVOKE
        reader.CheckRecord(0x0003); // PROJECTCODEPAGE
        reader.CheckSize(2, "PROJECTCODEPAGE");
        int codePage = reader.ReadUInt16();

        reader.CheckVariableRecord(0x0004); // PROJECTNAME
        reader.CheckVariableRecord(0x0005); // PROJECTDOCSTRING
        reader.CheckVariableRecord(0x0040); // PROJECTDOCSTRINGUNICODE
        reader.CheckVariableRecord(0x0006); // PROJECTHELPFILEPATH
        reader.CheckVariableRecord(0x003D); // PROJECTHELPFILEPATHUNICODE
        reader.Skip(32); // PROJECTHELPCONTEXT, PROJECTLIBFLAGS, PROJECTVERSION
        reader.CheckVariableRecord(0x000C); // PROJECTCONSTANTS
        reader.CheckVariableRecord(0x003C); // PROJECTCONSTANTSUNICODE

        return codePage;
    }

    private static List<VbaReferenceInfo> ReadReferences(ref DirReader reader, Encoding encoding)
    {
        var references = new List<VbaReferenceInfo>();
        var current = new ReferenceBuilder();

        while (true)
        {
            ushort recordId = reader.ReadUInt16();
            switch (recordId)
            {
                case 0x000F:
                    current.AddIfNamed(references);
                    return references;
                case 0x0016:
                    current.AddIfNamed(references);
                    string name = Decode(encoding, reader.ReadVariableRecord());
                    current = new ReferenceBuilder { Name = name, Description = name, Kind = "Name" };
                    reader.CheckVariableRecord(0x003E);
                    break;
                case 0x0033:
                    current.Kind = "Original";
                    SetReferenceLibId(ref reader, encoding, current);
                    break;
                case 0x002F:
                    current.Kind = "Control";
                    ReadReferenceControl(ref reader, encoding, current);
                    break;
                case 0x000D:
                    current.Kind = "Registered";
                    reader.Skip(4);
                    SetReferenceLibId(ref reader, encoding, current);
                    reader.Skip(6);
                    break;
                case 0x000E:
                    current.Kind = "Project";
                    ReadReferenceProject(ref reader, encoding, current);
                    break;
                default:
                    throw new VbaProjectParseException($"Unsupported VBA reference record 0x{recordId:X4}.");
            }
        }
    }

    private static void ReadReferenceControl(ref DirReader reader, Encoding encoding, ReferenceBuilder reference)
    {
        reader.Skip(4);
        SetReferenceLibId(ref reader, encoding, reference);
        reader.Skip(6);

        ushort recordId = reader.ReadUInt16();
        if (recordId == 0x0016)
        {
            reader.ReadVariableRecord();
            reader.CheckVariableRecord(0x003E);
            reader.CheckRecord(0x0030);
        }
        else if (recordId != 0x0030)
        {
            throw new VbaProjectParseException($"Unsupported VBA reference control record 0x{recordId:X4}.");
        }

        reader.Skip(4);
        SetReferenceLibId(ref reader, encoding, reference);
        reader.Skip(26);
    }

    private static void ReadReferenceProject(ref DirReader reader, Encoding encoding, ReferenceBuilder reference)
    {
        reader.Skip(4);
        string absolute = Decode(encoding, reader.ReadVariableRecord());
        reference.Path = absolute.StartsWith("*\\C", StringComparison.Ordinal)
            ? absolute[3..]
            : absolute;
        reader.ReadVariableRecord();
        reader.Skip(6);
    }

    private static void SetReferenceLibId(ref DirReader reader, Encoding encoding, ReferenceBuilder reference)
    {
        byte[] libIdBytes = reader.ReadVariableRecord();
        if (libIdBytes.Length == 0 || EndsWith(libIdBytes, "##"u8))
        {
            return;
        }

        string libId = Decode(encoding, libIdBytes);
        int separator = libId.LastIndexOf('#');
        if (separator < 0)
        {
            throw new VbaProjectParseException("Invalid VBA reference LIBID format.");
        }

        string path = libId[..separator];
        string description = libId[(separator + 1)..];
        if (!string.IsNullOrEmpty(description))
        {
            reference.Description = description;
        }

        if (!string.IsNullOrEmpty(path) && string.IsNullOrEmpty(reference.Path))
        {
            reference.Path = path;
        }
    }

    private static List<ModuleRecord> ReadModules(ref DirReader reader, Encoding encoding)
    {
        reader.Skip(4);
        int moduleCount = reader.ReadUInt16();
        reader.Skip(8); // PROJECTCOOKIE

        var modules = new List<ModuleRecord>(moduleCount);
        for (int i = 0; i < moduleCount; i++)
        {
            string name = Decode(encoding, reader.CheckVariableRecord(0x0019));
            reader.CheckOptionalVariableRecord(0x0047);
            string streamName = Decode(encoding, reader.CheckVariableRecord(0x001A));
            reader.CheckVariableRecord(0x0032);
            reader.CheckVariableRecord(0x001C);
            reader.CheckVariableRecord(0x0048);

            reader.CheckRecord(0x0031);
            reader.Skip(4);
            int textOffset = checked((int)reader.ReadUInt32());

            reader.CheckRecord(0x001E);
            reader.Skip(8);
            reader.CheckRecord(0x002C);
            reader.Skip(6);

            string kind = reader.ReadUInt16() switch
            {
                0x0021 => "Procedural",
                0x0022 => "DocumentOrClass",
                ushort value => throw new VbaProjectParseException($"Unsupported VBA module type record 0x{value:X4}.")
            };

            while (true)
            {
                reader.Skip(4);
                ushort recordId = reader.ReadUInt16();
                if (recordId == 0x002B)
                {
                    break;
                }

                if (recordId is not (0x0025 or 0x0028))
                {
                    throw new VbaProjectParseException($"Unsupported VBA module option record 0x{recordId:X4}.");
                }
            }

            reader.Skip(4);
            modules.Add(new ModuleRecord(name, streamName, kind, textOffset));
        }

        return modules;
    }

    private static List<VbaModuleInfo> ReadModuleInfos(
        CompoundFileBinary cfb,
        IReadOnlyList<ModuleRecord> moduleRecords,
        List<AnalysisWarning> warnings)
    {
        var modules = new List<VbaModuleInfo>(moduleRecords.Count);
        foreach (var module in moduleRecords)
        {
            int rawByteLength = 0;
            try
            {
                rawByteLength = ReadModuleSourceBytes(cfb, module).Length;
            }
            catch (VbaProjectParseException ex)
            {
                warnings.Add(new AnalysisWarning
                {
                    Code = VbaModuleWarning,
                    Message = $"VBA module '{module.Name}' source could not be decompressed: {ex.Message}",
                });
            }

            modules.Add(new VbaModuleInfo
            {
                Name = module.Name,
                StreamName = module.StreamName,
                Kind = module.Kind,
                TextOffset = module.TextOffset,
                RawByteLength = rawByteLength,
            });
        }

        return modules;
    }

    private static byte[] ReadModuleSourceBytes(CompoundFileBinary cfb, ModuleRecord module)
    {
        byte[] stream = cfb.GetStream(module.StreamName);
        if ((uint)module.TextOffset > (uint)stream.Length)
        {
            throw new VbaProjectParseException(
                $"VBA module '{module.Name}' text offset {module.TextOffset} exceeds stream length {stream.Length}.");
        }

        return VbaCompressedStream.Decompress(stream.AsSpan(module.TextOffset));
    }

    private static Encoding ResolveEncoding(int? codePage, List<AnalysisWarning> warnings)
    {
        if (codePage is null)
        {
            return Encoding.UTF8;
        }

        Encoding? encoding = codePage.Value switch
        {
            65001 => Encoding.UTF8,
            1200 => Encoding.Unicode,
            1201 => Encoding.BigEndianUnicode,
            20127 => Encoding.ASCII,
            28591 => Encoding.Latin1,
            1252 => Encoding.Latin1,
            _ => null,
        };

        if (encoding is not null)
        {
            return encoding;
        }

        warnings.Add(new AnalysisWarning
        {
            Code = VbaParseWarning,
            Message = $"VBA project code page {codePage.Value} is not natively supported; UTF-8 fallback was used.",
        });
        return Encoding.UTF8;
    }

    private static string Decode(Encoding encoding, byte[] bytes)
        => encoding.GetString(bytes).TrimEnd('\0');

    private static bool EndsWith(byte[] bytes, ReadOnlySpan<byte> suffix)
        => bytes.Length >= suffix.Length && bytes.AsSpan(bytes.Length - suffix.Length).SequenceEqual(suffix);

    private sealed record ModuleRecord(string Name, string StreamName, string Kind, int TextOffset);

    private sealed class ReferenceBuilder
    {
        public string Name { get; init; } = string.Empty;
        public string Description { get; set; } = string.Empty;
        public string Path { get; set; } = string.Empty;
        public string Kind { get; set; } = string.Empty;

        public void AddIfNamed(List<VbaReferenceInfo> references)
        {
            if (string.IsNullOrEmpty(Name))
            {
                return;
            }

            references.Add(new VbaReferenceInfo
            {
                Name = Name,
                Description = Description,
                Path = Path,
                Kind = Kind,
            });
        }
    }

    [StructLayout(LayoutKind.Auto)]
    private ref struct DirReader
    {
        private ReadOnlySpan<byte> _bytes;
        private int _offset;

        public DirReader(ReadOnlySpan<byte> bytes)
        {
            _bytes = bytes;
            _offset = 0;
        }

        public bool TryPeekUInt16(out ushort value)
        {
            if (_offset + 2 > _bytes.Length)
            {
                value = 0;
                return false;
            }

            value = BinaryPrimitives.ReadUInt16LittleEndian(_bytes.Slice(_offset, 2));
            return true;
        }

        public ushort ReadUInt16()
        {
            EnsureAvailable(2);
            ushort value = BinaryPrimitives.ReadUInt16LittleEndian(_bytes.Slice(_offset, 2));
            _offset += 2;
            return value;
        }

        public uint ReadUInt32()
        {
            EnsureAvailable(4);
            uint value = BinaryPrimitives.ReadUInt32LittleEndian(_bytes.Slice(_offset, 4));
            _offset += 4;
            return value;
        }

        public void CheckRecord(ushort expectedRecordId)
        {
            ushort actual = ReadUInt16();
            if (actual != expectedRecordId)
            {
                throw new VbaProjectParseException(
                    $"Invalid VBA dir stream: expected record 0x{expectedRecordId:X4}, found 0x{actual:X4}.");
            }
        }

        public void CheckSize(uint expectedSize, string recordName)
        {
            uint actual = ReadUInt32();
            if (actual != expectedSize)
            {
                throw new VbaProjectParseException(
                    $"Invalid VBA dir stream: {recordName} size was {actual}, expected {expectedSize}.");
            }
        }

        public byte[] CheckVariableRecord(ushort expectedRecordId)
        {
            CheckRecord(expectedRecordId);
            return ReadVariableRecord();
        }

        public byte[] CheckOptionalVariableRecord(ushort expectedRecordId)
        {
            if (!TryPeekUInt16(out ushort actual) || actual != expectedRecordId)
            {
                return [];
            }

            return CheckVariableRecord(expectedRecordId);
        }

        public byte[] ReadVariableRecord()
        {
            uint length = ReadUInt32();
            if (length > int.MaxValue)
            {
                throw new VbaProjectParseException("Invalid VBA dir stream: variable record length exceeds supported bounds.");
            }

            EnsureAvailable((int)length);
            byte[] value = _bytes.Slice(_offset, (int)length).ToArray();
            _offset += (int)length;
            return value;
        }

        public void Skip(int count)
        {
            EnsureAvailable(count);
            _offset += count;
        }

        private readonly void EnsureAvailable(int count)
        {
            if (count < 0 || _offset + count > _bytes.Length)
            {
                throw new VbaProjectParseException("Invalid VBA dir stream: record data is truncated.");
            }
        }
    }
}
