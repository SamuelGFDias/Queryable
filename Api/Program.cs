using Api.Context;
using Api.Interfaces;
using Api.Repositories;
using Api.Services;
using Queryable.Builders;
using Queryable.Core;
using Queryable.Extensions;
using Queryable.Interfaces;

WebApplicationBuilder? builder = WebApplication.CreateBuilder(args);

builder.Services.AddDbContext<AppDbContext>();

builder.Services.AddScoped<IQuerySpecApplier, QuerySpecApplier>();
builder.Services.AddScoped<IFilterBuilder, FilterBuilder>();
builder.Services.AddScoped<ISortBuilder, SortBuilder>();

builder.Services.AddScoped<IProdutoService, ProdutoService>();
builder.Services.AddScoped<IProdutoRepository, ProdutoRepository>();

builder.Services.AddControllers(options =>
{
    options.ModelBinderProviders.Insert(0, new QuerySpecModelBinderProvider());
});


builder.Services.AddSwaggerGen(c =>
{
    c.SwaggerDoc("v1.0", new() { Title = "Minha API", Version = "v1.0" });
    c.UseInlineDefinitionsForEnums();
});


WebApplication? app = builder.Build();

app.UseRouting();

app.UseSwagger();
app.UseSwaggerUI(c =>
{
    c.DocumentTitle = "Queryable";
    c.SwaggerEndpoint("/swagger/v1.0/swagger.json", "Minha API v1.0");
});

app.MapControllers();
app.Run();