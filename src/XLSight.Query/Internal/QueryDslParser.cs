using System.Globalization;
using System.Text;

namespace XLSight.Query.Internal;

internal static class QueryDslParser
{
    private const string SupportedAggregates = "COUNT, SUM, AVG, MIN, MAX";

    public static SheetQuerySpec Parse(string queryText)
    {
        ArgumentNullException.ThrowIfNull(queryText);

        var parser = new Parser(queryText);
        return parser.Parse();
    }

    private sealed class Parser
    {
        private readonly TokenReader _tokens;

        public Parser(string queryText)
        {
            _tokens = new TokenReader(queryText);
        }

        public SheetQuerySpec Parse()
        {
            ExpectKeyword("FROM");
            string sheet = ParseIdentifierLike("sheet name");
            Expect(TokenKind.Bang, "Expected '!' between sheet name and range.");
            (string rangeAddress, ExcelRange range) = ParseBoundedRange();

            SheetQueryHeader header = ParseHeader();
            (bool selectAll, IReadOnlyList<AggregateSpec> aggregates) = ParseSelect();
            List<SheetQueryPredicate> predicates = TryConsumeKeyword("WHERE")
                ? ParseWhere()
                : [];

            string? groupBy = null;
            if (_tokens.Current.IsKeyword("GROUP") && _tokens.Peek().IsKeyword("BY"))
            {
                _tokens.MoveNext(); // consume GROUP
                _tokens.MoveNext(); // consume BY
                groupBy = ParseIdentifierLike("GROUP BY column");
            }

            int? limit = null;
            if (TryConsumeKeyword("LIMIT"))
            {
                limit = ParsePositiveInteger("LIMIT");
            }

            if (!_tokens.Current.IsEnd)
            {
                throw Error($"Unexpected token '{_tokens.Current.Display}'. Clause order is FROM, HEADER, SELECT, WHERE, GROUP BY, LIMIT.");
            }

            if (groupBy is not null && selectAll)
            {
                throw Error("GROUP BY is not valid with SELECT *. Select aggregate functions instead.");
            }

            return new SheetQuerySpec(
                sheet,
                rangeAddress,
                range,
                header,
                selectAll,
                aggregates,
                predicates,
                groupBy,
                limit);
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
                string column = ParseBareToken("HEADER COLUMN");
                if (!ExcelAddress.TryParse($"{column}1", out _))
                {
                    throw Error($"Invalid HEADER COLUMN '{column}'. Expected an Excel column such as A or BC.");
                }

                return SheetQueryHeader.FromColumn(column.ToUpperInvariant());
            }

            throw Error("Expected HEADER ROW <number>, HEADER AUTO, or HEADER COLUMN <column> after range.");
        }

        private (bool SelectAll, IReadOnlyList<AggregateSpec> Aggregates) ParseSelect()
        {
            ExpectKeyword("SELECT");

            if (TryConsume(TokenKind.Star))
            {
                return (true, []);
            }

            var aggregates = new List<AggregateSpec>();
            while (true)
            {
                aggregates.Add(ParseAggregate());

                if (!TryConsume(TokenKind.Comma))
                {
                    break;
                }
            }

            return (false, aggregates);
        }

        private AggregateSpec ParseAggregate()
        {
            string function = ParseBareToken("aggregate function");
            if (_tokens.Current.Kind is not TokenKind.OpenParen)
            {
                throw Error("Projected row columns are not supported. Use SELECT * or aggregate functions.");
            }

            Expect(TokenKind.OpenParen, $"Expected '(' after aggregate '{function}'.");

            if (KeywordEquals(function, "COUNT"))
            {
                if (!_tokens.Current.Is(TokenKind.CloseParen))
                {
                    throw Error("COUNT() does not accept a column.");
                }

                Expect(TokenKind.CloseParen, "Expected ')' after COUNT().");
                return QueryAggregates.Count();
            }

            AggregateKind kind = function.ToUpperInvariant() switch
            {
                "SUM" => AggregateKind.Sum,
                "AVG" => AggregateKind.Average,
                "MIN" => AggregateKind.Min,
                "MAX" => AggregateKind.Max,
                _ => throw Error($"Unknown aggregate '{function}'. Supported aggregates: {SupportedAggregates}."),
            };

            if (_tokens.Current.Is(TokenKind.CloseParen))
            {
                throw Error($"{function.ToUpperInvariant()} requires a column.");
            }

            string column = ParseIdentifierLike($"{function} column");
            Expect(TokenKind.CloseParen, $"Expected ')' after aggregate '{function}'.");

            return kind switch
            {
                AggregateKind.Sum => QueryAggregates.Sum(column),
                AggregateKind.Average => QueryAggregates.Average(column),
                AggregateKind.Min => QueryAggregates.Min(column),
                AggregateKind.Max => QueryAggregates.Max(column),
                _ => throw Error($"Unknown aggregate '{function}'. Supported aggregates: {SupportedAggregates}."),
            };
        }

