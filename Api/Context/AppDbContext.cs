using Api.Interfaces;
using Api.Models;
using Microsoft.EntityFrameworkCore;

namespace Api.Context;

public class AppDbContext(DbContextOptions<AppDbContext> options) : DbContext(options)
{
    public DbSet<Produto> Produtos { get; set; }

    protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
    {
        if (optionsBuilder.IsConfigured) return;

        optionsBuilder.UseSqlite("Data Source=app.db;");
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        List<Type> configTypes = LoadEntityTypeConfigurationTypes();

        InitializeModelMappings(modelBuilder, configTypes);

        List<Type> allEntityTypes = GetEntityTypes(modelBuilder);

        IgnoreUnmappedTypes(modelBuilder, allEntityTypes, configTypes);
    }


    private static void IgnoreUnmappedTypes(
        ModelBuilder modelBuilder,
        List<Type> allEntityTypes,
        List<Type> configTypes
    )
    {
        HashSet<Type> mappedTypes = configTypes
                                   .Select(cfg => cfg.GetInterfaces()
                                                     .First(i => i.IsGenericType
                                                              && i.GetGenericTypeDefinition()
                                                              == typeof(IEntityTypeConfiguration<>))
                                                     .GenericTypeArguments[0])
                                   .ToHashSet();

        foreach (Type type in
                 from type in allEntityTypes
                 where !mappedTypes.Contains(type)
                 let baseType = type.BaseType
                 where type.IsAbstract
                    || !type.IsAssignableTo(typeof(IEntity))
                 select type)
        {
            modelBuilder.Ignore(type);
        }
    }

    private static void InitializeModelMappings(ModelBuilder modelBuilder, List<Type> configTypes)
    {
        foreach (object? cfgInstance in configTypes.Select(Activator.CreateInstance))
            modelBuilder.ApplyConfiguration((dynamic?)cfgInstance);
    }

    private static List<Type> LoadEntityTypeConfigurationTypes()
    {
        return typeof(AppDbContext).Assembly.GetTypes()
                                   .Where(t => t is { IsAbstract: false, IsInterface: false })
                                   .Where(t =>
                                    {
                                        Type[] interfaces = t.GetInterfaces();
                                        return interfaces.Any(i =>
                                                                  i.IsGenericType
                                                               && i.GetGenericTypeDefinition()
                                                               == typeof(IEntityTypeConfiguration<>));
                                    })
                                   .ToList();
    }

    private static List<Type> GetEntityTypes(ModelBuilder modelBuilder)
    {
        List<Type> entityTypes = modelBuilder.Model.GetEntityTypes().Select(et => et.ClrType).ToList();
        return entityTypes;
    }
}