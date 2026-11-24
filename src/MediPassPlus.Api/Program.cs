using System.Net;
using MediPassPlus.Api.Extensions;
using MediPassPlus.Api.Health;
using MediPassPlus.Api.Identity;
using MediPassPlus.Api.Logging;
using MediPassPlus.Api.RateLimiting;
using MediPassPlus.Api.Security;
using MediPassPlus.Application.Identity;
using MediPassPlus.Application.Validator;
using FluentValidation;
using Microsoft.AspNetCore.Cors.Infrastructure;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using MediPassPlus.Infrastructure.Extensions;
using MediPassPlus.Api.Errors;
using MediPassPlus.Api.Validation;
using MediPassPlus.Application.Validation;
using MediPassPlus.Infrastructure.Persistence;
using MediPassPlus.Infrastructure.Persistence.Seeding;
using MediPassPlus.Infrastructure.VisitAccess;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Options;
using Microsoft.Net.Http.Headers;

var builder = WebApplication.CreateBuilder(args);

// Do not advertise the server product in every response (NFR-SEC-07).
builder.WebHost.ConfigureKestrel(options => options.AddServerHeader = false);

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

// Swagger generation, the Bearer scheme and XML comments are configured in Extensions/SwaggerExtensions.cs
// (ADR-0012).

builder.Services.AddControllers(options =>
{
    // Runs any registered IValidator<T> before the action and returns 400 ValidationProblemDetails on failure
    // (ADR-0010, NFR-SEC-06).
    options.Filters.Add<ValidationFilter>();
});

// One error envelope for every failure, with no stack traces in the response (ADR-0010, NFR-REL-04).
builder.Services.AddProblemDetails();
builder.Services.AddExceptionHandler<ApiExceptionHandler>();

// The currently authenticated caller, resolved from the request's claims once (FR-ACC-07, FR-ACC-08). Controllers read
// the user id from ICurrentUser instead of parsing ClaimTypes.NameIdentifier themselves.
builder.Services.AddHttpContextAccessor();
builder.Services.AddScoped<ICurrentUser, HttpContextCurrentUser>();

// Rate limits are configuration bound through IOptions and validated at startup (ADR-0007 decision 2).
builder.Services.AddOptions<QrApproveRateLimitOptions>()
    .Bind(builder.Configuration.GetSection(QrApproveRateLimitOptions.SectionName))
    .ValidateDataAnnotations()
    .ValidateOnStart();

builder.Services.AddOptions<CodeGenerationRateLimitOptions>()
    .Bind(builder.Configuration.GetSection(CodeGenerationRateLimitOptions.SectionName))
    .ValidateDataAnnotations()
    .ValidateOnStart();

builder.Services.AddOptions<CodeVerificationRateLimitOptions>()
    .Bind(builder.Configuration.GetSection(CodeVerificationRateLimitOptions.SectionName))
    .ValidateDataAnnotations()
    .ValidateOnStart();

// A stolen access token must not let an attacker guess the current password: ChangePasswordAsync does not raise the
// lockout counter, so this budget is the only brake on that path (ADR-0007 decision 2).
builder.Services.AddOptions<ChangePasswordRateLimitOptions>()
    .Bind(builder.Configuration.GetSection(ChangePasswordRateLimitOptions.SectionName))
    .ValidateDataAnnotations()
    .ValidateOnStart();

// The access-code HMAC key is a server secret, separate from the JWT key and the Data Protection ring
// (ADR-0007 decision 5). A missing or weak key fails startup instead of silently weakening the stored hash.
builder.Services.AddOptions<AccessCodeOptions>()
    .Bind(builder.Configuration.GetSection(AccessCodeOptions.SectionName))
    .Validate(
        options => options.TryGetKeyBytes(out _),
        "AccessCode:HmacKey must be the Base64 encoding of at least 32 random bytes.")
    .ValidateOnStart();

