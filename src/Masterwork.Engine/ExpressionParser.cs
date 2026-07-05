using System.Text;

namespace Masterwork.Engine;

/// <summary>
/// Recursive-descent parser for the MWS expression language. Whitelist grammar only — no
/// arbitrary code execution. See <c>docs/mws-format-latest.md</c> §4 for the full operator/function
/// reference.
/// </summary>
public static class ExpressionParser
{
    /// <summary>Parses an MWS expression string into an <see cref="Expr"/> AST.</summary>
    /// <exception cref="ExprParseException">The expression text is syntactically invalid.</exception>
    public static Expr Parse(string source)
    {
        var tokens = Lexer.Tokenize(source);
        var parser = new Parser(tokens);
        var expr = parser.ParseOr();
        parser.ExpectEof();
        return expr;
    }

    private enum TokenKind { Int, String, Ident, Punct, Eof }

    private readonly record struct Token(TokenKind Kind, string Text, long IntValue = 0);

    private static class Lexer
    {
        public static List<Token> Tokenize(string s)
        {
            var tokens = new List<Token>();
            int i = 0;
            while (i < s.Length)
            {
                char c = s[i];
                if (char.IsWhiteSpace(c)) { i++; continue; }

                if (char.IsDigit(c))
                {
                    int start = i;
                    while (i < s.Length && char.IsDigit(s[i]))
                    {
                        i++;
                    }

                    var text = s[start..i];
                    tokens.Add(new Token(TokenKind.Int, text, long.Parse(text)));
                    continue;
                }

                if (c == '"')
                {
                    i++;
                    var sb = new StringBuilder();
                    while (i < s.Length && s[i] != '"')
                    {
                        if (s[i] == '\\' && i + 1 < s.Length)
                        {
                            sb.Append(s[i + 1]);
                            i += 2;
                        }
                        else
                        {
                            sb.Append(s[i]);
                            i++;
                        }
                    }
                    if (i >= s.Length)
                    {
                        throw new ExprParseException("Unterminated string literal");
                    }

                    i++; // closing quote
                    tokens.Add(new Token(TokenKind.String, sb.ToString()));
                    continue;
                }

                if (char.IsLetter(c) || c == '_')
                {
                    int start = i;
                    while (i < s.Length && (char.IsLetterOrDigit(s[i]) || s[i] == '_'))
                    {
                        i++;
                    }

                    tokens.Add(new Token(TokenKind.Ident, s[start..i]));
                    continue;
                }

                // Two-character operators.
                if (i + 1 < s.Length)
                {
                    var two = s.Substring(i, 2);
                    if (two is "==" or "!=" or "<=" or ">=" or "&&" or "||" or "..")
                    {
                        tokens.Add(new Token(TokenKind.Punct, two));
                        i += 2;
                        continue;
                    }
                }

                if ("(){}[].,:!<>+-*/%^".IndexOf(c) >= 0)
                {
                    tokens.Add(new Token(TokenKind.Punct, c.ToString()));
                    i++;
                    continue;
                }

                throw new ExprParseException($"Unexpected character '{c}' at position {i}");
            }
            tokens.Add(new Token(TokenKind.Eof, ""));
            return tokens;
        }
    }

    private sealed class Parser(List<Token> tokens)
    {
        private int _pos;

        private Token Current => tokens[_pos];
        private bool IsPunct(string text) => Current.Kind == TokenKind.Punct && Current.Text == text;
        private bool IsIdent(string text) => Current.Kind == TokenKind.Ident && Current.Text == text;

        private Token Advance() => tokens[_pos++];

        private void Expect(string punct)
        {
            if (!IsPunct(punct))
            {
                throw new ExprParseException($"Expected '{punct}' but found '{Current.Text}'");
            }

            Advance();
        }

        public void ExpectEof()
        {
            if (Current.Kind != TokenKind.Eof)
            {
                throw new ExprParseException($"Unexpected trailing input: '{Current.Text}'");
            }
        }

        public Expr ParseOr()
        {
            var left = ParseAnd();
            while (IsPunct("||"))
            {
                Advance();
                left = new Expr.Binary("||", left, ParseAnd());
            }
            return left;
        }

        private Expr ParseAnd()
        {
            var left = ParseEquality();
            while (IsPunct("&&"))
            {
                Advance();
                left = new Expr.Binary("&&", left, ParseEquality());
            }
            return left;
        }

        private Expr ParseEquality()
        {
            var left = ParseComparison();
            while (IsPunct("==") || IsPunct("!="))
            {
                var op = Advance().Text;
                left = new Expr.Binary(op, left, ParseComparison());
            }
            return left;
        }

        private Expr ParseComparison()
        {
            var left = ParseAdditive();
            while (IsPunct("<") || IsPunct("<=") || IsPunct(">") || IsPunct(">="))
            {
                var op = Advance().Text;
                left = new Expr.Binary(op, left, ParseAdditive());
            }
            return left;
        }

        private Expr ParseAdditive()
        {
            var left = ParseMultiplicative();
            while (IsPunct("+") || IsPunct("-"))
            {
                var op = Advance().Text;
                left = new Expr.Binary(op, left, ParseMultiplicative());
            }
            return left;
        }

