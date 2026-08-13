namespace Queryable.Filtering;

/// <summary>
/// Parser da mini-linguagem textual de filtros compostos, pensada para uso em GET/query string,
/// onde uma árvore JSON seria pouco natural (seção 4 de <c>docs/proposta-filtros-compostos.md</c>).
/// Produz a mesma árvore <see cref="FilterNode"/> que a porta JSON
/// (<see cref="FilterNodeJsonConverter"/>) já consome — o compilador <c>FilterNode → Expression</c>
/// de <see cref="Queryable.Builders.FilterBuilder"/> não distingue a origem da árvore.
/// </summary>
/// <remarks>
/// <para>Gramática:</para>
/// <code>
/// expr       := orExpr
/// orExpr     := andExpr ( "or" andExpr )*
/// andExpr    := unary ( "and" unary )*
/// unary      := "not"? primary
/// primary    := "(" expr ")" | comparison
/// comparison := field ( "__" operator )? "=" value
/// </code>
/// <para>
/// Precedência: <c>not</c> liga mais forte que <c>and</c>, que liga mais forte que <c>or</c>;
/// parênteses sobrepõem qualquer precedência. Operador ausente equivale a <c>eq</c>.
/// </para>
/// <para>
/// <b>Palavras-chave.</b> <c>and</c>/<c>or</c>/<c>not</c> são case-insensitive e só são
/// reconhecidas como palavra-chave quando aparecem como token isolado fora de aspas — nunca como
/// substring de um token maior não citado. Importante: um token não citado que seja
/// <i>exatamente</i> igual (ignorando maiúsculas/minúsculas) a uma dessas três palavras é
/// <b>sempre</b> tratado como palavra-chave, mesmo em posição de valor — para usá-la como valor
/// literal, é obrigatório citar entre aspas (ex.: <c>nome="and joão"</c>).
/// </para>
/// <para>
/// <b>Aspas e escape.</b> O valor de uma comparação precisa estar entre aspas duplas quando
/// contiver espaço, <c>(</c>, <c>)</c>, <c>,</c>, <c>=</c>, ou colidir com uma palavra-chave como
/// token isolado. Dentro de aspas, <c>\"</c> é aspa literal e <c>\\</c> é barra invertida literal
/// — nenhum outro escape é suportado. Fora de aspas, o valor termina no primeiro espaço,
/// <c>)</c>, <c>,</c> ou fim da string; não há escape fora de aspas. Aspas não fechadas antes do
/// fim da expressão são erro de sintaxe.
/// </para>
/// <para>
/// <b>Operador <c>in</c>.</b> Exige lista entre parênteses, separada por vírgula:
/// <c>id__in=(1,2,3)</c>. Cada item segue a mesma regra de aspas dos valores simples. O parser
/// desfaz os parênteses e devolve os itens juntos em uma única string separada por vírgula (o
/// formato CSV que <see cref="Queryable.Builders.FilterBuilder"/> já consome via
/// <c>Split(',')</c>) — por isso um item cujo conteúdo resolvido contém uma vírgula literal (só
/// possível quando o item veio entre aspas) é rejeitado com um erro claro, em vez de gerar um
/// filtro silenciosamente errado.
/// </para>
/// </remarks>
public static class FilterExpressionParser
{
    /// <summary>
    /// Interpreta <paramref name="expression"/> na mini-linguagem e devolve a árvore
    /// <see cref="FilterNode"/> equivalente.
    /// </summary>
    /// <exception cref="ArgumentNullException">Quando <paramref name="expression"/> é <c>null</c>.</exception>
    /// <exception cref="FilterExpressionSyntaxException">
    /// Quando <paramref name="expression"/> é vazia/só espaços, ou tem qualquer erro de sintaxe:
    /// parêntese não fechado, aspas não fechadas, palavra-chave colidindo com valor não citado,
    /// lista do operador <c>in</c> fora de parênteses, item de <c>in</c> com vírgula literal,
    /// token sobrando após o fim da expressão, entre outros.
    /// </exception>
    public static FilterNode Parse(string expression)
    {
        ArgumentNullException.ThrowIfNull(expression);

        if (string.IsNullOrWhiteSpace(expression))
            throw new FilterExpressionSyntaxException("Expressão de filtro vazia ou apenas espaços", 1);

        List<FilterToken> tokens = FilterExpressionTokenizer.Tokenize(expression);
        var parser = new RecursiveDescentParser(tokens);
        FilterNode result = parser.ParseExpression();
        parser.ExpectEof();
        return result;
    }

    /// <summary>
    /// Parser recursivo descendente clássico: um método por nível de precedência da gramática.
    /// Mantém só um índice de leitura sobre a lista de tokens já pronta (sem backtracking).
    /// </summary>
    private sealed class RecursiveDescentParser(List<FilterToken> tokens)
    {
        private int _index;

        private FilterToken Current => tokens[_index];

        public FilterNode ParseExpression() => ParseOr();

