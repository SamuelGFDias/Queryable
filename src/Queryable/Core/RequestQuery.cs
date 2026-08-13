namespace Queryable.Core;

/// <summary>
/// Modelo achatado de requisição, pensado para APIs que recebem os parâmetros
/// de filtro, ordenação e paginação como campos simples (ex.: corpo de request,
/// DTO de query string única) em vez do formato posicional baseado em dicionário.
/// Use <c>ToQuerySpec</c> para convertê-lo em um <see cref="QuerySpec{T}"/> pronto
/// para aplicar sobre um <see cref="IQueryable{T}"/>.
/// </summary>
public class RequestQuery
{
    /// <summary>
    /// String única contendo todos os filtros, no formato <c>campo__operador=valor</c>
    /// (sem sufixo <c>__operador</c> equivale a <c>eq</c>).
    /// Os pares são separados por <c>;</c> quando a string contém esse caractere;
    /// caso contrário, são separados por <c>,</c> (compatibilidade com o formato antigo).
    /// Use <c>;</c> sempre que algum valor for uma lista CSV do operador <c>in</c>,
    /// pois o valor de <c>in</c> é separado por vírgula.
    /// Exemplos válidos: <c>"nome=João,idade__gt=18"</c> ou <c>"id__in=1,2,3;ativo=true"</c>.
    /// Valor padrão: <c>null</c> (nenhum filtro).
    /// </summary>
    public string? QueryFilter { get; set; }

    /// <summary>
    /// Campo(s) de ordenação, no mesmo formato aceito por <see cref="QuerySpec{T}.Sort"/>.
    /// Valor padrão: <c>null</c> (sem ordenação).
    /// </summary>
    public string? Sort { get; set; }

    /// <summary>
    /// Número da página, começando em 1. Valor padrão: <c>1</c>.
    /// </summary>
    public int Page { get; set; } = 1;

    /// <summary>
    /// Quantidade de itens por página. Valor padrão: <c>10</c>.
    /// </summary>
    public int PageSize { get; set; } = 10;

    /// <summary>
    /// Quando <c>true</c>, indica que a contagem total de itens deve ser omitida
    /// (útil para otimizar consultas paginadas quando o total não é necessário).
    /// Valor padrão: <c>false</c>.
    /// </summary>
    public bool SkipTotalCount { get; set; }
}
