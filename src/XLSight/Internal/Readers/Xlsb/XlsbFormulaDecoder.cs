using System.Buffers.Binary;
using System.Globalization;
using XLSight.Internal.Sinks;

namespace XLSight.Internal.Readers.Xlsb;

internal static class XlsbFormulaDecoder
{
    private const int PtgAdd = 0x03;
    private const int PtgSub = 0x04;
    private const int PtgMul = 0x05;
    private const int PtgDiv = 0x06;
    private const int PtgPower = 0x07;
    private const int PtgConcat = 0x08;
    private const int PtgLt = 0x09;
    private const int PtgLe = 0x0A;
    private const int PtgEq = 0x0B;
    private const int PtgGe = 0x0C;
    private const int PtgGt = 0x0D;
    private const int PtgNe = 0x0E;
    private const int PtgIsect = 0x0F;
    private const int PtgUnion = 0x10;
    private const int PtgRange = 0x11;
    private const int PtgUPlus = 0x12;
    private const int PtgUMinus = 0x13;
    private const int PtgPercent = 0x14;
    private const int PtgParen = 0x15;
    private const int PtgStr = 0x17;
    private const int PtgErr = 0x1C;
    private const int PtgBool = 0x1D;
    private const int PtgInt = 0x1E;
    private const int PtgNum = 0x1F;
    private const int OperandTokenMask = 0x1F;
    private const int MaxFormulaTokenBytes = 16_384;
    private const int PtgRef = 0x04;
    private const int PtgArea = 0x05;
    private const int PtgRefErr = 0x0A;
    private const int PtgAreaErr = 0x0B;
    private const int PtgRef3d = 0x1A;
    private const int PtgArea3d = 0x1B;
    private const int PtgRefErr3d = 0x1C;
    private const int PtgAreaErr3d = 0x1D;
    private const ushort ColumnMask = 0x3FFF;
    private const ushort ColumnRelativeFlag = 0x4000;
    private const ushort RowRelativeFlag = 0x8000;

    internal static string Decode(ReadOnlySpan<byte> formula, XlsbFormulaContext? context = null)
    {
        if (!TryReadTokenBytes(formula, out ReadOnlySpan<byte> tokens))
        {
            return string.Empty;
        }

        var expressions = new Stack<string>();
        int offset = 0;
        while (offset < tokens.Length)
        {
            byte token = tokens[offset];
            OperatorResult operatorResult = TryHandleOperator(token, expressions);
            if (operatorResult == OperatorResult.Invalid)
            {
                return string.Empty;
            }

            if (operatorResult == OperatorResult.Handled)
            {
                offset++;
                continue;
            }

            if (TryHandleScalar(tokens, ref offset, expressions))
            {
                continue;
            }

            if (TryHandleReference(tokens, ref offset, expressions, context))
            {
                continue;
            }

            return string.Empty;
        }

        return expressions.Count == 1 ? expressions.Pop() : string.Empty;
    }

    internal static void EmitReferences<TSink>(
        ReadOnlySpan<byte> formula,
        XlsbFormulaContext context,
        ref TSink sink)
        where TSink : struct, IByteSheetSink
    {
        if (!TryReadTokenBytes(formula, out ReadOnlySpan<byte> tokens))
        {
            return;
        }

        int offset = 0;
        while (offset < tokens.Length)
        {
            int tokenLength = GetTokenLength(tokens, offset);
            if (tokenLength <= 0)
            {
                return;
            }

            int ptg = tokens[offset] & OperandTokenMask;
            if (ptg is PtgRef3d or PtgArea3d or PtgRefErr3d or PtgAreaErr3d)
            {
                int externSheetIndex = BinaryPrimitives.ReadUInt16LittleEndian(tokens.Slice(offset + 1, 2));
                if (context.TryResolveSheet(externSheetIndex, out string sheetName, out _))
                {
                    sink.OnFormulaReference(FormulaReference.FromText(null, sheetName));
                }
            }

            offset += tokenLength;
        }
    }

    private enum OperatorResult
    {
        NotOperator,
        Handled,
        Invalid,
    }

    private static bool TryReadTokenBytes(ReadOnlySpan<byte> formula, out ReadOnlySpan<byte> tokens)
    {
        tokens = [];
        if (formula.Length < 8)
        {
            return false;
        }

        uint tokenByteCount = BinaryPrimitives.ReadUInt32LittleEndian(formula[..4]);
        if (tokenByteCount == 0 ||
            tokenByteCount > MaxFormulaTokenBytes ||
            formula.Length - 4 < tokenByteCount + 4)
        {
            return false;
        }

        tokens = formula.Slice(4, (int)tokenByteCount);
        return true;
    }