// FR-PRV-07: sign-in attempts are bounded per email and client address, in front of the authoritative lockout
// (ADR-0007 decisions 1 and 2).
builder.Services.AddOptions<LoginRateLimitOptions>()
    .Bind(builder.Configuration.GetSection(LoginRateLimitOptions.SectionName))
    .ValidateDataAnnotations()
    .ValidateOnStart();

// NFR-SEC-07: only the Angular application may call the API from a browser.
builder.Services.AddOptions<CorsSettings>()
    .Bind(builder.Configuration.GetSection(CorsSettings.SectionName))
    .ValidateDataAnnotations()
    .ValidateOnStart();

builder.Services.AddOptions<PasswordResetRequestRateLimitOptions>()
    .Bind(builder.Configuration.GetSection(PasswordResetRequestRateLimitOptions.SectionName))
    .ValidateDataAnnotations()
    .ValidateOnStart();

builder.Services.AddOptions<PasswordResetSubmitRateLimitOptions>()
    .Bind(builder.Configuration.GetSection(PasswordResetSubmitRateLimitOptions.SectionName))
    .ValidateDataAnnotations()
    .ValidateOnStart();

// Forwarded headers are opt-in: a deployment names the proxies it trusts, and an invalid or empty trust list fails
// startup instead of silently trusting a spoofed address (NFR-SEC-07).
builder.Services.AddOptions<ForwardedHeadersSettings>()
    .Bind(builder.Configuration.GetSection(ForwardedHeadersSettings.SectionName))
    .ValidateDataAnnotations()
    .ValidateOnStart();

builder.Services.AddCors();
builder.Services.AddOptions<CorsOptions>()
    .Configure<IOptions<CorsSettings>>((cors, settings) =>
    {
        cors.AddPolicy(
            CorsSettings.PolicyName,
            policy => policy
                .WithOrigins(settings.Value.AllowedOrigins ?? [])
                // The Angular client is cross-origin, and Retry-After is not a CORS-safelisted response header: without
                // this line response.headers.get('Retry-After') would be null on every 429 (ADR-0007 decision 3).
                .WithExposedHeaders(HeaderNames.RetryAfter)
                .AllowAnyHeader()
                .AllowAnyMethod());
    });

builder.Services.AddOptions<ForwardedHeadersOptions>()
    .Configure<IOptions<ForwardedHeadersSettings>>((headers, settings) =>
    {
        var value = settings.Value;

        headers.ForwardedHeaders = value.Enabled
            ? ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto
            : ForwardedHeaders.None;

        if (!value.Enabled)
        {
            return;
        }

        headers.ForwardLimit = value.ForwardLimit;
        headers.KnownProxies.Clear();
        headers.KnownIPNetworks.Clear();

        if (!value.TrustAllProxies)
        {
            foreach (var proxy in value.KnownProxies)
            {
                headers.KnownProxies.Add(IPAddress.Parse(proxy));
            }
        }
    });

// HSTS is an HTTPS-only header; Development keeps plain HTTP for local tools, every other environment advertises it.
builder.Services.AddHsts(options =>
{
    options.MaxAge = TimeSpan.FromDays(365);
    options.IncludeSubDomains = true;
    options.Preload = false;
});

