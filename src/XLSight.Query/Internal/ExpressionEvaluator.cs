using System.Globalization;

namespace XLSight.Query.Internal;

/// <summary>Bound, AOT-safe scalar evaluation. Nodes cache only the current row's value.</summary>
internal sealed class ExpressionEvaluator(Func<string, int> resolveColumn, Func<QueryExpression, int?>? resolveSlot = null)
{
    private readonly Dictionary<string, BoundValue> _nodes = new(StringComparer.Ordinal);
    private ExcelRow _row;
    private ExcelCellValue[] _slots = [];
    private long _epoch;

    internal HashSet<int> Columns { get; } = [];

    internal void BeginRow(in ExcelRow row)
    {
        _row = row;
        _epoch++;
    }

    internal void BeginResult(ExcelCellValue[] slots)
    {
        _slots = slots;
        _epoch++;
    }

    internal BoundValue Bind(QueryExpression expression)
    {
        string key = Identity(expression);
        if (_nodes.TryGetValue(key, out BoundValue? existing)) { return existing; }
        BoundValue node;
        if (resolveSlot?.Invoke(expression) is int slot)
        {
            node = New(() => _slots[slot]);
        }
        else
        {
            node = expression switch
            {
                ColumnExpression column => BindColumn(column.Name),
                LiteralExpression literal => New(() => literal.Value, constant: true),
                UnaryExpression unary => BindUnary(unary),
                BinaryExpression binary => BindBinary(binary),
                EmptyExpression empty => BindEmpty(empty),
                InExpression membership => BindIn(membership),
                FunctionExpression function => BindFunction(function),
                _ => throw new QueryDslException("Aggregate is not valid in this expression."),
            };
        }

        _nodes.Add(key, node);
        return node;
    }

    private BoundValue New(Func<ExcelCellValue> evaluate, bool constant = false) => new(this, evaluate, constant);

    private BoundValue BindColumn(string name)
    {
        int index = resolveColumn(name);
        return BindColumnIndex(index);
    }

    internal BoundValue BindColumnIndex(int index)
    {
        Columns.Add(index);
        return New(() => _row.GetCell(index));
    }

    private BoundValue BindUnary(UnaryExpression expression)
    {
        BoundValue operand = Bind(expression.Operand);
        string op = expression.Operator.ToUpperInvariant();
        return New(() => op switch
        {
            "NOT" => operand.Value.TryGetBoolean(out bool boolean) ? Boolean(!boolean) : ExcelCellValue.Empty,
            "+" => operand.Value.TryGetNumber(out double number) ? Number(number) : ExcelCellValue.Empty,
            "-" => operand.Value.TryGetNumber(out double number) ? Number(-number) : ExcelCellValue.Empty,
            _ => ExcelCellValue.Empty,
        }, operand.IsConstant);
    }

    private BoundValue BindBinary(BinaryExpression expression)
    {
        BoundValue left = Bind(expression.Left);
        BoundValue right = Bind(expression.Right);
        string op = expression.Operator.ToUpperInvariant();
        return New(() =>
        {
            ExcelCellValue a = left.Value;
            if (op is "AND" or "OR")
            {
                bool knownA = a.TryGetBoolean(out bool boolA);
                if (knownA && (op is "AND" ? !boolA : boolA)) { return Boolean(boolA); }
                bool knownB = right.Value.TryGetBoolean(out bool boolB);
                if (knownB && (op is "AND" ? !boolB : boolB)) { return Boolean(boolB); }
                return knownA && knownB ? Boolean(boolB) : ExcelCellValue.Empty;
            }

            ExcelCellValue b = right.Value;
            if (op is "+" or "-" or "*" or "/" or "%")
            {
                if (!a.TryGetNumber(out double x) || !b.TryGetNumber(out double y)) { return ExcelCellValue.Empty; }
                return Number(op switch
                {
                    "+" => x + y, "-" => x - y, "*" => x * y,
                    "/" => y == 0 ? double.NaN : x / y,
                    _ => y == 0 ? double.NaN : x % y,
                });
            }

            if (Compare(a, b) is not int cmp) { return ExcelCellValue.Empty; }
            if (a.CellType == CellType.Boolean && op is not ("=" or "!=" or "<>")) { return ExcelCellValue.Empty; }
            return Boolean(op switch
            {
                "=" => cmp == 0, "!=" or "<>" => cmp != 0, "<" => cmp < 0,
                "<=" => cmp <= 0, ">" => cmp > 0, ">=" => cmp >= 0, _ => false,
            });
        }, left.IsConstant && right.IsConstant);
    }

