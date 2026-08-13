using Xunit;
using System.Reflection;
using Queryable.Extensions;

namespace Queryable.Tests;

public class PathExtensionTests
{
    [Fact]
    public void BuildPropertyPaths_NavegacaoBidirecional_NaoEstouraERetorna()
    {
        // Produto.Categoria -> Categoria.Produtos (List<Produto>) -> guarda de coleção corta.
        Dictionary<string, List<PropertyInfo>> caminhosProduto = PathExtension.BuildPropertyPaths<Produto>();
        Dictionary<string, List<PropertyInfo>> caminhosCategoria = PathExtension.BuildPropertyPaths<Categoria>();

        Assert.NotEmpty(caminhosProduto);
        Assert.NotEmpty(caminhosCategoria);
    }

    [Fact]
    public void BuildPropertyPaths_NenhumAliasContemMembrosDeColecao()
    {
        Dictionary<string, List<PropertyInfo>> caminhos = PathExtension.BuildPropertyPaths<Categoria>();

        Assert.DoesNotContain(caminhos.Keys, k => k.Contains(".capacity", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(caminhos.Keys, k => k.Contains(".count", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(caminhos.Keys, k => k.Contains(".item", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void BuildPropertyPaths_ContemCaminhoAninhadoDeCategoria()
    {
        Dictionary<string, List<PropertyInfo>> caminhos = PathExtension.BuildPropertyPaths<Produto>();

        Assert.True(caminhos.ContainsKey("categoria.nome"));
        Assert.True(caminhos.ContainsKey("categoria.id"));
    }

    [Fact]
    public void BuildPropertyPaths_AliasDeAtributo_SubstituiNomeOriginal()
    {
        Dictionary<string, List<PropertyInfo>> caminhos = PathExtension.BuildPropertyPaths<Produto>();

        Assert.True(caminhos.ContainsKey("valor"));
        Assert.False(caminhos.ContainsKey("preco"));
    }

    [Fact]
    public void BuildPropertyPaths_RamosIrmaosDoMesmoTipo_AmbosPresentes()
    {
        Dictionary<string, List<PropertyInfo>> caminhos = PathExtension.BuildPropertyPaths<Pedido>();

        Assert.True(caminhos.ContainsKey("enderecoentrega.rua"));
        Assert.True(caminhos.ContainsKey("enderecocobranca.rua"));
    }

    [Fact]
    public void BuildPropertyPaths_CicloDireto_RetornaContemProximoMasNaoProximoProximo()
    {
        Dictionary<string, List<PropertyInfo>> caminhos = PathExtension.BuildPropertyPaths<No>();

        Assert.True(caminhos.ContainsKey("proximo"));
        Assert.False(caminhos.ContainsKey("proximo.proximo"));
    }

    [Fact]
    public void BuildPropertyPaths_ChavesSaoCaseInsensitive()
    {
        Dictionary<string, List<PropertyInfo>> caminhos = PathExtension.BuildPropertyPaths<Produto>();

        Assert.True(caminhos.ContainsKey("NOME"));
        Assert.True(caminhos.ContainsKey("Nome"));
        Assert.True(caminhos.ContainsKey("CATEGORIA.NOME"));
    }
}