        private FilterNode ParseOr()
        {
            var terms = new List<FilterNode> { ParseAnd() };

            while (IsKeyword("or"))
            {
                Advance();
                terms.Add(ParseAnd());
            }

            return terms.Count == 1 ? terms[0] : new FilterGroup(FilterLogic.Or, terms);
        }

        private FilterNode ParseAnd()
        {
            var terms = new List<FilterNode> { ParseUnary() };

            while (IsKeyword("and"))
            {
                Advance();
                terms.Add(ParseUnary());
            }

            return terms.Count == 1 ? terms[0] : new FilterGroup(FilterLogic.And, terms);
        }

        private FilterNode ParseUnary()
        {
            if (IsKeyword("not"))
            {
                Advance();
                return new FilterNot(ParsePrimary());
            }

            return ParsePrimary();
        }

        private FilterNode ParsePrimary()
        {
            if (Current.Type == FilterTokenType.LParen)
            {
                Advance();
                FilterNode inner = ParseExpression();
                Expect(FilterTokenType.RParen, "')' fechando o grupo");
                return inner;
            }

            return ParseComparison();
        }

        private FilterNode ParseComparison()
        {
            if (Current.Type != FilterTokenType.Word)
                throw SyntaxError("nome de campo esperado");

            string rawField = Current.Text;
            int fieldPosition = Current.Position;
            Advance();

            (string field, string op) = SplitFieldOperator(rawField, fieldPosition);

            Expect(FilterTokenType.Equals, "'=' separando campo e valor");

            string value = op.Equals("in", StringComparison.OrdinalIgnoreCase)
                ? ParseInList()
                : ParseScalarValue();

            return new FilterCondition(field, op, value);
        }

        private string ParseInList()
        {
            Expect(FilterTokenType.LParen,
                "'(' — o operador 'in' exige uma lista entre parênteses, ex.: campo__in=(1,2,3)");

            var items = new List<string>();

            if (Current.Type != FilterTokenType.RParen)
            {
                items.Add(ParseInListItem());

                while (Current.Type == FilterTokenType.Comma)
                {
                    Advance();
                    items.Add(ParseInListItem());
                }
            }

            Expect(FilterTokenType.RParen, "')' fechando a lista do operador 'in'");

            return string.Join(',', items);
        }

        private string ParseInListItem()
        {
            int position = Current.Position;
            string item = ParseScalarValue();

            if (item.Contains(','))
                throw new FilterExpressionSyntaxException(
                    $"Item de 'in' com vírgula literal ('{item}') ainda não é suportado: ficaria " +
                    "ambíguo no formato CSV que o operador 'in' usa internamente",
                    position);

            return item;
        }

        private string ParseScalarValue()
        {
            FilterToken token = Current;

            switch (token.Type)
            {
                case FilterTokenType.QuotedString:
                    Advance();
                    return token.Text;

                case FilterTokenType.Word:
                    Advance();
                    return token.Text;

                case FilterTokenType.Keyword:
                    throw new FilterExpressionSyntaxException(
                        $"A palavra reservada '{token.Text}' não pode ser usada como valor sem " +
                        $"aspas; para usá-la como valor literal, escreva \"{token.Text}\"",
                        token.Position);

                default:
                    throw SyntaxErrorAt("valor esperado", token);
            }
        }

        private static (string field, string op) SplitFieldOperator(string rawField, int position)
        {
            // FilterOperators.All já vem ordenado por comprimento decrescente — garante que
            // "gte" seja testado antes de "gt" ao casar o sufixo __operador.
            foreach (string op in FilterOperators.All)
            {
                string suffix = $"__{op}";
                if (rawField.Length > suffix.Length &&
                    rawField.EndsWith(suffix, StringComparison.OrdinalIgnoreCase))
                    return (rawField[..^suffix.Length], op);
            }

            if (rawField.Length == 0)
                throw new FilterExpressionSyntaxException("Nome de campo vazio", position);

            return (rawField, FilterOperators.Default);
        }

        private bool IsKeyword(string text) =>
            Current.Type == FilterTokenType.Keyword &&
            Current.Text.Equals(text, StringComparison.OrdinalIgnoreCase);

        private void Advance()
        {
            if (_index < tokens.Count - 1)
                _index++;
        }

        private void Expect(FilterTokenType type, string expectedDescription)
        {
            if (Current.Type != type)
                throw SyntaxError($"esperado {expectedDescription}");

            Advance();
        }

        public void ExpectEof()
        {
            if (Current.Type != FilterTokenType.Eof)
                throw SyntaxError($"token inesperado '{Current.Text}' — esperado o fim da expressão");
        }

        private FilterExpressionSyntaxException SyntaxError(string message) => SyntaxErrorAt(message, Current);

        private static FilterExpressionSyntaxException SyntaxErrorAt(string message, FilterToken token) =>
            new(DescribeError(message, token), token.Position);

        private static string DescribeError(string message, FilterToken token) =>
            token.Type == FilterTokenType.Eof
                ? $"{message}, mas a expressão terminou"
                : $"{message}, mas foi encontrado '{token.Text}'";
    }
}