// One named policy per endpoint group (ADR-0007 decision 4). The QrApprove policy is reused by every approve/reject
// endpoint, including the scan-token path planned for V6, so no endpoint adds its own limiter.
builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;

    options.AddPolicy(LoginRateLimitPolicy.Name, httpContext =>
    {
        var limits = httpContext.RequestServices
            .GetRequiredService<IOptions<LoginRateLimitOptions>>()
            .Value;

        return LoginRateLimitPolicy.CreatePartition(httpContext, limits);
    });

    options.AddPolicy(PasswordResetRateLimitPolicy.RequestName, httpContext =>
    {
        var limits = httpContext.RequestServices
            .GetRequiredService<IOptions<PasswordResetRequestRateLimitOptions>>()
            .Value;

        return PasswordResetRateLimitPolicy.CreateRequestPartition(httpContext, limits);
    });

    options.AddPolicy(PasswordResetRateLimitPolicy.SubmitName, httpContext =>
    {
        var limits = httpContext.RequestServices
            .GetRequiredService<IOptions<PasswordResetSubmitRateLimitOptions>>()
            .Value;

        return PasswordResetRateLimitPolicy.CreateSubmitPartition(httpContext, limits);
    });

    options.AddPolicy(QrApproveRateLimitPolicy.Name, httpContext =>
    {
        var limits = httpContext.RequestServices
            .GetRequiredService<IOptions<QrApproveRateLimitOptions>>()
            .Value;

        return QrApproveRateLimitPolicy.CreatePartition(httpContext, limits);
    });

    // The six-digit fallback has its own generation and verification budgets (ADR-0007 decision 2).
    options.AddPolicy(AccessCodeRateLimitPolicy.GenerationName, httpContext =>
    {
        var limits = httpContext.RequestServices
            .GetRequiredService<IOptions<CodeGenerationRateLimitOptions>>()
            .Value;

        return AccessCodeRateLimitPolicy.CreateGenerationPartition(httpContext, limits);
    });

    options.AddPolicy(AccessCodeRateLimitPolicy.VerificationName, httpContext =>
    {
        var limits = httpContext.RequestServices
            .GetRequiredService<IOptions<CodeVerificationRateLimitOptions>>()
            .Value;

        return AccessCodeRateLimitPolicy.CreateVerificationPartition(httpContext, limits);
    });

    // The self-service password change is limited per account, not per IP (NFR-SEC-06).
    options.AddPolicy(ChangePasswordRateLimitPolicy.Name, httpContext =>
    {
        var limits = httpContext.RequestServices
            .GetRequiredService<IOptions<ChangePasswordRateLimitOptions>>()
            .Value;

        return ChangePasswordRateLimitPolicy.CreatePartition(httpContext, limits);
    });

    // The rejection body and Retry-After header are shared, but each policy family keeps its own title.
    options.OnRejected = static (context, cancellationToken) =>
    {
        var policyName = context.HttpContext.GetEndpoint()
            ?.Metadata.GetMetadata<EnableRateLimitingAttribute>()?.PolicyName;

        return policyName switch
        {
            LoginRateLimitPolicy.Name => LoginRateLimitPolicy.WriteRejectionAsync(
                context.HttpContext,
                context.Lease,
                cancellationToken),
            PasswordResetRateLimitPolicy.RequestName => PasswordResetRateLimitPolicy.WriteRequestRejectionAsync(
                context.HttpContext,
                context.Lease,
                cancellationToken),
            PasswordResetRateLimitPolicy.SubmitName => PasswordResetRateLimitPolicy.WriteSubmitRejectionAsync(
                context.HttpContext,
                context.Lease,
                cancellationToken),
            AccessCodeRateLimitPolicy.GenerationName => AccessCodeRateLimitPolicy.WriteGenerationRejectionAsync(
                context.HttpContext,
                context.Lease,
                cancellationToken),
            AccessCodeRateLimitPolicy.VerificationName => AccessCodeRateLimitPolicy.WriteVerificationRejectionAsync(
                context.HttpContext,
                context.Lease,
                cancellationToken),
            ChangePasswordRateLimitPolicy.Name => ChangePasswordRateLimitPolicy.WriteRejectionAsync(
                context.HttpContext,
                context.Lease,
                cancellationToken),
            _ => QrApproveRateLimitPolicy.WriteRejectionAsync(
                context.HttpContext,
                context.Lease,
                cancellationToken)
        };
    };
});

// Validators are discovered from the Application assembly. Every module adds its own assembly here.
builder.Services.AddValidatorsFromAssemblyContaining<SampleRequestValidator>();

// DbContext + Identity + Roles + JWT (all registered in Infrastructure/Extensions)
builder.Services.AddInfrastructure(builder.Configuration, builder.Environment);

