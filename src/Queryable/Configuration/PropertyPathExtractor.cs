using System.Linq.Expressions;
using System.Reflection;

namespace Queryable.Configuration;

/// <summary>
/// Extrai, a partir de uma <see cref="LambdaExpression"/> em forma de seletor de propriedade
/// (ex.: <c>u =&gt; u.Cpf.Value</c>), a mesma estrutura <see cref="List{PropertyInfo}"/> que
/// <c>PathExtension.BuildPropertyPaths&lt;T&gt;()</c> já produz por reflexão — o segundo produtor
/// da mesma estrutura de dados consumida por <c>FilterBuilder</c> e <c>SortBuilder</c>
/// (<c>path.Aggregate&lt;PropertyInfo, Expression&gt;(parameter, Expression.Property)</c>).
/// </summary>
internal static class PropertyPathExtractor
{
    /// <summary>
    /// Percorre o corpo da lambda desembrulhando <see cref="UnaryExpression"/> (conversão/boxing)
    /// e encadeando <see cref="MemberExpression"/> até o <see cref="ParameterExpression"/> raiz,
    /// coletando o <see cref="PropertyInfo"/> de cada nó na ordem raiz → folha.
    /// </summary>
    /// <param name="selector">A expressão de seletor, ex.: <c>u =&gt; u.Cpf.Value</c>.</param>
    /// <param name="metodo">
    /// Nome do método público que chamou a extração (<c>For</c> ou <c>Ignore</c>), usado apenas
    /// para compor a mensagem de erro.
    /// </param>
    /// <exception cref="ArgumentException">
    /// Quando a expressão não é uma cadeia de propriedades válida: chamada de método, indexador,
    /// campo (<see cref="FieldInfo"/>) ou qualquer outra coisa que não seja
    /// <see cref="MemberExpression"/> sobre <see cref="PropertyInfo"/>.
    /// </exception>
    public static List<PropertyInfo> Extract(LambdaExpression selector, string metodo = "For")
    {
        var pilha = new Stack<PropertyInfo>();
        Expression atual = selector.Body;

        while (true)
        {
            if (atual is UnaryExpression { NodeType: ExpressionType.Convert or ExpressionType.ConvertChecked } unario)
            {
                atual = unario.Operand;
                continue;
            }

            if (atual is MemberExpression { Member: PropertyInfo propriedade } membro)
            {
                pilha.Push(propriedade);
                atual = membro.Expression
                     ?? throw NaoEhCadeiaDePropriedades(selector, metodo);
                continue;
            }

            if (atual is ParameterExpression)
                break;

            throw NaoEhCadeiaDePropriedades(selector, metodo);
        }

        return pilha.ToList();
    }

    private static ArgumentException NaoEhCadeiaDePropriedades(LambdaExpression selector, string metodo) =>
        new($"A expressão '{selector}' não é uma cadeia de propriedades válida para {metodo}().");
}
