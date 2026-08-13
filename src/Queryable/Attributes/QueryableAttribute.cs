namespace Queryable.Attributes;

[AttributeUsage(AttributeTargets.Property)]
public class QueryableAttribute(string? alias = null) : Attribute
{
    public string? Alias { get; set; } = alias;
}