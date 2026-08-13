using global::Microsoft.EntityFrameworkCore;
using global::Microsoft.EntityFrameworkCore.Diagnostics;

namespace Queryable.EntityFrameworkCore.Tests;

/// <summary>
/// <see cref="DbContext"/> de teste. Namespace raiz do repositório (<c>Queryable</c>) colide
/// com <c>Microsoft.EntityFrameworkCore</c>, por isso o <c>using global::</c> explícito — o
/// mesmo truque usado em <c>PagedQueryService.cs</c> para resolver os tipos do EF Core.
/// </summary>
public class TestDbContext(DbContextOptions<TestDbContext> options) : DbContext(options)
{
    public DbSet<Produto> Produtos => Set<Produto>();
    public DbSet<Categoria> Categorias => Set<Categoria>();

    protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
    {
        // Faz a consulta lançar em vez de cair silenciosamente em avaliação no cliente,
        // para que os testes peguem erros de tradução para SQL (o que LINQ to Objects
        // nunca pegaria, por rodar tudo em memória).
        optionsBuilder.ConfigureWarnings(w => w.Throw(RelationalEventId.CommandError));
    }
}