        private List<SheetQueryPredicate> ParseWhere()
        {
            var predicates = new List<SheetQueryPredicate>();

            while (true)
            {
                predicates.Add(ParsePredicate());

                if (TryConsumeKeyword("AND"))
                {
                    continue;
                }

                if (_tokens.Current.IsKeyword("OR"))
                {
                    throw Error("OR is not supported. Predicates must be combined with AND.");
                }

                break;
            }

            return predicates;
        }

        private SheetQueryPredicate ParsePredicate()
        {
            string column = ParseIdentifierLike("predicate column");
            QueryOperator op = ParseOperator();
            ExcelCellValue literal = ParseLiteral();

            if (literal.CellType == CellType.Boolean && op is not (QueryOperator.Equals or QueryOperator.NotEquals))
            {
                throw Error($"Boolean predicates support '=' and '!=' only. Column '{column}' used operator '{OperatorText(op)}'.");
            }

            return new SheetQueryPredicate(column, op, literal);
        }

        private ExcelCellValue ParseLiteral()
        {
            Token token = _tokens.Current;
            if (token.Kind is TokenKind.QuotedText)
            {
                _tokens.MoveNext();
                return ExcelCellValue.FromText(token.Text);
            }

            if (token.Kind is TokenKind.Integer or TokenKind.Number)
            {
                _tokens.MoveNext();
                if (!double.TryParse(token.Text, NumberStyles.AllowDecimalPoint | NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out double value))
                {
                    throw Error($"Invalid numeric literal '{token.Text}'. Numbers must use invariant culture without thousands separators.");
                }

                return ExcelCellValue.FromNumber(value);
            }

            if (TryConsumeKeyword("DATE"))
            {
                Token date = _tokens.Current;
                if (date.Kind is not TokenKind.QuotedText)
                {
                    throw Error("Expected DATE \"yyyy-MM-dd\".");
                }

                _tokens.MoveNext();
                if (!DateTime.TryParseExact(date.Text, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out DateTime value))
                {
                    throw Error($"Invalid DATE literal '{date.Text}'. Dates must use ISO format yyyy-MM-dd.");
                }

                return ExcelCellValue.FromDate(value);
            }

            if (token.IsIdentifier)
            {
                if (token.IsKeyword("TRUE"))
                {
                    _tokens.MoveNext();
                    return ExcelCellValue.FromBoolean(true);
                }

                if (token.IsKeyword("FALSE"))
                {
                    _tokens.MoveNext();
                    return ExcelCellValue.FromBoolean(false);
                }
            }

            throw Error("Expected a literal: text, number, DATE \"yyyy-MM-dd\", true, or false.");
        }

        private QueryOperator ParseOperator()
        {
            Token token = _tokens.Current;
            _tokens.MoveNext();
            return token.Kind switch
            {
                TokenKind.Equals => QueryOperator.Equals,
                TokenKind.NotEquals => QueryOperator.NotEquals,
                TokenKind.LessThan => QueryOperator.LessThan,
                TokenKind.LessThanOrEqual => QueryOperator.LessThanOrEqual,
                TokenKind.GreaterThan => QueryOperator.GreaterThan,
                TokenKind.GreaterThanOrEqual => QueryOperator.GreaterThanOrEqual,
                _ => throw Error("Expected predicate operator: =, !=, <, <=, >, or >=."),
            };
        }

        private (string RangeAddress, ExcelRange Range) ParseBoundedRange()
        {
            string start = ParseBareToken("range start");
            Expect(TokenKind.Colon, "FROM range must be a bounded A1 range such as A1:F100.");
            string end = ParseBareToken("range end");
            string rangeAddress = $"{start}:{end}".ToUpperInvariant();

            if (!ExcelRange.TryParse(rangeAddress, out ExcelRange range))
            {
                throw Error("FROM range must be a bounded A1 range such as A1:F100.");
            }

            return (rangeAddress, range);
        }

        private int ParsePositiveInteger(string context)
        {
            Token token = _tokens.Current;
            if (token.Kind is not TokenKind.Integer ||
                !int.TryParse(token.Text, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out int value) ||
                value <= 0)
            {
                throw Error($"{context} must be a positive integer.");
            }

            _tokens.MoveNext();
            return value;
        }

        private string ParseIdentifierLike(string context)
        {
            Token token = _tokens.Current;
            if (token.Kind is not (TokenKind.Identifier or TokenKind.QuotedText or TokenKind.Integer))
            {
                throw Error($"Expected {context}.");
            }

            _tokens.MoveNext();
            return token.Text;
        }