        private Expr ParseMultiplicative()
        {
            var left = ParseUnary();
            while (IsPunct("*") || IsPunct("/") || IsPunct("%"))
            {
                var op = Advance().Text;
                left = new Expr.Binary(op, left, ParseUnary());
            }
            return left;
        }

        private Expr ParseUnary()
        {
            if (IsPunct("!") || IsPunct("-"))
            {
                var op = Advance().Text;
                return new Expr.Unary(op, ParseUnary());
            }
            return ParsePostfix();
        }

        private Expr ParsePostfix()
        {
            var expr = ParsePrimary();
            while (true)
            {
                if (IsPunct("."))
                {
                    Advance();
                    var name = ExpectIdent();
                    if (IsPunct("("))
                    {
                        var args = ParseArgList();
                        expr = new Expr.MethodCall(expr, name, args);
                    }
                    else
                    {
                        expr = new Expr.PropertyAccess(expr, name);
                    }
                }
                else if (IsPunct("["))
                {
                    Advance();
                    var arg = ParseIndexArg();
                    Expect("]");
                    expr = new Expr.IndexAccess(expr, arg);
                }
                else
                {
                    break;
                }
            }
            return expr;
        }

        private IndexArg ParseIndexArg()
        {
            if (IsPunct(".."))
            {
                Advance();
                var (end, endFromEnd) = ParseOptionalBound(closer: "]");
                return new IndexArg.Range(null, false, end, endFromEnd);
            }

            var (first, firstFromEnd) = ParseBound();

            if (IsPunct(".."))
            {
                Advance();
                var (end, endFromEnd) = ParseOptionalBound(closer: "]");
                return new IndexArg.Range(first, firstFromEnd, end, endFromEnd);
            }

            return new IndexArg.Single(first, firstFromEnd);
        }

        private (Expr? expr, bool fromEnd) ParseOptionalBound(string closer)
        {
            if (IsPunct(closer))
            {
                return (null, false);
            }

            return ParseBound();
        }

        private (Expr expr, bool fromEnd) ParseBound()
        {
            if (IsPunct("^"))
            {
                Advance();
                return (ParseAdditive(), true);
            }
            return (ParseAdditive(), false);
        }

        private List<Expr> ParseArgList()
        {
            Expect("(");
            var args = new List<Expr>();
            if (!IsPunct(")"))
            {
                args.Add(ParseOr());
                while (IsPunct(","))
                {
                    Advance();
                    args.Add(ParseOr());
                }
            }
            Expect(")");
            return args;
        }

        private string ExpectIdent()
        {
            if (Current.Kind != TokenKind.Ident)
            {
                throw new ExprParseException($"Expected identifier but found '{Current.Text}'");
            }

            return Advance().Text;
        }

        private Expr ParsePrimary()
        {
            var tok = Current;
            switch (tok.Kind)
            {
                case TokenKind.Int:
                    Advance();
                    return new Expr.IntLiteral(tok.IntValue);
                case TokenKind.String:
                    Advance();
                    return new Expr.StringLiteral(tok.Text);
                case TokenKind.Ident:
                    if (tok.Text == "true") { Advance(); return new Expr.BoolLiteral(true); }
                    if (tok.Text == "false") { Advance(); return new Expr.BoolLiteral(false); }
                    Advance();
                    if (IsPunct("("))
                    {
                        return new Expr.FunctionCall(tok.Text, ParseArgList());
                    }

                    return new Expr.VarRef(tok.Text);
                case TokenKind.Punct when tok.Text == "(":
                    Advance();
                    var inner = ParseOr();
                    Expect(")");
                    return inner;
                case TokenKind.Punct when tok.Text == "[":
                    return ParseArrayLiteral();
                case TokenKind.Punct when tok.Text == "{":
                    return ParseRecordLiteral();
                default:
                    throw new ExprParseException($"Unexpected token '{tok.Text}'");
            }
        }

        private Expr ParseArrayLiteral()
        {
            Expect("[");
            var elements = new List<ArrayElement>();
            if (!IsPunct("]"))
            {
                elements.Add(ParseArrayElement());
                while (IsPunct(","))
                {
                    Advance();
                    elements.Add(ParseArrayElement());
                }
            }
            Expect("]");
            return new Expr.ArrayLiteral(elements);
        }

        private ArrayElement ParseArrayElement()
        {
            if (IsPunct(".."))
            {
                Advance();
                return new ArrayElement.Spread(ParseOr());
            }
            return new ArrayElement.Item(ParseOr());
        }

        private Expr ParseRecordLiteral()
        {
            Expect("{");
            var props = new Dictionary<string, Expr>(StringComparer.Ordinal);
            if (!IsPunct("}"))
            {
                ParseRecordProperty(props);
                while (IsPunct(","))
                {
                    Advance();
                    ParseRecordProperty(props);
                }
            }
            Expect("}");
            return new Expr.RecordLiteral(props);
        }

        private void ParseRecordProperty(Dictionary<string, Expr> props)
        {
            var name = ExpectIdent();
            Expect(":");
            props[name] = ParseOr();
        }
    }
}
