using Microservices.Common.Context;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.Options;

namespace Microservices.WebCore.Security;

/// <summary>
/// Enforces permission-based access control.
/// Usage: [HasPermission("products.create")]
/// </summary>
[AttributeUsage(AttributeTargets.Method | AttributeTargets.Class, AllowMultiple = true)]
public class HasPermissionAttribute : AuthorizeAttribute
{
    public const string PolicyPrefix = "APP_PERMISSION_";

    public string Permission { get; }

    public HasPermissionAttribute(string permission)
        : base(policy: $"{PolicyPrefix}{permission}")
    {
        Permission = permission;
    }
}

public record PermissionRequirement(string Permission) : IAuthorizationRequirement;

public class PermissionAuthorizationHandler(ICurrentUser currentUser) : AuthorizationHandler<PermissionRequirement>
{
    protected override Task HandleRequirementAsync(
        AuthorizationHandlerContext context,
        PermissionRequirement requirement)
    {
        if (currentUser.IsAuthenticated && currentUser.HasPermission(requirement.Permission))
        {
            context.Succeed(requirement);
        }

        return Task.CompletedTask;
    }
}

public class PermissionPolicyProvider(IOptions<AuthorizationOptions> options) : DefaultAuthorizationPolicyProvider(options)
{
    private static readonly System.Collections.Concurrent.ConcurrentDictionary<string, AuthorizationPolicy> PolicyCache = new();

    public override async Task<AuthorizationPolicy?> GetPolicyAsync(string policyName)
    {
        if (policyName.StartsWith(HasPermissionAttribute.PolicyPrefix, StringComparison.OrdinalIgnoreCase))
        {
            return PolicyCache.GetOrAdd(policyName, static name =>
            {
                var permission = name[HasPermissionAttribute.PolicyPrefix.Length..];
                return new AuthorizationPolicyBuilder()
                    .RequireAuthenticatedUser()
                    .AddRequirements(new PermissionRequirement(permission))
                    .Build();
            });
        }

        return await base.GetPolicyAsync(policyName);
    }
}