        private string ParseBareToken(string context)
        {
            Token token = _tokens.Current;
            if (token.Kind is not TokenKind.Identifier)
            {
                throw Error($"Expected {context}.");
            }

            _tokens.MoveNext();
            return token.Text;
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
            if (!_tokens.Current.IsKeyword(keyword))
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

        public TokenReader(string text)
        {
            _text = text;
            Current = ReadNext();
        }

        public Token Current { get; private set; }

        public void MoveNext() => Current = ReadNext();

        private Token ReadNext()
        {
            SkipWhitespace();
            if (_position >= _text.Length)
            {
                return new Token(TokenKind.End, string.Empty);
            }

            char c = _text[_position];
            if (IsIdentifierStart(c))
            {
                return ReadIdentifier();
            }

            if (char.IsDigit(c) || ((c is '+' or '-') && _position + 1 < _text.Length && char.IsDigit(_text[_position + 1])))
            {
                return ReadNumberOrIdentifier();
            }

            if (c == '"')
            {
                return ReadQuotedText();
            }

            _position++;
            return c switch
            {
                '!' when TryConsume('=') => new Token(TokenKind.NotEquals, "!="),
                '!' => new Token(TokenKind.Bang, "!"),
                ':' => new Token(TokenKind.Colon, ":"),
                ',' => new Token(TokenKind.Comma, ","),
                '(' => new Token(TokenKind.OpenParen, "("),
                ')' => new Token(TokenKind.CloseParen, ")"),
                '*' => new Token(TokenKind.Star, "*"),
                '=' => new Token(TokenKind.Equals, "="),
                '<' when TryConsume('=') => new Token(TokenKind.LessThanOrEqual, "<="),
                '<' => new Token(TokenKind.LessThan, "<"),
                '>' when TryConsume('=') => new Token(TokenKind.GreaterThanOrEqual, ">="),
                '>' => new Token(TokenKind.GreaterThan, ">"),
                _ => throw new QueryDslException($"Unexpected character '{c}'."),
            };
        }

        private Token ReadIdentifier()
        {
            int start = _position;
            _position++;
            while (_position < _text.Length && IsIdentifierPart(_text[_position]))
            {
                _position++;
            }

            return new Token(TokenKind.Identifier, _text[start.._position]);
        }

        private Token ReadNumberOrIdentifier()
        {
            int start = _position;
            if (_position < _text.Length && _text[_position] is '+' or '-')
                _position++;
            while (_position < _text.Length && char.IsDigit(_text[_position]))
            {
                _position++;
            }

            if (_position < _text.Length && IsIdentifierStart(_text[_position]))
            {
                _position++;
                while (_position < _text.Length && IsIdentifierPart(_text[_position]))
                {
                    _position++;
                }

                return new Token(TokenKind.Identifier, _text[start.._position]);
            }

            if (_position < _text.Length && _text[_position] == '.')
            {
                _position++;
                while (_position < _text.Length && char.IsDigit(_text[_position]))
                {
                    _position++;
                }
            }

            return new Token(TokenKind.Number, _text[start.._position]);
        }

        private Token ReadQuotedText()
        {
            _position++;
            var value = new StringBuilder();
            while (_position < _text.Length)
            {
                char c = _text[_position++];
                if (c == '"')
                {
                    if (_position < _text.Length && _text[_position] == '"')
                    {
                        value.Append('"');
                        _position++;
                        continue;
                    }

                    return new Token(TokenKind.QuotedText, value.ToString());
                }

                value.Append(c);
            }

            throw new QueryDslException("Unterminated quoted text. Escape double quotes by doubling them.");
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

    private readonly record struct Token(TokenKind Kind, string Text)
    {
        public bool IsEnd => Kind is TokenKind.End;

        public bool IsIdentifier => Kind is TokenKind.Identifier;

        public string Display => IsEnd ? "<end>" : Text;

        public bool Is(TokenKind kind) => Kind == kind;

        public bool IsKeyword(string keyword) =>
            Kind is TokenKind.Identifier && KeywordEquals(Text, keyword);

        public bool IsIntegerNumber => Kind is TokenKind.Number && !Text.Contains('.', StringComparison.Ordinal);
    }

    private enum TokenKind
    {
        End,
        Identifier,
        QuotedText,
        Number,
        Bang,
        Colon,
        Comma,
        OpenParen,
        CloseParen,
        Star,
        Equals,
        NotEquals,
        LessThan,
        LessThanOrEqual,
        GreaterThan,
        GreaterThanOrEqual,
    }

    private static bool KeywordEquals(string value, string keyword) =>
        string.Equals(value, keyword, StringComparison.OrdinalIgnoreCase);

    private static string OperatorText(QueryOperator op) => op switch
    {
        QueryOperator.Equals => "=",
        QueryOperator.NotEquals => "!=",
        QueryOperator.LessThan => "<",
        QueryOperator.LessThanOrEqual => "<=",
        QueryOperator.GreaterThan => ">",
        QueryOperator.GreaterThanOrEqual => ">=",
        _ => op.ToString(),
    };

    private static bool IsIdentifierStart(char c) =>
        c == '_' || (c >= 'A' && c <= 'Z') || (c >= 'a' && c <= 'z');

    private static bool IsIdentifierPart(char c) =>
        IsIdentifierStart(c) || char.IsDigit(c);
}
