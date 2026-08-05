namespace SharpAccess.SampleApi;

internal static class SampleCorsConfiguration
{
    public const string PolicyName = "SampleCors";

    public static string[] Register(WebApplicationBuilder builder)
    {
        string[] configuredOrigins = builder.Configuration.GetSection("Cors:AllowedOrigins").Get<string[]>() ?? [];
        List<string> validatedOrigins = [];
        foreach (string configuredOrigin in configuredOrigins)
        {
            if (!Uri.TryCreate(configuredOrigin, UriKind.Absolute, out Uri? origin)
                || (origin.Scheme != Uri.UriSchemeHttp && origin.Scheme != Uri.UriSchemeHttps)
                || !string.IsNullOrEmpty(origin.UserInfo)
                || !string.IsNullOrEmpty(origin.Query)
                || !string.IsNullOrEmpty(origin.Fragment)
                || origin.AbsolutePath != "/"
                || (!origin.IsLoopback && origin.Scheme != Uri.UriSchemeHttps))
            {
                throw new InvalidOperationException(
                    "Each Cors:AllowedOrigins entry must be an HTTP(S) origin without credentials, a path, query, or fragment; non-loopback origins must use HTTPS.");
            }

            validatedOrigins.Add(origin.GetLeftPart(UriPartial.Authority));
        }

        string[] allowedOrigins = validatedOrigins.Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        builder.Services.AddCors(cors =>
        {
            cors.AddPolicy(PolicyName, policy =>
            {
                if (allowedOrigins.Length > 0)
                {
                    policy.WithOrigins(allowedOrigins).AllowAnyHeader().AllowAnyMethod();
                }
            });
        });
        return allowedOrigins;
    }
}