    private BoundValue BindEmpty(EmptyExpression expression)
    {
        BoundValue operand = Bind(expression.Operand);
        return New(() => Boolean(operand.Value.IsEmpty != expression.Negated), operand.IsConstant);
    }

    private BoundValue BindIn(InExpression expression)
    {
        BoundValue operand = Bind(expression.Operand);
        if (expression.Values.Any(v => v is not LiteralExpression))
        {
            throw new QueryDslException("IN requires a list of literal values.");
        }

        ExcelCellValue[] values = expression.Values.Cast<LiteralExpression>().Select(v => v.Value).ToArray();
        HashSet<ExcelCellValue>? set = values.Length > 8 ? new(values) : null;
        CellType[] types = values.Select(v => v.CellType).Distinct().ToArray();
        return New(() =>
        {
            ExcelCellValue value = operand.Value;
            if (value.IsEmpty || value.CellType == CellType.Error
                || (value.TryGetNumber(out double number) && !double.IsFinite(number))) { return ExcelCellValue.Empty; }
            bool found = set?.Contains(value) ?? Array.IndexOf(values, value) >= 0;
            bool unknown = false;
            foreach (CellType type in types) { unknown |= type != value.CellType || type == CellType.Empty; }
            return !found && unknown ? ExcelCellValue.Empty : Boolean(found != expression.Negated);
        }, operand.IsConstant);
    }

    private BoundValue BindFunction(FunctionExpression expression)
    {
        string name = expression.Name.ToUpperInvariant();
        BoundValue[] args = expression.Arguments.Select(Bind).ToArray();
        int count = args.Length;
        bool valid = name switch
        {
            "YEAR" or "MONTH" or "DAY" or "ABS" => count == 1,
            "ROUND" => count is 1 or 2,
            "DATE_TRUNC" => count == 2,
            "COALESCE" => count > 0,
            _ => false,
        };
        if (!valid) { throw new QueryDslException($"Unknown scalar function or invalid argument count: {name}."); }
        if (name is "DATE_TRUNC" && (expression.Arguments[0] is not LiteralExpression literal
            || !literal.Value.TryGetText(out string? unit) || unit is not ("year" or "month" or "day")))
        {
            throw new QueryDslException("DATE_TRUNC requires a literal unit: 'year', 'month', or 'day'.");
        }

        return New(() =>
        {
            if (name is "COALESCE")
            {
                foreach (BoundValue arg in args)
                {
                    ExcelCellValue value = arg.Value;
                    if (!value.IsEmpty) { return value; }
                }
                return ExcelCellValue.Empty;
            }
            if (name is "DATE_TRUNC")
            {
                if (!args[1].Value.TryGetDate(out DateTime date)) { return ExcelCellValue.Empty; }
                string unit = args[0].Value.AsText();
                return ExcelCellValue.FromDate(unit switch
                {
                    "year" => new DateTime(date.Year, 1, 1),
                    "month" => new DateTime(date.Year, date.Month, 1),
                    _ => date.Date,
                });
            }
            ExcelCellValue first = args[0].Value;
            if (name is "YEAR" or "MONTH" or "DAY")
            {
                return first.TryGetDate(out DateTime date)
                    ? Number(name is "YEAR" ? date.Year : name is "MONTH" ? date.Month : date.Day)
                    : ExcelCellValue.Empty;
            }
            if (!first.TryGetNumber(out double n)) { return ExcelCellValue.Empty; }
            if (name is "ABS") { return Number(Math.Abs(n)); }
            double digits = 0;
            if (count == 2 && !args[1].Value.TryGetNumber(out digits)) { return ExcelCellValue.Empty; }
            if (!double.IsFinite(digits) || digits != Math.Truncate(digits) || digits is < 0 or > 15) { return ExcelCellValue.Empty; }
            return Number(Math.Round(n, (int)digits, MidpointRounding.AwayFromZero));
        }, args.All(a => a.IsConstant));
    }

    private static ExcelCellValue Number(double value) => double.IsFinite(value) ? ExcelCellValue.FromNumber(value) : ExcelCellValue.Empty;
    private static ExcelCellValue Boolean(bool value) => ExcelCellValue.FromBoolean(value);
    internal static bool IsTrue(ExcelCellValue value) => value.TryGetBoolean(out bool result) && result;

