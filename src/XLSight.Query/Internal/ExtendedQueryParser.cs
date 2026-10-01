using System.Globalization;
using System.Text;

namespace XLSight.Query.Internal;

/// <summary>Parses the expression-based query language used by the newer query executor.</summary>
internal static class ExtendedQueryParser
{
    private const int MaxTokens = 8192;
    private const int MaxExpressionDepth = 64;
    private const int MaxInputLength = 1_048_576;

    public static ExtendedQueryPlan Parse(string queryText)
    {
        ArgumentNullException.ThrowIfNull(queryText);
        if (queryText.Length > MaxInputLength)
        {
            throw new QueryDslException($"Query text exceeds the maximum length of {MaxInputLength} characters.");
        }

        return new Parser(queryText).Parse();
    }

    private sealed class Parser
    {
        private readonly TokenReader _tokens;

        public Parser(string text) => _tokens = new TokenReader(text);

        public ExtendedQueryPlan Parse()
        {
            (string sheet, string rangeAddress, ExcelRange range, SheetQueryHeader header,
                bool selectAll, List<QuerySelection> selections) = ParseSourceAndSelect();
            (QueryExpression? where, List<QueryExpression> groupBy, QueryExpression? having,
                QueryExpression? orderBy, bool orderDescending, int? limit) = ParseClauses();
            ValidateShape(selectAll, selections, where, groupBy, having, orderBy, limit);

            return new ExtendedQueryPlan(
                sheet, rangeAddress, range, header, selectAll,
                selections.ToArray(), where, groupBy.ToArray(), having, orderBy, orderDescending, limit);
        }

        private (string Sheet, string RangeAddress, ExcelRange Range, SheetQueryHeader Header,
            bool SelectAll, List<QuerySelection> Selections) ParseSourceAndSelect()
        {
            if (_tokens.CurrentIsKeyword("FROM"))
            {
                (string sheet, string address, ExcelRange range) = ParseFrom();
                SheetQueryHeader header = ParseHeader();
                (bool all, List<QuerySelection> selected) = ParseSelect();
                return (sheet, address, range, header, all, selected);
            }

            if (_tokens.CurrentIsKeyword("SELECT"))
            {
                (bool all, List<QuerySelection> selected) = ParseSelect();
                (string sheet, string address, ExcelRange range) = ParseFrom();
                SheetQueryHeader header = ParseHeader();
                return (sheet, address, range, header, all, selected);
            }

            throw Error("Expected FROM or SELECT at the start of the query.");
        }

        private (QueryExpression? Where, List<QueryExpression> GroupBy, QueryExpression? Having,
            QueryExpression? OrderBy, bool OrderDescending, int? Limit) ParseClauses()
        {
            QueryExpression? where = ParseWhereClause();
            List<QueryExpression> groupBy = ParseGroupByClause();
            QueryExpression? having = ParseOptionalExpression("HAVING");
            (QueryExpression? orderBy, bool descending) = ParseOrderByClause();
            int? limit = TryConsumeKeyword("LIMIT") ? ParseNonNegativeInteger("LIMIT") : null;
            if (!_tokens.Current.IsEnd)
            {
                throw Error($"Unexpected token '{_tokens.CurrentDisplay}'. Expected WHERE, GROUP BY, HAVING, ORDER BY, or LIMIT.");
            }

            return (where, groupBy, having, orderBy, descending, limit);
        }

        private QueryExpression? ParseWhereClause()
        {
            if (!TryConsumeKeyword("WHERE")) { return null; }
            QueryExpression expression = ParseExpression();
            EnsureNoAggregate(expression, "WHERE");
            return expression;
        }

        private List<QueryExpression> ParseGroupByClause()
        {
            var groupBy = new List<QueryExpression>();
            if (!TryConsumeKeyword("GROUP")) { return groupBy; }
            ExpectKeyword("BY");
            do
            {
                QueryExpression expression = ParseExpression();
                EnsureNoAggregate(expression, "GROUP BY");
                groupBy.Add(expression);
            }
            while (TryConsume(TokenKind.Comma));
            return groupBy;
        }

        private QueryExpression? ParseOptionalExpression(string keyword)
        {
            if (!TryConsumeKeyword(keyword)) { return null; }
            return ParseExpression();
        }

        private (QueryExpression? Expression, bool Descending) ParseOrderByClause()
        {
            if (!TryConsumeKeyword("ORDER")) { return (null, false); }
            ExpectKeyword("BY");
            QueryExpression expression = ParseExpression();
            bool descending = TryConsumeKeyword("DESC");
            if (!descending) { TryConsumeKeyword("ASC"); }
            return (expression, descending);
        }

        private (string Sheet, string RangeAddress, ExcelRange Range) ParseFrom()
        {
            ExpectKeyword("FROM");
            string sheet = ParseName("sheet name");
            Expect(TokenKind.Bang, "Expected '!' between sheet name and range.");
            string start = ParseRangePart("range start");
            Expect(TokenKind.Colon, "FROM range must be a bounded A1 range such as A1:F100.");
            string end = ParseRangePart("range end");
            string address = $"{start}:{end}".ToUpperInvariant();
            if (!ExcelAddress.TryParse(start, out _) ||
                !ExcelAddress.TryParse(end, out _) ||
                !ExcelRange.TryParse(address, out ExcelRange range))
            {
                throw Error("FROM range must be a bounded A1 range such as A1:F100.");
            }

            return (sheet, address, range);
        }