// NFR-REL-06: /health reports the API itself and the database connectivity, and nothing else.
// The database check is a plain IHealthCheck over the existing DbContext, so no package is added (ADR-0013).
builder.Services.AddHealthChecks()
    .AddCheck(HealthCheckNames.Api, () => HealthCheckResult.Healthy(), tags: ["api"])
    .AddCheck<DatabaseHealthCheck>(HealthCheckNames.Database, tags: ["db"], timeout: TimeSpan.FromSeconds(5));

// Swashbuckle is the single Swagger document generator and UI (ADR-0012).
builder.Services.AddSwaggerDocumentation();
var app = builder.Build();

// The proxy contract must apply before anything reads the scheme or the client address: HTTPS redirection, HSTS,
// the IP-keyed rate limits and the request log all depend on it (NFR-SEC-07).
app.UseForwardedHeaders();

// Registered before the first middleware that can start a response; the headers themselves are written from
// OnStarting, so the rate limiter's 429 and the exception handler's 500 keep them (NFR-SEC-07).
app.UseMiddleware<SecurityHeadersMiddleware>();

// The correlation id must exist before the first log line, including the exception handler's (NFR-REL-02).
app.UseMiddleware<CorrelationIdMiddleware>();
// Must run before every endpoint so a downstream failure goes through ApiExceptionHandler (ADR-0010).
app.UseExceptionHandler();
await app.SeedDefaultRolesAsync();
// Run the seeders in development only.
if (app.Environment.IsDevelopment())
{
    using var scope = app.Services.CreateScope();
    await scope.ServiceProvider.GetRequiredService<DatabaseSeeder>().RunAsync();
}

// Configure the HTTP request pipeline.
// The Swagger document and UI are development tools. They are served in Development only, unless a deployment
// explicitly opts in with OpenApi:ExposeOutsideDevelopment (ADR-0012).
var exposeApiReference = app.Environment.IsDevelopment()
    || app.Configuration.GetValue<bool>("OpenApi:ExposeOutsideDevelopment");

if (exposeApiReference)
{
    app.UseSwagger();
    // The static Swagger helper page is exposed exactly when the API reference is (ADR-0012, MP-97).
    app.UseStaticFiles();
    app.UseSwaggerUI(options =>
    {
        options.HeadContent = "<script src=\"/swagger-visit-access-tool.js\" defer></script>";
    });
}

// HSTS is only meaningful over HTTPS; Development keeps plain HTTP for the local tools and the phone camera helper.
if (!app.Environment.IsDevelopment())
{
    app.UseHsts();
}

// A probe has no HTTPS client, so /health answers its status instead of a 307 (ADR-0013).
app.UseWhen(
    context => !context.Request.Path.StartsWithSegments("/health"),
    branch => branch.UseHttpsRedirection());

// The CORS policy answers a preflight before authentication and rate limiting, so OPTIONS never spends a login
// budget and a foreign origin never reaches an action.
app.UseCors(CorsSettings.PolicyName);

// Add authentication and authorization middleware
app.UseAuthentication();
app.UseAuthorization();

// The email-keyed policies (login, password reset start and completion) need the email from the body, which the
// limiter itself cannot see (ADR-0007 decision 2). The limiter still runs after authentication so the authenticated
// partitions see the claims (ADR-0007 decisions 2 and 4).
app.UseMiddleware<RateLimitEmailKeyMiddleware>();
app.UseRateLimiter();

// Anonymous on purpose: a probe has no token, and the body carries statuses only (NFR-REL-06, FR-PRV-08).
// It stays out of the Swagger document: it is operational surface, not a product contract (ADR-0013).
// The response writer owns the status code (Healthy 200, anything else 503), so no ResultStatusCodes override is
// configured here: a second mapping would be dead code today and could disagree with the writer.
app.MapHealthChecks("/health", new HealthCheckOptions
{
    ResponseWriter = HealthResponseWriter.WriteAsync
}).AllowAnonymous();

app.MapControllers();

app.Run();

/// <summary>
/// The API entry point, made public so <c>WebApplicationFactory&lt;Program&gt;</c> can boot the real pipeline in the
/// HTTP integration tests (ADR-0006).
/// </summary>
public partial class Program
{
}
