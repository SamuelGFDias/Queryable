using System.Text;

namespace Queryable.Filtering;

/// <summary>
/// Tipo de token produzido por <see cref="FilterExpressionTokenizer"/> para a mini-linguagem de
/// filtros compostos.
/// </summary>
internal enum FilterTokenType
{
    LParen,
    RParen,
    Comma,
    Equals,
    Keyword,
    Word,
    QuotedString,
    Eof
}

/// <summary>
/// Um token da mini-linguagem. <see cref="Position"/> é 1-based, usado nas mensagens de erro do
/// parser. Para <see cref="FilterTokenType.Keyword"/> e <see cref="FilterTokenType.QuotedString"/>,
/// <see cref="Text"/> já vem normalizado (palavra-chave em minúsculas; string entre aspas já
/// resolvida, sem as aspas delimitadoras e com os escapes <c>\"</c>/<c>\\</c> já aplicados).
/// </summary>
internal readonly record struct FilterToken(FilterTokenType Type, string Text, int Position);

/// <summary>
/// Tokenizador da mini-linguagem textual de filtros compostos (seção 4 de
/// <c>docs/proposta-filtros-compostos.md</c>). Não conhece gramática — só quebra a string em
/// tokens, resolvendo aspas/escape e reconhecendo <c>and</c>/<c>or</c>/<c>not</c> como palavras-
/// chave sempre que aparecem como token isolado fora de aspas (nunca como substring de um token
/// não citado maior, e nunca dentro de aspas).
/// </summary>
internal static class FilterExpressionTokenizer
{
    private static readonly HashSet<string> Keywords = new(StringComparer.OrdinalIgnoreCase) { "and", "or", "not" };

    public static List<FilterToken> Tokenize(string input)
    {
        var tokens = new List<FilterToken>();
        int i = 0;
        int length = input.Length;

        while (i < length)
        {
            char c = input[i];

            if (char.IsWhiteSpace(c))
            {
                i++;
                continue;
            }

            int tokenStart = i + 1; // posição 1-based

            switch (c)
            {
                case '(':
                    tokens.Add(new FilterToken(FilterTokenType.LParen, "(", tokenStart));
                    i++;
                    break;
                case ')':
                    tokens.Add(new FilterToken(FilterTokenType.RParen, ")", tokenStart));
                    i++;
                    break;
                case ',':
                    tokens.Add(new FilterToken(FilterTokenType.Comma, ",", tokenStart));
                    i++;
                    break;
                case '=':
                    tokens.Add(new FilterToken(FilterTokenType.Equals, "=", tokenStart));
                    i++;
                    break;
                case '"':
                    i = ReadQuoted(input, i, tokenStart, tokens);
                    break;
                default:
                    i = ReadBareWord(input, i, tokenStart, tokens);
                    break;
            }
        }

        tokens.Add(new FilterToken(FilterTokenType.Eof, string.Empty, length + 1));
        return tokens;
    }

    private static int ReadQuoted(string input, int i, int tokenStart, List<FilterToken> tokens)
    {
        int length = input.Length;
        i++; // pula a aspa de abertura
        var sb = new StringBuilder();

        while (true)
        {
            if (i >= length)
                throw new FilterExpressionSyntaxException(
                    "Aspas não fechadas: a expressão terminou antes de encontrar a aspa de fechamento",
                    tokenStart);

            char c = input[i];

            if (c == '"')
            {
                i++;
                break;
            }

            if (c == '\\')
            {
                if (i + 1 >= length)
                    throw new FilterExpressionSyntaxException(
                        "Aspas não fechadas: sequência de escape incompleta no final da expressão",
                        tokenStart);

                char next = input[i + 1];
                if (next is '"' or '\\')
                {
                    sb.Append(next);
                    i += 2;
                    continue;
                }

                throw new FilterExpressionSyntaxException(
                    $"Sequência de escape inválida '\\{next}': dentro de aspas só \\\" e \\\\ são suportados",
                    i + 1);
            }

            sb.Append(c);
            i++;
        }

        tokens.Add(new FilterToken(FilterTokenType.QuotedString, sb.ToString(), tokenStart));
        return i;
    }

    private static int ReadBareWord(string input, int i, int tokenStart, List<FilterToken> tokens)
    {
        int length = input.Length;
        int start = i;

        while (i < length && !IsTerminator(input[i]))
            i++;

        string text = input[start..i];

        FilterTokenType type = Keywords.Contains(text) ? FilterTokenType.Keyword : FilterTokenType.Word;
        string normalized = type == FilterTokenType.Keyword ? text.ToLowerInvariant() : text;

        tokens.Add(new FilterToken(type, normalized, tokenStart));
        return i;
    }

    private static bool IsTerminator(char c) =>
        char.IsWhiteSpace(c) || c is '(' or ')' or ',' or '=' or '"';
}
