namespace Queryable.Configuration;

/// <summary>
/// Interface não genérica implementada explicitamente por <see cref="QueryableConfiguration{TEntity}"/>,
/// que permite ao <see cref="QueryableConfigurationRegistry"/> e às extensões de DI manipular uma
/// configuração concreta (ex.: <c>UsuarioQueryConfiguration</c>) sem conhecer <c>TEntity</c> em
/// tempo de compilação — necessário para <c>AddQueryableConfiguration&lt;TConfiguration&gt;()</c>
/// (um único parâmetro de tipo genérico) e para a varredura de assembly.
/// </summary>
internal interface IQueryableConfigurationSource
{
    /// <summary>O <c>TEntity</c> ao qual esta configuração se aplica.</summary>
    Type EntityType { get; }

    /// <summary>Materializa o resultado imutável da configuração acumulada até aqui.</summary>
    QueryableTypeConfiguration Build();
}
