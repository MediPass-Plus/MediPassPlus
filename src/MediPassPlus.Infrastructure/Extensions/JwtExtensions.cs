using System;
using System.Collections.Generic;
using System.Text;
using MediPassPlus.Infrastructure.Auth;
using Microsoft.AspNetCore.Authentication.JwtBearer;
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
                    IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwt.SecretKey)),
                    ValidateLifetime = true,
                    ClockSkew = TimeSpan.Zero
                };
            });

        services.AddAuthorization();

        return services;
    }
}