        private SheetQueryHeader ParseHeader()
        {
            ExpectKeyword("HEADER");
            if (TryConsumeKeyword("AUTO"))
            {
                return SheetQueryHeader.Auto();
            }

            if (TryConsumeKeyword("ROW"))
            {
                return SheetQueryHeader.FromRow(ParsePositiveInteger("HEADER ROW"));
            }

            if (TryConsumeKeyword("COLUMN"))
            {
                string column = ParseName("HEADER COLUMN");
                if (!ExcelAddress.TryParse($"{column}1", out _))
                {
                    throw Error($"Invalid HEADER COLUMN '{column}'. Expected an Excel column such as A or BC.");
                }

                return SheetQueryHeader.FromColumn(column.ToUpperInvariant());
            }

            throw Error("Expected HEADER ROW <number>, HEADER AUTO, or HEADER COLUMN <column>.");
        }

        private (bool SelectAll, List<QuerySelection> Selections) ParseSelect()
        {
            ExpectKeyword("SELECT");
            if (TryConsume(TokenKind.Star))
            {
                return (true, []);
            }

            var selections = new List<QuerySelection>();
            var aliases = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            do
            {
                QueryExpression expression = ParseExpression();
                string? alias = null;
                if (TryConsumeKeyword("AS"))
                {
                    alias = ParseName("SELECT alias");
                    if (!aliases.Add(alias))
                    {
                        throw Error($"Duplicate alias '{alias}'.");
                    }
                }

                selections.Add(new QuerySelection(expression, alias));
            }
            while (TryConsume(TokenKind.Comma));

            if (selections.Count == 0)
            {
                throw Error("SELECT requires at least one expression.");
            }

            return (false, selections);
        }

        private QueryExpression ParseExpression() => ParseOr();

        private QueryExpression ParseOr()
        {
            QueryExpression left = ParseAnd();
            while (TryConsumeKeyword("OR"))
            {
                left = MakeBinary("OR", left, ParseAnd());
            }

            return left;
        }

        private QueryExpression ParseAnd()
        {
            QueryExpression left = ParseNot();
            while (TryConsumeKeyword("AND"))
            {
                left = MakeBinary("AND", left, ParseNot());
            }

            return left;
        }

        private QueryExpression ParseNot()
        {
            if (TryConsumeKeyword("NOT"))
            {
                _tokens.EnterDepth();
                try
                {
                    return MakeUnary("NOT", ParseNot());
                }
                finally
                {
                    _tokens.LeaveDepth();
                }
            }

            return ParseComparison();
        }

        private QueryExpression ParseComparison()
        {
            QueryExpression left = ParseAdditive();

            if (TryConsumeKeyword("IS"))
            {
                bool negated = TryConsumeKeyword("NOT");
                if (!TryConsumeKeyword("EMPTY") && !TryConsumeKeyword("NULL"))
                {
                    throw Error("Expected EMPTY or NULL after IS.");
                }

                return new EmptyExpression(left, negated);
            }

            bool inNegated = false;
            if (TryConsumeKeyword("NOT"))
            {
                inNegated = true;
                if (!_tokens.CurrentIsKeyword("IN"))
                {
                    throw Error("Expected IN after NOT in a comparison.");
                }
            }

            if (TryConsumeKeyword("IN"))
            {
                Expect(TokenKind.OpenParen, "Expected '(' after IN.");
                var values = new List<QueryExpression>();
                if (!_tokens.Current.Is(TokenKind.CloseParen))
                {
                    do
                    {
                        values.Add(new LiteralExpression(ParseLiteral("IN value")));
                    }
                    while (TryConsume(TokenKind.Comma));
                }

                Expect(TokenKind.CloseParen, "Expected ')' after IN values.");
                if (values.Count == 0)
                {
                    throw Error("IN requires at least one literal value.");
                }

                return new InExpression(left, values.ToArray(), inNegated);
            }

            if (inNegated)
            {
                throw Error("Expected IN after NOT in a comparison.");
            }

            if (TryConsumeComparisonOperator(out string? op))
            {
                QueryExpression right = ParseAdditive(comparisonRight: true);
                return MakeBinary(op!, left, right);
            }

            return left;
        }

        private QueryExpression ParseAdditive(bool comparisonRight = false)
        {
            QueryExpression left = ParseMultiplicative(comparisonRight);
            while (true)
            {
                if (TryConsume(TokenKind.Plus))
                {
                    left = MakeBinary("+", left, ParseMultiplicative(comparisonRight));
                }
                else if (TryConsume(TokenKind.Minus))
                {
                    left = MakeBinary("-", left, ParseMultiplicative(comparisonRight));
                }
                else
                {
                    return left;
                }
            }
        }

        private QueryExpression ParseMultiplicative(bool comparisonRight = false)
        {
            QueryExpression left = ParseUnary(comparisonRight);
            while (true)
            {
                if (TryConsume(TokenKind.Star))
                {
                    left = MakeBinary("*", left, ParseUnary(comparisonRight));
                }
                else if (TryConsume(TokenKind.Slash))
                {
                    left = MakeBinary("/", left, ParseUnary(comparisonRight));
                }
                else if (TryConsume(TokenKind.Percent))
                {
                    left = MakeBinary("%", left, ParseUnary(comparisonRight));
                }
                else
                {
                    return left;
                }
            }
        }

