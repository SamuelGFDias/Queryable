using System.Text.Json;
using Queryable.Extensions;
using Queryable.Filtering;
using Xunit;

namespace Queryable.Tests;

/// <summary>
/// Etapa 7 (issue #7): os limites configurados chegam ao <see cref="FilterNodeJsonConverter"/>
/// quando uma instância construída com eles é registrada em
/// <see cref="JsonSerializerOptions.Converters"/> via
/// <see cref="JsonSerializerOptionsExtensions.AddQueryableFilterLimits"/> — a precedência do
/// <c>System.Text.Json</c> faz essa instância vencer o atributo <c>[JsonConverter]</c> de
/// <see cref="FilterNode"/>. Sem o registro, os defaults continuam valendo.
/// </summary>
public class FilterNodeJsonConverterLimitsTests
{
    /// <summary>Grupo AND com <paramref name="childCount"/> condições — total de nós = childCount + 1.</summary>
    private static string WideGroupJson(int childCount)
    {
        string children = string.Join(",", Enumerable.Range(0, childCount)
            .Select(i => $$"""{ "field": "campo{{i}}", "value": "x" }"""));
        return $$"""{ "logic": "and", "children": [{{children}}] }""";
    }

    [Fact]
    public void Deserialize_ComLimitesRegistradosNasOpcoes_ArvoreAcimaDoConfiguradoMasAbaixoDoDefault_Lanca()
    {
        var options = new JsonSerializerOptions().AddQueryableFilterLimits(new FilterLimits { MaxNodes = 10 });
        string json = WideGroupJson(30); // 31 nós: > 10, < 100

        var ex = Assert.Throws<FilterLimitExceededException>(() => JsonSerializer.Deserialize<FilterNode>(json, options));

        Assert.Equal(FilterLimitKind.MaxNodes, ex.Limit);
        Assert.Equal(10, ex.Allowed);
    }

    [Fact]
    public void Deserialize_SemRegistroExplicito_AtributoDoTipoValeEUsaDefaults()
    {
        string json = WideGroupJson(30);

        FilterNode? node = JsonSerializer.Deserialize<FilterNode>(json);

        var group = Assert.IsType<FilterGroup>(node);
        Assert.Equal(30, group.Children.Count);
    }

    [Fact]
    public void Deserialize_ComLimitesRegistrados_ArvoreDentroDoConfigurado_NaoLanca()
    {
        var options = new JsonSerializerOptions().AddQueryableFilterLimits(new FilterLimits { MaxNodes = 10 });

        FilterNode? node = JsonSerializer.Deserialize<FilterNode>(WideGroupJson(5), options);

        Assert.IsType<FilterGroup>(node);
    }

    [Fact]
    public void AddQueryableFilterLimits_ChamadoDuasVezes_DeixaUmUnicoConversorComOsUltimosLimites()
    {
        var options = new JsonSerializerOptions();
        options.AddQueryableFilterLimits(new FilterLimits { MaxNodes = 10 });
        options.AddQueryableFilterLimits(new FilterLimits { MaxNodes = 20 });

        var converter = Assert.Single(options.Converters.OfType<FilterNodeJsonConverter>());
        Assert.Equal(20, converter.Limits.MaxNodes);
    }

    [Fact]
    public void AddQueryableFilterLimits_ArgumentosNulos_LancaArgumentNullException()
    {
        Assert.Throws<ArgumentNullException>(() => ((JsonSerializerOptions)null!).AddQueryableFilterLimits(new FilterLimits()));
        Assert.Throws<ArgumentNullException>(() => new JsonSerializerOptions().AddQueryableFilterLimits(null!));
    }

    [Fact]
    public void ConstrutorSemArgumentos_UsaFilterLimitsDefault()
    {
        Assert.Same(FilterLimits.Default, new FilterNodeJsonConverter().Limits);
    }
}
