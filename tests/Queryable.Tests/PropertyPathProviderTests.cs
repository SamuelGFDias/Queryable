using System.Reflection;
using Xunit;
using Queryable.Extensions;
using Queryable.Interfaces;

namespace Queryable.Tests;

public class PropertyPathProviderTests
{
    [Fact]
    public void GetPaths_DevolveMesmoConteudoQuePathExtension()
    {
        IPropertyPathProvider provider = new ReflectionPropertyPathProvider();

        Dictionary<string, List<PropertyInfo>> esperado = PathExtension.BuildPropertyPaths<Produto>();
        IReadOnlyDictionary<string, List<PropertyInfo>> obtido = provider.GetPaths<Produto>();

        Assert.Equal(esperado.Keys.OrderBy(k => k), obtido.Keys.OrderBy(k => k));

        foreach (string chave in esperado.Keys)
            Assert.Equal(esperado[chave], obtido[chave]);
    }

    [Fact]
    public void GetPaths_MesmoTipoChamadoDuasVezes_DevolveMesmaInstancia()
    {
        IPropertyPathProvider provider = new ReflectionPropertyPathProvider();

        IReadOnlyDictionary<string, List<PropertyInfo>> primeira = provider.GetPaths<Produto>();
        IReadOnlyDictionary<string, List<PropertyInfo>> segunda = provider.GetPaths<Produto>();

        Assert.Same(primeira, segunda);
    }

    [Fact]
    public void GetPaths_TiposDiferentes_DevolveInstanciasDiferentes()
    {
        IPropertyPathProvider provider = new ReflectionPropertyPathProvider();

        IReadOnlyDictionary<string, List<PropertyInfo>> produto = provider.GetPaths<Produto>();
        IReadOnlyDictionary<string, List<PropertyInfo>> categoria = provider.GetPaths<Categoria>();

        Assert.NotSame(produto, categoria);
    }

    [Fact]
    public void GetPaths_BuscaDeChave_ContinuaCaseInsensitive()
    {
        IPropertyPathProvider provider = new ReflectionPropertyPathProvider();

        IReadOnlyDictionary<string, List<PropertyInfo>> caminhos = provider.GetPaths<Produto>();

        Assert.True(caminhos.TryGetValue("categoria.NOME", out List<PropertyInfo>? viaMaiuscula));
        Assert.True(caminhos.TryGetValue("categoria.nome", out List<PropertyInfo>? viaMinuscula));
        Assert.Equal(viaMinuscula, viaMaiuscula);
    }

    [Fact]
    public void GetPaths_ChamadasConcorrentesParaMesmoTipo_NaoLancaEDevolveMesmaInstancia()
    {
        IPropertyPathProvider provider = new ReflectionPropertyPathProvider();
        var resultados = new IReadOnlyDictionary<string, List<PropertyInfo>>[100];

        var exception = Record.Exception(() =>
            Parallel.For(0, resultados.Length, i =>
            {
                resultados[i] = provider.GetPaths<Produto>();
            }));

        Assert.Null(exception);
        Assert.All(resultados, r => Assert.Same(resultados[0], r));
    }
}