        private QueryExpression ParseUnary(bool comparisonRight = false)
        {
            if (TryConsume(TokenKind.Plus))
            {
                _tokens.EnterDepth();
                try
                {
                    return MakeUnary("+", ParseUnary(comparisonRight));
                }
                finally
                {
                    _tokens.LeaveDepth();
                }
            }

            if (TryConsume(TokenKind.Minus))
            {
                _tokens.EnterDepth();
                try
                {
                    return MakeUnary("-", ParseUnary(comparisonRight));
                }
                finally
                {
                    _tokens.LeaveDepth();
                }
            }

            return ParsePrimary(comparisonRight);
        }

        private QueryExpression ParsePrimary(bool comparisonRight)
        {
            _tokens.EnterDepth();
            try
            {
                if (TryConsume(TokenKind.OpenParen))
                {
                    QueryExpression expression = ParseExpression();
                    Expect(TokenKind.CloseParen, "Expected ')' to close expression.");
                    return expression;
                }

                return ParsePrimaryValue(comparisonRight);
            }
            finally
            {
                _tokens.LeaveDepth();
            }
        }

        private QueryExpression ParsePrimaryValue(bool comparisonRight)
        {
            Token token = _tokens.Current;

            if (token.Kind is TokenKind.Integer or TokenKind.Number or TokenKind.StringLiteral)
            {
                return new LiteralExpression(ParseLiteral("expression literal"));
            }

            if (token.Kind is TokenKind.QuotedText)
            {
                _tokens.MoveNext();
                return comparisonRight
                    ? new LiteralExpression(ExcelCellValue.FromText(_tokens.GetText(token)))
                    : new ColumnExpression(_tokens.GetText(token));
            }

            if (token.Kind is TokenKind.BracketedIdentifier)
            {
                _tokens.MoveNext();
                return new ColumnExpression(_tokens.GetText(token));
            }

            if (!token.IsIdentifier)
            {
                throw Error("Expected a column, literal, function, or parenthesized expression.");
            }

            if (_tokens.IsKeyword(token, "TRUE") || _tokens.IsKeyword(token, "FALSE") || _tokens.IsKeyword(token, "DATE")
                    || _tokens.IsKeyword(token, "NULL") || _tokens.IsKeyword(token, "EMPTY"))
            {
                return new LiteralExpression(ParseLiteral("expression literal"));
            }

            string name = _tokens.GetText(token);
            _tokens.MoveNext();
            if (!TryConsume(TokenKind.OpenParen))
            {
                return new ColumnExpression(name);
            }

            return ParseCall(name);
        }

        private QueryExpression ParseCall(string name)
        {
            string upper = name.ToUpperInvariant();
            if (IsAggregate(upper))
            {
                return ParseAggregateCall(name, upper);
            }

            if (string.Equals(upper, "FILTER", StringComparison.Ordinal))
            {
                throw Error("FILTER is only valid after an aggregate function.");
            }

            return ParseScalarCall(name, upper);
        }

        private AggregateExpression ParseAggregateCall(string name, string upper)
        {
            QueryExpression? argument = _tokens.Current.Is(TokenKind.CloseParen) ? null : ParseExpression();
            if (argument is not null && ContainsAggregate(argument))
            {
                throw Error("Nested aggregate functions are not supported.");
            }

            Expect(TokenKind.CloseParen, $"Expected ')' after aggregate '{name}'.");
            QueryExpression? filter = null;
            if (TryConsumeKeyword("FILTER"))
            {
                Expect(TokenKind.OpenParen, "Expected '(' after FILTER.");
                ExpectKeyword("WHERE");
                filter = ParseExpression();
                EnsureNoAggregate(filter, "aggregate FILTER");
                Expect(TokenKind.CloseParen, "Expected ')' after aggregate FILTER.");
            }

            if (string.Equals(upper, "COUNT", StringComparison.Ordinal) && argument is not null)
            {
                throw Error("COUNT() does not accept an argument.");
            }

            if (!string.Equals(upper, "COUNT", StringComparison.Ordinal) && argument is null)
            {
                throw Error($"{name.ToUpperInvariant()} requires an argument.");
            }

            return new AggregateExpression(ToAggregateKind(upper), argument, filter);
        }

        private FunctionExpression ParseScalarCall(string name, string upper)
        {
            var args = new List<QueryExpression>();
            if (!_tokens.Current.Is(TokenKind.CloseParen))
            {
                do
                {
                    args.Add(ParseExpression());
                }
                while (TryConsume(TokenKind.Comma));
            }

            Expect(TokenKind.CloseParen, $"Expected ')' after function '{name}'.");
            ValidateFunction(upper, args.Count);
            if (upper is "DATE_TRUNC" && (args[0] is not LiteralExpression literal
                || !literal.Value.TryGetText(out string? unit) || unit is not ("year" or "month" or "day")))
            {
                throw Error("DATE_TRUNC requires a literal unit: 'year', 'month', or 'day'.");
            }

            return new FunctionExpression(upper, args.ToArray());
        }

