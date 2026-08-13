using global::Microsoft.Data.Sqlite;
using global::Microsoft.EntityFrameworkCore;
using global::Microsoft.EntityFrameworkCore.Diagnostics;

namespace Queryable.EntityFrameworkCore.Tests;

/// <summary>
/// Cria um banco SQLite in-memory de verdade (provider relacional, não LINQ to Objects) para
/// cada teste. A conexão precisa ficar aberta durante toda a vida do teste: fechá-la destrói
/// o banco <c>:memory:</c> do SQLite.
/// </summary>
public sealed class SqliteInMemoryFixture : IDisposable
{
    public static readonly Guid MouseId = Guid.Parse("00000000-0000-0000-0000-000000000001");
    public static readonly Guid TecladoId = Guid.Parse("00000000-0000-0000-0000-000000000002");
    public static readonly Guid MonitorId = Guid.Parse("00000000-0000-0000-0000-000000000003");
    public static readonly Guid NotebookId = Guid.Parse("00000000-0000-0000-0000-000000000004");
    public static readonly Guid CaboHdmiId = Guid.Parse("00000000-0000-0000-0000-000000000005");
    public static readonly Guid ImpressoraId = Guid.Parse("00000000-0000-0000-0000-000000000006");

    public SqliteConnection Connection { get; }
    public TestDbContext Context { get; }

    public SqliteInMemoryFixture()
    {
        Connection = new SqliteConnection("Filename=:memory:");
        Connection.Open();

        DbContextOptions<TestDbContext> options = new DbContextOptionsBuilder<TestDbContext>()
            .UseSqlite(Connection)
            .ConfigureWarnings(w => w.Throw(RelationalEventId.CommandError))
            .Options;

        Context = new TestDbContext(options);
        Context.Database.EnsureCreated();

        Seed(Context);
    }

    private static void Seed(TestDbContext context)
    {
        var perifericos = new Categoria { Id = 1, Nome = "Perifericos" };
        var informatica = new Categoria { Id = 2, Nome = "Informatica" };
        var escritorio = new Categoria { Id = 3, Nome = "Escritorio" };

        context.Categorias.AddRange(perifericos, informatica, escritorio);

        context.Produtos.AddRange(
            new Produto { Id = MouseId, Nome = "Mouse Gamer", Preco = 150m, Ativo = true, CategoriaId = perifericos.Id, Categoria = perifericos },
            new Produto { Id = TecladoId, Nome = "Teclado Mecanico", Preco = 300m, Ativo = true, CategoriaId = perifericos.Id, Categoria = perifericos },
            new Produto { Id = MonitorId, Nome = "Monitor 24 Polegadas", Preco = 900m, Ativo = false, CategoriaId = perifericos.Id, Categoria = perifericos },
            new Produto { Id = NotebookId, Nome = "Notebook", Preco = 3500m, Ativo = true, CategoriaId = informatica.Id, Categoria = informatica },
            new Produto { Id = CaboHdmiId, Nome = "Cabo HDMI", Preco = 25m, Ativo = true, CategoriaId = informatica.Id, Categoria = informatica },
            new Produto { Id = ImpressoraId, Nome = "Impressora", Preco = 700m, Ativo = false, CategoriaId = escritorio.Id, Categoria = escritorio }
        );

        context.SaveChanges();
    }

    public void Dispose()
    {
        Context.Dispose();
        Connection.Close();
        Connection.Dispose();
    }
}
