using Microsoft.EntityFrameworkCore;
using MediPassPlus.Api.OpenApi;
using MediPassPlus.Infrastructure.Persistence;
using MediPassPlus.Infrastructure.Persistence.Seeding;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
builder.Services.AddDbContext<MediPassDbContext>(options =>
    options.UseSqlServer(builder.Configuration.GetConnectionString("Default")));

// Seeding: each module adds one line here, for example:
// builder.Services.AddScoped<ISeeder, ClinicsSeeder>();
builder.Services.AddScoped<DatabaseSeeder>();

builder.Services.AddControllers();
// OpenAPI document (XML documentation is folded in by the .NET 10 source generator) plus JWT bearer metadata.
builder.Services.AddOpenApi("v1", options =>
{
    options.AddDocumentTransformer<BearerSecuritySchemeTransformer>();
    options.AddOperationTransformer<BearerSecuritySchemeTransformer>();
});

var app = builder.Build();

// Run the seeders in development only.
if (app.Environment.IsDevelopment())
{
    using var scope = app.Services.CreateScope();
    await scope.ServiceProvider.GetRequiredService<DatabaseSeeder>().RunAsync();
}

// Configure the HTTP request pipeline.
// The OpenAPI document and Swagger UI are development tools. They are served in Development only, unless a
// deployment explicitly opts in with OpenApi:ExposeOutsideDevelopment (ADR-0008).
var exposeApiReference = app.Environment.IsDevelopment()
    || app.Configuration.GetValue<bool>("OpenApi:ExposeOutsideDevelopment");

if (exposeApiReference)
{
    app.MapOpenApi();
    app.UseSwaggerUI(options =>
    {
        options.SwaggerEndpoint("/openapi/v1.json", "MediPass+ API v1");
        options.DocumentTitle = "MediPass+ API";
    });
}

app.UseHttpsRedirection();

app.UseAuthorization();

app.MapControllers();

app.Run();