        private static void ValidateFunction(string function, int count)
        {
            bool valid = function switch
            {
                "YEAR" or "MONTH" or "DAY" or "ABS" => count == 1,
                "DATE_TRUNC" => count == 2,
                "COALESCE" => count >= 1,
                "ROUND" => count is 1 or 2,
                _ => false,
            };
            if (!valid)
            {
                throw new QueryDslException($"Unsupported function '{function}' or invalid argument count.");
            }
        }

        private ExcelCellValue ParseLiteral(string context)
        {
            if (TryConsumeKeyword("NULL") || TryConsumeKeyword("EMPTY")) { return ExcelCellValue.Empty; }
            if (_tokens.Current.Kind is TokenKind.Plus or TokenKind.Minus)
            {
                bool negative = TryConsume(TokenKind.Minus);
                if (!negative) { Expect(TokenKind.Plus, "Expected sign."); }
                Token numberToken = _tokens.Current;
                if (numberToken.Kind is not (TokenKind.Integer or TokenKind.Number)) { throw Error("Expected numeric literal after sign."); }
                ExcelCellValue number = ParseLiteral(context);
                return ExcelCellValue.FromNumber(negative ? -number.AsNumber() : number.AsNumber());
            }
            Token token = _tokens.Current;
            if (token.Kind is TokenKind.QuotedText or TokenKind.StringLiteral)
            {
                _tokens.MoveNext();
                return ExcelCellValue.FromText(_tokens.GetText(token));
            }

            if (token.Kind is TokenKind.Integer or TokenKind.Number)
            {
                _tokens.MoveNext();
                if (!double.TryParse(_tokens.GetSpan(token), NumberStyles.Float, CultureInfo.InvariantCulture, out double number) || !double.IsFinite(number))
                {
                    throw Error($"Invalid numeric literal '{_tokens.GetText(token)}'.");
                }

                return ExcelCellValue.FromNumber(number);
            }

            if (_tokens.CurrentIsKeyword("DATE"))
            {
                _tokens.MoveNext();
                Token date = _tokens.Current;
                if (date.Kind is not (TokenKind.QuotedText or TokenKind.StringLiteral) ||
                    !DateTime.TryParseExact(_tokens.GetSpan(date), "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out DateTime value))
                {
                    throw Error("Expected DATE 'yyyy-MM-dd' or DATE \"yyyy-MM-dd\".");
                }

                _tokens.MoveNext();
                return ExcelCellValue.FromDate(value);
            }

            if (_tokens.CurrentIsKeyword("TRUE") || _tokens.CurrentIsKeyword("FALSE"))
            {
                bool value = _tokens.CurrentIsKeyword("TRUE");
                _tokens.MoveNext();
                return ExcelCellValue.FromBoolean(value);
            }

            throw Error($"Expected a literal for {context}: text, number, DATE, TRUE, or FALSE.");
        }

        private bool TryConsumeComparisonOperator(out string? op)
        {
            op = _tokens.Current.Kind switch
            {
                TokenKind.Equals => "=",
                TokenKind.NotEquals => "!=",
                TokenKind.LessThan => "<",
                TokenKind.LessThanOrEqual => "<=",
                TokenKind.GreaterThan => ">",
                TokenKind.GreaterThanOrEqual => ">=",
                _ => null,
            };
            if (op is null)
            {
                return false;
            }

            _tokens.MoveNext();
            return true;
        }

        private string ParseName(string context)
        {
            Token token = _tokens.Current;
            if (token.Kind is not (TokenKind.Identifier or TokenKind.QuotedText or TokenKind.BracketedIdentifier or TokenKind.Integer or TokenKind.Number))
            {
                throw Error($"Expected {context}.");
            }

            _tokens.MoveNext();
            return _tokens.GetText(token);
        }

        private string ParseRangePart(string context)
        {
            Token token = _tokens.Current;
            if (token.Kind is not (TokenKind.Identifier or TokenKind.Integer))
            {
                throw Error($"Expected {context}.");
            }

            _tokens.MoveNext();
            return _tokens.GetText(token);
        }

        private int ParsePositiveInteger(string context)
        {
            int result = ParseNonNegativeInteger(context);
            if (result <= 0)
            {
                throw Error($"{context} must be a positive integer.");
            }

            return result;
        }

        private int ParseNonNegativeInteger(string context)
        {
            bool negative = TryConsume(TokenKind.Minus);
            if (!negative) { TryConsume(TokenKind.Plus); }
            Token token = _tokens.Current;
            if (token.Kind is not TokenKind.Integer ||
                !int.TryParse(_tokens.GetSpan(token), NumberStyles.None, CultureInfo.InvariantCulture, out int result) ||
                result < 0 || (negative && result != 0))
            {
                throw Error($"{context} must be a non-negative integer.");
            }

            _tokens.MoveNext();
            return result;
        }

        private void EnsureNoAggregate(QueryExpression expression, string context)
        {
            if (ContainsAggregate(expression))
            {
                throw Error($"Aggregate functions are not valid in {context}.");
            }
        }

        private static bool ContainsAggregate(QueryExpression expression) => expression switch
        {
            AggregateExpression => true,
            UnaryExpression unary => ContainsAggregate(unary.Operand),
            BinaryExpression binary => ContainsAggregate(binary.Left) || ContainsAggregate(binary.Right),
            FunctionExpression function => function.Arguments.Any(ContainsAggregate),
            InExpression @in => ContainsAggregate(@in.Operand) || @in.Values.Any(ContainsAggregate),
            EmptyExpression empty => ContainsAggregate(empty.Operand),
            _ => false,
        };

        private static bool IsAggregate(string name) => name is "COUNT" or "SUM" or "AVG" or "AVERAGE" or "MIN" or "MAX";

        private static AggregateKind ToAggregateKind(string name) => name switch
        {
            "COUNT" => AggregateKind.Count,
            "SUM" => AggregateKind.Sum,
            "AVG" or "AVERAGE" => AggregateKind.Average,
            "MIN" => AggregateKind.Min,
            "MAX" => AggregateKind.Max,
            _ => throw new ArgumentOutOfRangeException(nameof(name)),
        };

        private void ValidateShape(
            bool selectAll,
            IReadOnlyList<QuerySelection> selections,
            QueryExpression? where,
            List<QueryExpression> groupBy,
            QueryExpression? having,
            QueryExpression? orderBy,
            int? limit)
        {
            if (selectAll && groupBy.Count > 0)
            {
                throw Error("GROUP BY is not valid with SELECT *. Select aggregate functions instead.");
            }

            bool hasAggregate = selections.Any(selection => ContainsAggregate(selection.Expression))
                || (having is not null && ContainsAggregate(having))
                || (orderBy is not null && ContainsAggregate(orderBy));
            if (selectAll && hasAggregate)
            {
                throw Error("Cannot mix SELECT * and aggregates.");
            }

            if (having is not null && !hasAggregate && groupBy.Count == 0)
            {
                throw Error("HAVING requires an aggregate query or GROUP BY.");
            }

            if (hasAggregate || groupBy.Count > 0)
            {
                ValidateResultExpressions(selections, groupBy, having);
            }

            if (orderBy is not null && groupBy.Count > 0)
            {
                ValidateGroupedOrderBy(orderBy, selections, groupBy);
            }

            if (orderBy is not null && groupBy.Count == 0)
            {
                if (hasAggregate)
                {
                    throw Error("ORDER BY is not valid on a global aggregate, which returns a single row. Add GROUP BY to rank aggregated groups.");
                }

                if (limit is null)
                {
                    throw Error("ORDER BY requires LIMIT on row results. Add LIMIT n, or GROUP BY to rank aggregated groups.");
                }
            }

            if (where is not null)
            {
                ValidateBooleanComparisons(where);
            }
            IEnumerable<QueryExpression> expressions = selections.Select(s => s.Expression).Concat(groupBy);
            if (where is not null) { expressions = expressions.Append(where); }
            if (having is not null) { expressions = expressions.Append(having); }
            if (orderBy is not null) { expressions = expressions.Append(orderBy); }
            if (expressions.Any(e => ExpressionDepth(e) > MaxExpressionDepth))
            {
                throw Error($"Expression nesting exceeds the maximum depth of {MaxExpressionDepth}.");
            }
        }

        private void ValidateResultExpressions(IReadOnlyList<QuerySelection> selections,
            List<QueryExpression> groupBy, QueryExpression? having)
        {
            foreach (QuerySelection selection in selections)
            {
                if (!IsResultExpression(selection.Expression, groupBy))
                {
                    throw Error(groupBy.Count == 0
                        ? "Cannot mix raw columns and aggregates without GROUP BY."
                        : "Raw selections with GROUP BY must also appear in GROUP BY.");
                }
            }
            if (having is null) { return; }
            var aliases = selections.Where(s => s.Alias is not null)
                .ToDictionary(s => s.Alias!, s => s.Expression, StringComparer.OrdinalIgnoreCase);
            if (!IsResultExpression(ExpressionEvaluator.ReplaceAliases(having, aliases), groupBy))
            {
                throw Error("HAVING columns must be grouped or aggregated.");
            }
        }

        private static bool IsResultExpression(QueryExpression expression, IReadOnlyList<QueryExpression> groupBy)
        {
            if (groupBy.Any(group => SameExpression(group, expression))) { return true; }
            return expression switch
            {
                AggregateExpression or LiteralExpression => true,
                UnaryExpression u => IsResultExpression(u.Operand, groupBy),
                BinaryExpression b => IsResultExpression(b.Left, groupBy) && IsResultExpression(b.Right, groupBy),
                FunctionExpression f => f.Arguments.All(a => IsResultExpression(a, groupBy)),
                InExpression i => IsResultExpression(i.Operand, groupBy),
                EmptyExpression e => IsResultExpression(e.Operand, groupBy),
                _ => false,
            };
        }

        private void ValidateGroupedOrderBy(
            QueryExpression orderBy,
            IReadOnlyList<QuerySelection> selections,
            List<QueryExpression> groupBy)
        {
            var aliases = selections
                .Where(selection => selection.Alias is not null)
                .ToDictionary(selection => selection.Alias!, selection => selection.Expression, StringComparer.OrdinalIgnoreCase);
            AggregateExpression[] aggregates = selections.SelectMany(s => ExpressionEvaluator.Aggregates(s.Expression)).ToArray();
            bool IsValid(QueryExpression expression)
            {
                if (groupBy.Any(group => SameExpression(group, expression))) { return true; }
                if (aggregates.Any(aggregate => SameExpression(aggregate, expression))) { return true; }

                return expression switch
                {
                    LiteralExpression => true,
                    UnaryExpression unary => IsValid(unary.Operand),
                    BinaryExpression binary => IsValid(binary.Left) && IsValid(binary.Right),
                    FunctionExpression function => function.Arguments.All(IsValid),
                    InExpression membership => IsValid(membership.Operand) && membership.Values.All(IsValid),
                    EmptyExpression empty => IsValid(empty.Operand),
                    _ => false,
                };
            }

            if (IsValid(ExpressionEvaluator.ReplaceAliases(orderBy, aliases))) { return; }

            var validKeys = new List<string>(groupBy.Count + selections.Count);
            foreach (QueryExpression group in groupBy)
            {
                AddValidKey(validKeys, ExpressionText.Format(group));
            }

            foreach (QuerySelection selection in selections)
            {
                if (ContainsAggregate(selection.Expression))
                {
                    AddValidKey(validKeys, selection.Alias ?? ExpressionText.Format(selection.Expression));
                }
            }

            string written = ExpressionText.Format(orderBy);
            throw Error($"Unknown ORDER BY key '{written}'. Valid keys: {string.Join(", ", validKeys)}.");
        }

        private static void AddValidKey(List<string> keys, string key)
        {
            if (!keys.Contains(key, StringComparer.OrdinalIgnoreCase)) { keys.Add(key); }
        }

        private void ValidateBooleanComparisons(QueryExpression expression)
        {
            switch (expression)
            {
                case BinaryExpression binary when (binary.Right is LiteralExpression { Value.CellType: CellType.Boolean }
                    || binary.Left is LiteralExpression { Value.CellType: CellType.Boolean })
                    && binary.Operator is "<" or "<=" or ">" or ">=":
                    throw Error($"Boolean predicates support '=' and '!=' only. Operator '{binary.Operator}' is not supported.");
                case UnaryExpression unary:
                    ValidateBooleanComparisons(unary.Operand);
                    break;
                case BinaryExpression binary:
                    ValidateBooleanComparisons(binary.Left);
                    ValidateBooleanComparisons(binary.Right);
                    break;
                case FunctionExpression function:
                    foreach (QueryExpression argument in function.Arguments) { ValidateBooleanComparisons(argument); }
                    break;
                case InExpression membership:
                    ValidateBooleanComparisons(membership.Operand);
                    break;
                case EmptyExpression empty:
                    ValidateBooleanComparisons(empty.Operand);
                    break;
            }
        }

        private UnaryExpression MakeUnary(string op, QueryExpression operand)
        {
            if (ExpressionDepth(operand) + 1 > MaxExpressionDepth)
            {
                throw Error($"Expression nesting exceeds the maximum depth of {MaxExpressionDepth}.");
            }

            return new UnaryExpression(op, operand);
        }

        private BinaryExpression MakeBinary(string op, QueryExpression left, QueryExpression right)
        {
            if (Math.Max(ExpressionDepth(left), ExpressionDepth(right)) + 1 > MaxExpressionDepth)
            {
                throw Error($"Expression nesting exceeds the maximum depth of {MaxExpressionDepth}.");
            }

            return new BinaryExpression(op, left, right);
        }

        private static int ExpressionDepth(QueryExpression expression) => expression switch
        {
            UnaryExpression unary => 1 + ExpressionDepth(unary.Operand),
            BinaryExpression binary => 1 + Math.Max(ExpressionDepth(binary.Left), ExpressionDepth(binary.Right)),
            FunctionExpression function => 1 + (function.Arguments.Count == 0 ? 0 : function.Arguments.Max(ExpressionDepth)),
            InExpression membership => 1 + Math.Max(ExpressionDepth(membership.Operand), membership.Values.Count == 0 ? 0 : membership.Values.Max(ExpressionDepth)),
            EmptyExpression empty => 1 + ExpressionDepth(empty.Operand),
            AggregateExpression aggregate => 1 + Math.Max(aggregate.Argument is null ? 0 : ExpressionDepth(aggregate.Argument), aggregate.Filter is null ? 0 : ExpressionDepth(aggregate.Filter)),
            _ => 1,
        };

        private static bool SameExpression(QueryExpression left, QueryExpression right)
        {
            if (left.GetType() != right.GetType()) { return false; }
            return (left, right) switch
            {
                (ColumnExpression a, ColumnExpression b) => string.Equals(a.Name, b.Name, StringComparison.OrdinalIgnoreCase),
                (LiteralExpression a, LiteralExpression b) => a.Value.Equals(b.Value),
                (UnaryExpression a, UnaryExpression b) => string.Equals(a.Operator, b.Operator, StringComparison.OrdinalIgnoreCase)
                    && SameExpression(a.Operand, b.Operand),
                (BinaryExpression a, BinaryExpression b) => string.Equals(a.Operator, b.Operator, StringComparison.OrdinalIgnoreCase)
                    && SameExpression(a.Left, b.Left) && SameExpression(a.Right, b.Right),
                (FunctionExpression a, FunctionExpression b) => string.Equals(a.Name, b.Name, StringComparison.OrdinalIgnoreCase)
                    && a.Arguments.Count == b.Arguments.Count
                    && a.Arguments.Zip(b.Arguments).All(pair => SameExpression(pair.First, pair.Second)),
                (AggregateExpression a, AggregateExpression b) => a.Kind == b.Kind
                    && ((a.Argument is null && b.Argument is null) || (a.Argument is not null && b.Argument is not null && SameExpression(a.Argument, b.Argument)))
                    && ((a.Filter is null && b.Filter is null) || (a.Filter is not null && b.Filter is not null && SameExpression(a.Filter, b.Filter))),
                (InExpression a, InExpression b) => a.Negated == b.Negated
                    && SameExpression(a.Operand, b.Operand)
                    && a.Values.Count == b.Values.Count
                    && a.Values.Zip(b.Values).All(pair => SameExpression(pair.First, pair.Second)),
                (EmptyExpression a, EmptyExpression b) => a.Negated == b.Negated && SameExpression(a.Operand, b.Operand),
                _ => false,
            };
        }

        private void ExpectKeyword(string keyword)
        {
            if (!TryConsumeKeyword(keyword))
            {
                throw Error($"Expected {keyword}.");
            }
        }

        private bool TryConsumeKeyword(string keyword)
        {
            if (!_tokens.CurrentIsKeyword(keyword))
            {
                return false;
            }

            _tokens.MoveNext();
            return true;
        }

        private void Expect(TokenKind kind, string message)
        {
            if (!TryConsume(kind))
            {
                throw Error(message);
            }
        }

        private bool TryConsume(TokenKind kind)
        {
            if (!_tokens.Current.Is(kind))
            {
                return false;
            }

            _tokens.MoveNext();
            return true;
        }

        private QueryDslException Error(string message) => new(message, _tokens.Current.Position);
    }

    private sealed class TokenReader
    {
        private readonly string _text;
        private int _position;
        private int _tokenCount;
        private int _depth;
        private Token? _peeked;

        public TokenReader(string text)
        {
            _text = text;
            Current = ReadNext();
        }

        public Token Current { get; private set; }
        public Token Peek() => _peeked ??= ReadNext();
        public string CurrentDisplay => Current.IsEnd ? "<end>" : GetText(Current);

        public void MoveNext()
        {
            Current = _peeked ?? ReadNext();
            _peeked = null;
        }

        public void EnterDepth()
        {
            if (++_depth > MaxExpressionDepth)
            {
                throw new QueryDslException($"Expression nesting exceeds the maximum depth of {MaxExpressionDepth}.", Current.Position);
            }
        }

        public void LeaveDepth() => _depth--;

        public ReadOnlySpan<char> GetSpan(Token token)
            => token.HasOverride ? token.TextOverride.AsSpan() : _text.AsSpan(token.Offset, token.Length);

        public string GetText(Token token)
            => token.HasOverride ? token.TextOverride : _text[token.Offset..(token.Offset + token.Length)];

        public bool IsKeyword(Token token, string keyword)
            => token.Kind is TokenKind.Identifier && GetSpan(token).Equals(keyword.AsSpan(), StringComparison.OrdinalIgnoreCase);

        public bool CurrentIsKeyword(string keyword) => IsKeyword(Current, keyword);

        private Token ReadNext()
        {
            if (++_tokenCount > MaxTokens)
            {
                throw new QueryDslException($"Query exceeds the maximum token count of {MaxTokens}.", _position);
            }

            SkipWhitespace();
            if (_position >= _text.Length)
            {
                return new Token(TokenKind.End, _position, 0, _position);
            }

            int start = _position;
            char c = _text[_position];
            if (IsIdentifierStart(c))
            {
                return ReadIdentifier();
            }

            if (char.IsDigit(c) || IsSignedNumberStart(c))
            {
                return ReadNumber();
            }

            _position++;
            return c switch
            {
                '!' when TryConsume('=') => new(TokenKind.NotEquals, start, 2, start),
                '!' => new(TokenKind.Bang, start, 1, start),
                ':' => new(TokenKind.Colon, start, 1, start),
                ',' => new(TokenKind.Comma, start, 1, start),
                '(' => new(TokenKind.OpenParen, start, 1, start),
                ')' => new(TokenKind.CloseParen, start, 1, start),
                '*' => new(TokenKind.Star, start, 1, start),
                '/' => new(TokenKind.Slash, start, 1, start),
                '%' => new(TokenKind.Percent, start, 1, start),
                '+' => new(TokenKind.Plus, start, 1, start),
                '-' => new(TokenKind.Minus, start, 1, start),
                '=' => new(TokenKind.Equals, start, 1, start),
                '<' when TryConsume('=') => new(TokenKind.LessThanOrEqual, start, 2, start),
                '<' when TryConsume('>') => new(TokenKind.NotEquals, start, 2, start),
                '<' => new(TokenKind.LessThan, start, 1, start),
                '>' when TryConsume('=') => new(TokenKind.GreaterThanOrEqual, start, 2, start),
                '>' => new(TokenKind.GreaterThan, start, 1, start),
                '"' => ReadQuotedText(start),
                '\'' => ReadSingleQuotedText(start),
                '[' => ReadBracketedIdentifier(start),
                _ => throw new QueryDslException($"Unexpected character '{c}'.", start),
            };
        }

        private Token ReadIdentifier()
        {
            int start = _position++;
            while (_position < _text.Length && IsIdentifierPart(_text[_position]))
            {
                _position++;
            }

            return new Token(TokenKind.Identifier, start, _position - start, start);
        }

        private Token ReadNumber()
        {
            int start = _position;
            if (_text[_position] is '+' or '-')
            {
                _position++;
            }

            while (_position < _text.Length && char.IsDigit(_text[_position]))
            {
                _position++;
            }

            bool isDecimal = false;
            if (_position < _text.Length && _text[_position] == '.')
            {
                isDecimal = true;
                _position++;
                while (_position < _text.Length && char.IsDigit(_text[_position]))
                {
                    _position++;
                }
            }

            bool exponent = IsExponentStart();
            if (_position < _text.Length && IsIdentifierStart(_text[_position]) && !exponent)
            {
                while (_position < _text.Length && IsIdentifierPart(_text[_position])) { _position++; }
                return new Token(TokenKind.Identifier, start, _position - start, start);
            }

            if (exponent)
            {
                isDecimal = true;
                _position++;
                if (_position < _text.Length && _text[_position] is '+' or '-')
                {
                    _position++;
                }

                while (_position < _text.Length && char.IsDigit(_text[_position]))
                {
                    _position++;
                }
            }

            return new Token(isDecimal ? TokenKind.Number : TokenKind.Integer, start, _position - start, start);
        }

        private bool IsExponentStart()
        {
            if (_position >= _text.Length || _text[_position] is not ('e' or 'E'))
            {
                return false;
            }

            int digitPosition = _position + 1;
            if (digitPosition < _text.Length && _text[digitPosition] is '+' or '-')
            {
                digitPosition++;
            }

            return digitPosition < _text.Length && char.IsDigit(_text[digitPosition]);
        }

        private bool IsSignedNumberStart(char c)
        {
            if (c is not ('+' or '-') || _position + 1 >= _text.Length || !char.IsDigit(_text[_position + 1]))
            {
                return false;
            }

            int previous = _position - 1;
            while (previous >= 0 && char.IsWhiteSpace(_text[previous]))
            {
                previous--;
            }

            return previous < 0 || _text[previous] is '(' or ',' or '=' or '<' or '>' or '!' or ':';
        }

        private Token ReadQuotedText(int start)
        {
            int contentStart = _position;
            var value = new StringBuilder();
            while (_position < _text.Length)
            {
                char c = _text[_position++];
                if (c != '"')
                {
                    value.Append(c);
                    continue;
                }

                if (_position < _text.Length && _text[_position] == '"')
                {
                    value.Append('"');
                    _position++;
                    continue;
                }

                return value.Length == _position - contentStart - 1
                    ? new Token(TokenKind.QuotedText, contentStart, value.Length, start)
                    : Token.Escaped(TokenKind.QuotedText, value.ToString(), start);
            }

            throw new QueryDslException("Unterminated quoted text. Escape double quotes by doubling them.", start);
        }

        private Token ReadSingleQuotedText(int start)
        {
            int contentStart = _position;
            var value = new StringBuilder();
            while (_position < _text.Length)
            {
                char c = _text[_position++];
                if (c != '\'')
                {
                    value.Append(c);
                    continue;
                }

                if (_position < _text.Length && _text[_position] == '\'')
                {
                    value.Append('\'');
                    _position++;
                    continue;
                }

                return value.Length == _position - contentStart - 1
                    ? new Token(TokenKind.StringLiteral, contentStart, value.Length, start)
                    : Token.Escaped(TokenKind.StringLiteral, value.ToString(), start);
            }

            throw new QueryDslException("Unterminated string literal. Escape single quotes by doubling them.", start);
        }

        private Token ReadBracketedIdentifier(int start)
        {
            int contentStart = _position;
            int end = _text.IndexOf(']', _position);
            if (end < 0)
            {
                throw new QueryDslException("Unterminated bracketed identifier.", start);
            }

            _position = end + 1;
            if (end == contentStart)
            {
                throw new QueryDslException("Bracketed identifiers cannot be empty.", start);
            }

            return new Token(TokenKind.BracketedIdentifier, contentStart, end - contentStart, start);
        }

        private void SkipWhitespace()
        {
            while (_position < _text.Length && char.IsWhiteSpace(_text[_position]))
            {
                _position++;
            }
        }

        private bool TryConsume(char expected)
        {
            if (_position >= _text.Length || _text[_position] != expected)
            {
                return false;
            }

            _position++;
            return true;
        }
    }

    private readonly record struct Token(TokenKind Kind, int Offset, int Length, int Position)
    {
        private readonly string? _textOverride;

        private Token(TokenKind kind, string text, int position)
            : this(kind, 0, 0, position) => _textOverride = text;

        public static Token Escaped(TokenKind kind, string text, int position) => new(kind, text, position);
        public bool HasOverride => _textOverride is not null;
        public string TextOverride => _textOverride!;
        public bool IsEnd => Kind is TokenKind.End;
        public bool IsIdentifier => Kind is TokenKind.Identifier;
        public bool Is(TokenKind kind) => Kind == kind;
    }

    private enum TokenKind
    {
        End, Identifier, QuotedText, StringLiteral, BracketedIdentifier, Integer, Number,
        Bang, Colon, Comma, OpenParen, CloseParen, Star, Slash, Percent, Plus, Minus,
        Equals, NotEquals, LessThan, LessThanOrEqual, GreaterThan, GreaterThanOrEqual,
    }

    private static bool IsIdentifierStart(char c) => c == '_' || char.IsLetter(c);
    private static bool IsIdentifierPart(char c) => IsIdentifierStart(c) || char.IsDigit(c) || c == '.';
}
