using Api.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Api.Context.EntityMaps;

public class ProdutoMap : IEntityTypeConfiguration<Produto>
{
    public void Configure(EntityTypeBuilder<Produto> builder)
    {
        builder.HasKey(p => p.Id);

        builder.Property(a => a.Id)
               .ValueGeneratedOnAdd();

        builder.Property(a => a.Descricao)
               .HasMaxLength(255);

        builder.Property(a => a.Preco)
               .IsRequired();

        builder.Property(a => a.Criado)
               .IsRequired();
    }
}