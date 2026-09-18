using System.Globalization;

namespace RagExample.Api.Services;

// A small recursive-descent arithmetic evaluator supporting + - * / % ^ and parentheses.
//
// Note what this deliberately does NOT do: there is no eval(), no scripting engine, no
// reflection. Any tool you expose is something a model can be talked into calling with
// hostile arguments, so a "calculator" that can execute arbitrary expressions is a remote
// code execution hole. This one can only ever do arithmetic.
public static class Calculator
{
    public static double Evaluate(string expression)
    {
        var parser = new Parser(expression);
        var value = parser.ParseExpression();
        parser.ExpectEnd();
        return value;
    }

    private sealed class Parser(string text)
    {
        private int _pos;

        // expression := term (('+' | '-') term)*
        public double ParseExpression()
        {
            var value = ParseTerm();
            while (true)
            {
                if (Match('+')) value += ParseTerm();
                else if (Match('-')) value -= ParseTerm();
                else return value;
            }
        }

        // term := factor (('*' | '/' | '%') factor)*
        private double ParseTerm()
        {
            var value = ParseFactor();
            while (true)
            {
                if (Match('*')) value *= ParseFactor();
                else if (Match('/')) value /= ParseFactor();
                else if (Match('%')) value %= ParseFactor();
                else return value;
            }
        }

        // factor := ('-' | '+') factor | primary ('^' factor)?
        private double ParseFactor()
        {
            if (Match('-')) return -ParseFactor();
            if (Match('+')) return ParseFactor();

            var value = ParsePrimary();
            if (Match('^')) value = Math.Pow(value, ParseFactor());
            return value;
        }

        // primary := '(' expression ')' | number
        private double ParsePrimary()
        {
            if (Match('('))
            {
                var value = ParseExpression();
                if (!Match(')')) throw new FormatException("Expected ')'.");
                return value;
            }

            SkipWhitespace();
            var start = _pos;
            while (_pos < text.Length && (char.IsAsciiDigit(text[_pos]) || text[_pos] == '.')) _pos++;

            if (start == _pos)
                throw new FormatException($"Expected a number at position {_pos}.");

            return double.Parse(text[start.._pos], CultureInfo.InvariantCulture);
        }

        public void ExpectEnd()
        {
            SkipWhitespace();
            if (_pos < text.Length)
                throw new FormatException($"Unexpected '{text[_pos]}' at position {_pos}.");
        }

        private bool Match(char c)
        {
            SkipWhitespace();
            if (_pos >= text.Length || text[_pos] != c) return false;
            _pos++;
            return true;
        }

        private void SkipWhitespace()
        {
            while (_pos < text.Length && char.IsWhiteSpace(text[_pos])) _pos++;
        }
    }
}
