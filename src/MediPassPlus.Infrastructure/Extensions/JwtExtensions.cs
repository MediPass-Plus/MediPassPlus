using System;
using System.Collections.Generic;
using System.Text;
using MediPassPlus.Infrastructure.Auth;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.Tokens;

namespace MediPassPlus.Infrastructure.Extensions;

internal static class JwtExtensions
{                              // JWT settings + Authentication          
    internal static IServiceCollection AddJwtAuthentication(
        this IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<JwtSettings>(configuration.GetSection(JwtSettings.SectionName));

        var jwt = configuration.GetSection(JwtSettings.SectionName).Get<JwtSettings>()
                  ?? throw new InvalidOperationException("Missing 'Jwt' section in configuration.");

        if (string.IsNullOrWhiteSpace(jwt.SecretKey) || jwt.SecretKey.Length < 32)
            throw new InvalidOperationException("Jwt:SecretKey is missing or shorter than 32 characters.");

        // A non-positive lifetime would make RefreshToken.Issue throw on the first login and answer 500, so the
        // misconfiguration is refused at startup instead (FR-ACC-06).
        if (jwt.AccessTokenMinutes <= 0)
            throw new InvalidOperationException("Jwt:AccessTokenMinutes must be greater than zero.");

        if (jwt.RefreshTokenDays <= 0)
            throw new InvalidOperationException("Jwt:RefreshTokenDays must be greater than zero.");

        if (jwt.SessionDays <= 0)
            throw new InvalidOperationException("Jwt:SessionDays must be greater than zero.");

        if (jwt.RefreshTokenDays > jwt.SessionDays)
            throw new InvalidOperationException("Jwt:RefreshTokenDays cannot be longer than Jwt:SessionDays.");

        services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
            .AddJwtBearer(options =>
            {
                options.TokenValidationParameters = new TokenValidationParameters
                {
                    ValidateIssuer = true,
                    ValidIssuer = jwt.Issuer,
                    ValidateAudience = true,
                    ValidAudience = jwt.Audience,
                    ValidateIssuerSigningKey = true,
                    IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwt.SecretKey!)),
                    ValidateLifetime = true,
                    ClockSkew = TimeSpan.Zero
                };
                options.Events = new JwtBearerEvents
{
    OnChallenge = async context =>
    {
        context.HandleResponse();
        context.Response.StatusCode = StatusCodes.Status401Unauthorized;
        context.Response.ContentType = "application/problem+json";
        await context.Response.WriteAsJsonAsync(
            new
            {
                type = "https://tools.ietf.org/html/rfc9110#section-15.5.2",
                title = "Authentication required.",
                status = StatusCodes.Status401Unauthorized,
                detail = "A valid access token is required."
            },
            options: null,
            contentType: "application/problem+json");
    },
    OnForbidden = async context =>
    {
        context.Response.StatusCode = StatusCodes.Status403Forbidden;
        context.Response.ContentType = "application/problem+json";
        await context.Response.WriteAsJsonAsync(
            new
            {
                type = "https://tools.ietf.org/html/rfc9110#section-15.5.4",
                title = "You cannot access this resource.",
                status = StatusCodes.Status403Forbidden,
                detail = "Your role is not allowed to use this endpoint."
            },
            options: null,
            contentType: "application/problem+json");
    }
};
            });

        services.AddAuthorization();

        return services;
    }
}