    private static OperatorResult TryHandleOperator(byte token, Stack<string> expressions)
    {
        string op = token switch
        {
            PtgAdd => "+",
            PtgSub => "-",
            PtgMul => "*",
            PtgDiv => "/",
            PtgPower => "^",
            PtgConcat => "&",
            PtgLt => "<",
            PtgLe => "<=",
            PtgEq => "=",
            PtgGe => ">=",
            PtgGt => ">",
            PtgNe => "<>",
            PtgIsect => " ",
            PtgUnion => ",",
            PtgRange => ":",
            _ => string.Empty,
        };

        if (op.Length > 0)
        {
            if (!TryPopBinary(expressions, out string left, out string right))
            {
                return OperatorResult.Invalid;
            }

            expressions.Push($"{left}{op}{right}");
            return OperatorResult.Handled;
        }

        switch (token)
        {
            case PtgUPlus:
                return expressions.Count > 0 ? OperatorResult.Handled : OperatorResult.Invalid;
            case PtgUMinus when expressions.Count > 0:
                expressions.Push($"-{expressions.Pop()}");
                return OperatorResult.Handled;
            case PtgUMinus:
                return OperatorResult.Invalid;
            case PtgPercent when expressions.Count > 0:
                expressions.Push($"{expressions.Pop()}%");
                return OperatorResult.Handled;
            case PtgPercent:
                return OperatorResult.Invalid;
            case PtgParen when expressions.Count > 0:
                expressions.Push($"({expressions.Pop()})");
                return OperatorResult.Handled;
            case PtgParen:
                return OperatorResult.Invalid;
            default:
                return OperatorResult.NotOperator;
        }
    }

