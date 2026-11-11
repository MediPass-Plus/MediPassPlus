using MediPassPlus.Api.Extensions;
using MediPassPlus.Api.RateLimiting;
using MediPassPlus.Application.Validator;
using FluentValidation;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using MediPassPlus.Infrastructure.Extensions;
using MediPassPlus.Api.Errors;
using MediPassPlus.Api.Validation;
using MediPassPlus.Application.Validation;
using MediPassPlus.Infrastructure.Persistence;
using MediPassPlus.Infrastructure.Persistence.Seeding;
using Microsoft.Extensions.Options;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.

// ((( because we are using a separate Extensions class to configure the services, we can call it here)))
//builder.Services.AddDbContext<MediPassDbContext>(options =>
//  options.UseSqlServer(builder.Configuration.GetConnectionString("Default")));

// Seeding: each module adds one line here, for example:
// builder.Services.AddScoped<ISeeder, ClinicsSeeder>();
// Demo data (MP-96): the password comes from user-secrets and is never committed (NFR-SEC-08).
builder.Services.Configure<DemoSeedOptions>(builder.Configuration.GetSection(DemoSeedOptions.SectionName));
builder.Services.AddScoped<ISeeder, DemoDataSeeder>();
builder.Services.AddScoped<DatabaseSeeder>();

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

// Rate limits are configuration bound through IOptions and validated at startup (ADR-0007 decision 2).
builder.Services.AddOptions<QrApproveRateLimitOptions>()
    .Bind(builder.Configuration.GetSection(QrApproveRateLimitOptions.SectionName))
    .ValidateDataAnnotations()
    .ValidateOnStart();

// One named policy per endpoint group (ADR-0007 decision 4). The QrApprove policy is reused by every approve/reject
// endpoint, including the scan-token path planned for V6, so no endpoint adds its own limiter.
builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;

    options.AddPolicy(QrApproveRateLimitPolicy.Name, httpContext =>
    {
        var limits = httpContext.RequestServices
            .GetRequiredService<IOptions<QrApproveRateLimitOptions>>()
            .Value;

        return QrApproveRateLimitPolicy.CreatePartition(httpContext, limits);
    });

    options.OnRejected = static (context, cancellationToken) =>
        QrApproveRateLimitPolicy.WriteRejectionAsync(context.HttpContext, context.Lease, cancellationToken);
});

// Validators are discovered from the Application assembly. Every module adds its own assembly here.
builder.Services.AddValidatorsFromAssemblyContaining<SampleRequestValidator>();

// DbContext + Identity + Roles + JWT (all registered in Infrastructure/Extensions)
builder.Services.AddInfrastructure(builder.Configuration);
// Swashbuckle is the single Swagger document generator and UI (ADR-0012).
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
}

app.UseHttpsRedirection();
        // Add authentication and authorization middleware
 app.UseAuthentication();
app.UseAuthorization();

// Must run after authentication so the QrApprove partition sees the patient id, and before endpoints so a rejected
// decision never reaches the database (ADR-0007 decisions 2 and 4).
app.UseRateLimiter();

app.MapControllers();

app.Run();

/// <summary>
/// The API entry point, made public so <c>WebApplicationFactory&lt;Program&gt;</c> can boot the real pipeline in the
/// HTTP integration tests (ADR-0006).
/// </summary>
public partial class Program
{
}
