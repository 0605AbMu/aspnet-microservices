using System.Text;
using Microservices.Common.Constants;
using Microservices.Common.Options;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.Tokens;

namespace Microservices.WebCore.Extensions;

public static class JwksExtensions
{
    /// <summary>
    /// Configures JWT Bearer authentication.
    /// Supports asymmetric keys via JWKS endpoint or standard configuration.
    /// </summary>
    public static IServiceCollection AddMicroserviceJwtAuth(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.Configure<IdentityOptions>(configuration.GetSection(ConfigSections.Identity));
        var identityOptions = configuration.GetSection(ConfigSections.Identity).Get<IdentityOptions>()
            ?? new IdentityOptions();

        var secret = configuration["Jwt:Secret"] ?? configuration["Identity:Secret"];

        services
            .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
            .AddJwtBearer(options =>
            {
                options.RequireHttpsMetadata = false;

                options.TokenValidationParameters = new TokenValidationParameters
                {
                    ValidateIssuer = !string.IsNullOrWhiteSpace(identityOptions.Issuer),
                    ValidIssuer = identityOptions.Issuer,

                    ValidateAudience = !string.IsNullOrWhiteSpace(identityOptions.Audience),
                    ValidAudience = identityOptions.Audience,

                    ValidateLifetime = true,
                    ClockSkew = TimeSpan.FromSeconds(30),

                    ValidateIssuerSigningKey = true
                };

                if (!string.IsNullOrWhiteSpace(identityOptions.Issuer))
                {
                    options.Authority = identityOptions.Issuer;
                }

                // If explicit metadata / JWKS endpoint is configured
                if (!string.IsNullOrWhiteSpace(identityOptions.JwksUri))
                {
                    options.MetadataAddress = identityOptions.JwksUri;
                }
                else if (!string.IsNullOrWhiteSpace(secret))
                {
                    options.TokenValidationParameters.IssuerSigningKey =
                        new SymmetricSecurityKey(Encoding.UTF8.GetBytes(secret));
                }

                options.Events = new JwtBearerEvents
                {
                    OnAuthenticationFailed = _ => Task.CompletedTask,
                    OnTokenValidated = ctx =>
                    {
                        ctx.HttpContext.ResolveCorrelationId();
                        return Task.CompletedTask;
                    }
                };
            });

        services.AddAuthorization();

        return services;
    }
}