    private static bool TryHandleScalar(ReadOnlySpan<byte> tokens, ref int offset, Stack<string> expressions)
    {
        byte token = tokens[offset];
        switch (token)
        {
            case PtgInt:
                if (tokens.Length - offset < 3) { return false; }
                expressions.Push(BinaryPrimitives.ReadUInt16LittleEndian(tokens.Slice(offset + 1, 2)).ToString(CultureInfo.InvariantCulture));
                offset += 3;
                return true;
            case PtgNum:
                if (tokens.Length - offset < 9) { return false; }
                long bits = BinaryPrimitives.ReadInt64LittleEndian(tokens.Slice(offset + 1, 8));
                expressions.Push(BitConverter.Int64BitsToDouble(bits).ToString("R", CultureInfo.InvariantCulture));
                offset += 9;
                return true;
            case PtgBool:
                if (tokens.Length - offset < 2) { return false; }
                expressions.Push(tokens[offset + 1] == 0 ? "FALSE" : "TRUE");
                offset += 2;
                return true;
            case PtgErr:
                if (tokens.Length - offset < 2) { return false; }
                expressions.Push(GetErrorText(tokens[offset + 1]));
                offset += 2;
                return true;
            case PtgStr:
                offset++;
                expressions.Push($"\"{XlsbBinary.ReadWideString(tokens, ref offset).Replace("\"", "\"\"", StringComparison.Ordinal)}\"");
                return true;
            default:
                return false;
        }
    }

    private static bool TryHandleReference(
        ReadOnlySpan<byte> tokens,
        ref int offset,
        Stack<string> expressions,
        XlsbFormulaContext? context)
    {
        int ptg = tokens[offset] & OperandTokenMask;
        switch (ptg)
        {
            case PtgRef:
                if (tokens.Length - offset < 7) { return false; }
                expressions.Push(DecodeCellReference(tokens.Slice(offset + 1, 6)));
                offset += 7;
                return true;
            case PtgArea:
                if (tokens.Length - offset < 13) { return false; }
                expressions.Push(DecodeAreaReference(tokens.Slice(offset + 1, 12)));
                offset += 13;
                return true;
            case PtgRefErr:
                if (tokens.Length - offset < 7) { return false; }
                expressions.Push("#REF!");
                offset += 7;
                return true;
            case PtgAreaErr:
                if (tokens.Length - offset < 13) { return false; }
                expressions.Push("#REF!");
                offset += 13;
                return true;
            default:
                return TryHandle3dReference(tokens, ref offset, expressions, context, ptg);
        }
    }

    private static bool TryHandle3dReference(
        ReadOnlySpan<byte> tokens,
        ref int offset,
        Stack<string> expressions,
        XlsbFormulaContext? context,
        int ptg)
    {
        (int Length, bool IsArea, bool IsError) properties = ptg switch
        {
            PtgRef3d => (9, false, false),
            PtgArea3d => (15, true, false),
            PtgRefErr3d => (7, false, true),
            PtgAreaErr3d => (7, true, true),
            _ => default,
        };
        if (properties.Length == 0 || tokens.Length - offset < properties.Length)
        {
            return false;
        }

        expressions.Push(Decode3dReference(
            tokens.Slice(offset, properties.Length),
            context,
            properties.IsArea,
            properties.IsError));
        offset += properties.Length;
        return true;
    }

    private static string Decode3dReference(
        ReadOnlySpan<byte> token,
        XlsbFormulaContext? context,
        bool isArea,
        bool isError)
    {
        int externSheetIndex = BinaryPrimitives.ReadUInt16LittleEndian(token.Slice(1, 2));
        if (context is null || !context.TryResolveSheet(externSheetIndex, out _, out string sheetPrefix))
        {
            return "#REF!";
        }

        if (isError)
        {
            return $"{sheetPrefix}!#REF!";
        }

        string address = isArea
            ? DecodeAreaReference(token[3..])
            : DecodeCellReference(token[3..]);
        return $"{sheetPrefix}!{address}";
    }

    private static int GetTokenLength(ReadOnlySpan<byte> tokens, int offset)
    {
        byte token = tokens[offset];
        if (token is >= PtgAdd and <= PtgParen)
        {
            return 1;
        }

        if (token == PtgStr)
        {
            if (tokens.Length - offset < 5)
            {
                return -1;
            }

            uint charCount = BinaryPrimitives.ReadUInt32LittleEndian(tokens.Slice(offset + 1, 4));
            long stringLength = 5L + (charCount * 2L);
            return stringLength <= tokens.Length - offset ? (int)stringLength : -1;
        }

        if (token is PtgErr or PtgBool) { return tokens.Length - offset >= 2 ? 2 : -1; }
        if (token == PtgInt) { return tokens.Length - offset >= 3 ? 3 : -1; }
        if (token == PtgNum) { return tokens.Length - offset >= 9 ? 9 : -1; }

        int ptg = token & OperandTokenMask;
        int length = ptg switch
        {
            PtgRef or PtgRefErr => 7,
            PtgArea or PtgAreaErr => 13,
            PtgRef3d => 9,
            PtgArea3d => 15,
            PtgRefErr3d or PtgAreaErr3d => 7,
            _ => -1,
        };
        return length <= tokens.Length - offset ? length : -1;
    }

    private static string DecodeCellReference(ReadOnlySpan<byte> loc)
    {
        uint row = BinaryPrimitives.ReadUInt32LittleEndian(loc[..4]);
        ushort column = BinaryPrimitives.ReadUInt16LittleEndian(loc.Slice(4, 2));
        return FormatAddress(row, column);
    }

    private static string DecodeAreaReference(ReadOnlySpan<byte> loc)
    {
        string firstAddress = FormatAddress(
            BinaryPrimitives.ReadUInt32LittleEndian(loc[..4]),
            BinaryPrimitives.ReadUInt16LittleEndian(loc.Slice(8, 2)));
        string lastAddress = FormatAddress(
            BinaryPrimitives.ReadUInt32LittleEndian(loc.Slice(4, 4)),
            BinaryPrimitives.ReadUInt16LittleEndian(loc.Slice(10, 2)));
        return string.Equals(firstAddress, lastAddress, StringComparison.Ordinal)
            ? firstAddress
            : $"{firstAddress}:{lastAddress}";
    }

    private static string FormatAddress(uint zeroBasedRow, ushort columnFlags)
    {
        if (zeroBasedRow >= ExcelLimits.MaxRows)
        {
            return "#REF!";
        }

        int zeroBasedColumn = columnFlags & ColumnMask;
        if (zeroBasedColumn >= ExcelLimits.MaxColumns)
        {
            return "#REF!";
        }

        string columnPrefix = (columnFlags & ColumnRelativeFlag) == 0 ? "$" : string.Empty;
        string rowPrefix = (columnFlags & RowRelativeFlag) == 0 ? "$" : string.Empty;
        return $"{columnPrefix}{FormatColumnName(zeroBasedColumn)}{rowPrefix}{zeroBasedRow + 1}";
    }

    private static string FormatColumnName(int zeroBasedColumn)
    {
        Span<char> buffer = stackalloc char[3];
        int position = buffer.Length;
        int value = zeroBasedColumn + 1;
        while (value > 0)
        {
            value--;
            buffer[--position] = (char)('A' + (value % 26));
            value /= 26;
        }

        return new string(buffer[position..]);
    }

    private static bool TryPopBinary(Stack<string> expressions, out string left, out string right)
    {
        if (expressions.Count < 2)
        {
            left = string.Empty;
            right = string.Empty;
            return false;
        }

        right = expressions.Pop();
        left = expressions.Pop();
        return true;
    }

    private static string GetErrorText(byte errorCode) => errorCode switch
    {
        0x00 => "#NULL!",
        0x07 => "#DIV/0!",
        0x0F => "#VALUE!",
        0x17 => "#REF!",
        0x1D => "#NAME?",
        0x24 => "#NUM!",
        0x2A => "#N/A",
        0x2B => "#GETTING_DATA",
        _ => $"#ERR{errorCode}",
    };
}