    private static int? Compare(ExcelCellValue a, ExcelCellValue b)
    {
        if (a.CellType != b.CellType || a.IsEmpty
            || (a.TryGetNumber(out double x) && (!double.IsFinite(x) || !double.IsFinite(b.AsNumber())))) { return null; }
        return a.CellType switch
        {
            CellType.Number => a.AsNumber().CompareTo(b.AsNumber()),
            CellType.Date => a.AsDate().CompareTo(b.AsDate()),
            CellType.Text => string.CompareOrdinal(a.AsText(), b.AsText()),
            CellType.Boolean => a.AsBoolean().CompareTo(b.AsBoolean()),
            _ => null,
        };
    }

    // Length prefixes keep string literals and quoted column names unambiguous.
    internal static string Identity(QueryExpression expression, Func<string, string>? normalizeColumn = null)
    {
        static string Part(string text) => $"{text.Length}:{text}";
        string Key(QueryExpression e) => Identity(e, normalizeColumn);
        string Parts(IEnumerable<QueryExpression> expressions) => string.Concat(expressions.Select(e => Part(Key(e))));
        return expression switch
        {
            ColumnExpression c => "c" + Part(normalizeColumn?.Invoke(c.Name) ?? c.Name),
            LiteralExpression l => "l" + l.Value.CellType + Part(l.Value.CellType == CellType.Number
                ? l.Value.AsNumber().ToString("R", CultureInfo.InvariantCulture)
                : l.Value.CellType == CellType.Date ? l.Value.AsDate().Ticks.ToString(CultureInfo.InvariantCulture) : l.Value.ToString()),
            UnaryExpression u => "u" + Part(u.Operator.ToUpperInvariant()) + Part(Key(u.Operand)),
            BinaryExpression b => "b" + Part(b.Operator.ToUpperInvariant()) + Parts([b.Left, b.Right]),
            FunctionExpression f => "f" + Part(f.Name.ToUpperInvariant()) + Parts(f.Arguments),
            AggregateExpression a => "a" + a.Kind + (a.Argument is null ? "-" : Part(Key(a.Argument)))
                + (a.Filter is null ? "-" : Part(Key(a.Filter))),
            InExpression i => "i" + i.Negated + Part(Key(i.Operand)) + Parts(i.Values),
            EmptyExpression e => "e" + e.Negated + Part(Key(e.Operand)),
            _ => throw new QueryDslException("Unknown expression."),
        };
    }

    internal static IEnumerable<AggregateExpression> Aggregates(QueryExpression expression)
    {
        if (expression is AggregateExpression aggregate) { yield return aggregate; yield break; }
        foreach (QueryExpression child in Children(expression))
        {
            foreach (AggregateExpression nested in Aggregates(child)) { yield return nested; }
        }
    }

    // Expand only references in the clause. Columns inside a selected expression
    // refer to the source, even when another selection uses that name as an alias.
    internal static QueryExpression ReplaceAliases(QueryExpression expression, IReadOnlyDictionary<string, QueryExpression> aliases) => expression switch
    {
        ColumnExpression c when aliases.TryGetValue(c.Name, out QueryExpression? value) => value,
        UnaryExpression u => u with { Operand = ReplaceAliases(u.Operand, aliases) },
        BinaryExpression b => b with { Left = ReplaceAliases(b.Left, aliases), Right = ReplaceAliases(b.Right, aliases) },
        FunctionExpression f => f with { Arguments = f.Arguments.Select(a => ReplaceAliases(a, aliases)).ToArray() },
        InExpression i => i with { Operand = ReplaceAliases(i.Operand, aliases) },
        EmptyExpression e => e with { Operand = ReplaceAliases(e.Operand, aliases) },
        _ => expression,
    };

    private static IEnumerable<QueryExpression> Children(QueryExpression expression) => expression switch
    {
        UnaryExpression u => [u.Operand], BinaryExpression b => [b.Left, b.Right],
        FunctionExpression f => f.Arguments, InExpression i => [i.Operand, .. i.Values],
        EmptyExpression e => [e.Operand], _ => [],
    };

    internal sealed class BoundValue(ExpressionEvaluator owner, Func<ExcelCellValue> evaluate, bool constant)
    {
        private long _epoch = -1;
        private ExcelCellValue _value;
        internal bool IsConstant => constant;
        internal ExcelCellValue Value
        {
            get
            {
                if (_epoch < 0 || (!constant && _epoch != owner._epoch))
                {
                    _value = evaluate();
                    _epoch = owner._epoch;
                }
                return _value;
            }
        }
    }
}
