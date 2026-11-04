using MediPassPlus.Api.Extensions;
using MediPassPlus.Application.Validator;
using FluentValidation;
using Microsoft.EntityFrameworkCore;
using MediPassPlus.Infrastructure.Extensions;
using MediPassPlus.Api.Errors;
using MediPassPlus.Api.OpenApi;
using MediPassPlus.Api.Validation;
using MediPassPlus.Application.Validation;
using MediPassPlus.Infrastructure.Persistence;
using MediPassPlus.Infrastructure.Persistence.Seeding;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.

// ((( because we are using a separate Extensions class to configure the services, we can call it here)))
//builder.Services.AddDbContext<MediPassDbContext>(options =>
//  options.UseSqlServer(builder.Configuration.GetConnectionString("Default")));

// Seeding: each module adds one line here, for example:
// builder.Services.AddScoped<ISeeder, ClinicsSeeder>();
builder.Services.AddScoped<DatabaseSeeder>();

builder.Services.AddControllers();
// Learn more about configuring OpenAPI at https://aka.ms/aspnet/openapi
  
builder.Services.AddControllers(options =>
{
    // Runs any registered IValidator<T> before the action and returns 400 ValidationProblemDetails on failure
    // (ADR-0010, NFR-SEC-06).
    options.Filters.Add<ValidationFilter>();
});

// One error envelope for every failure, with no stack traces in the response (ADR-0010, NFR-REL-04).
builder.Services.AddProblemDetails();
builder.Services.AddExceptionHandler<ApiExceptionHandler>();

// Validators are discovered from the Application assembly. Every module adds its own assembly here.
builder.Services.AddValidatorsFromAssemblyContaining<SampleRequestValidator>();

// OpenAPI document (XML documentation is folded in by the .NET 10 source generator) plus JWT bearer metadata.
builder.Services.AddOpenApi("v1", options =>
{
    options.AddDocumentTransformer<BearerSecuritySchemeTransformer>();
    options.AddOperationTransformer<BearerSecuritySchemeTransformer>();
});

// DbContext + Identity + Roles + JWT (all registered in Infrastructure/Extensions)
builder.Services.AddInfrastructure(builder.Configuration);
//  Swagger Services
builder.Services.AddSwaggerDocumentation();
var app = builder.Build();


// Must run first so every downstream failure goes through ApiExceptionHandler (ADR-0010).
app.UseExceptionHandler();
await app.SeedDefaultRolesAsync();
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
    app.UseSwagger();
    app.UseSwaggerUI();
  
    app.MapOpenApi();
    app.UseSwaggerUI(options =>
    {
        options.SwaggerEndpoint("/openapi/v1.json", "MediPass+ API v1");
        options.DocumentTitle = "MediPass+ API";
    });
}

app.UseHttpsRedirection();
        // Add authentication and authorization middleware
 app.UseAuthentication();
app.UseAuthorization();

app.MapControllers();

app.Run();
