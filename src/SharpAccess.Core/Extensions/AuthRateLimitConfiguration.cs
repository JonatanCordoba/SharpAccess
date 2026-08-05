using System.Threading.RateLimiting;
using SharpAccess;
using SharpAccess.Configuration;
using SharpAccess.Endpoints;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Options;

namespace Microsoft.Extensions.DependencyInjection;

internal static class AuthRateLimitConfiguration
{
    internal const string NormalizedAccountItem = "SharpAccess.RateLimit.NormalizedAccount";
    internal const string CategoryItem = "SharpAccess.RateLimit.Category";

    internal static void ConfigureRateLimiter(RateLimiterOptions rateLimiter)
    {
        rateLimiter.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
        rateLimiter.OnRejected = static async (context, cancellationToken) =>
        {
            context.HttpContext.Response.StatusCode = StatusCodes.Status429TooManyRequests;
            await AuthProblemDetailsWriter.WriteProblemAsync(
                    context.HttpContext.Response,
                    new ProblemDetails
                    {
                        Status = StatusCodes.Status429TooManyRequests,
                        Title = "Too many requests.",
                        Type = "https://httpstatuses.com/429"
                    },
                    cancellationToken)
                .ConfigureAwait(false);
        };

        AddPolicy(rateLimiter, AuthEndpointMapper.LoginRateLimit, static options => options.LoginPerMinute);
        AddPolicy(rateLimiter, AuthEndpointMapper.RegisterRateLimit, static options => options.RegisterPerMinute);
        AddPolicy(rateLimiter, AuthEndpointMapper.RefreshRateLimit, static options => options.RefreshPerMinute);
        AddPolicy(rateLimiter, AuthEndpointMapper.PasswordResetRateLimit, static options => options.PasswordResetPerMinute);
        AddPolicy(rateLimiter, AuthEndpointMapper.VerificationRateLimit, static options => options.EmailVerificationPerMinute);
        AddPolicy(rateLimiter, AuthEndpointMapper.OAuthRateLimit, static options => options.OAuthPerMinute);
    }

    private static void AddPolicy(
        RateLimiterOptions rateLimiter,
        string policyName,
        Func<AuthRateLimitOptions, int> selectLimit)
    {
        rateLimiter.AddPolicy(policyName, context =>
        {
            AuthOptions options = context.RequestServices.GetRequiredService<IOptions<AuthOptions>>().Value;
            IAuthRateLimitPartitionKeyProvider partitions =
                context.RequestServices.GetRequiredService<IAuthRateLimitPartitionKeyProvider>();
            string? normalizedAccount = context.Items.TryGetValue(NormalizedAccountItem, out object? account)
                ? account as string
                : null;
            string? category = context.Items.TryGetValue(CategoryItem, out object? value)
                ? value as string
                : policyName;
            string key = partitions.CreatePartitionKey(context, policyName, normalizedAccount, category);
            int permitLimit = selectLimit(options.RateLimits);
            return RateLimitPartition.GetFixedWindowLimiter(
                key,
                _ => new FixedWindowRateLimiterOptions
                {
                    PermitLimit = permitLimit,
                    Window = TimeSpan.FromMinutes(1),
                    QueueLimit = 0,
                    AutoReplenishment = true
                });
        });
    }
}
