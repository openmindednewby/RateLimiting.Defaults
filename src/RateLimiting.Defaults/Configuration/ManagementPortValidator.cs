namespace RateLimiting.Defaults.Configuration;

internal static class ManagementPortValidator
{
    internal static void Validate(RateLimitingDefaultsOptions options)
    {
        if (options.ManagementPort <= 0)
        {
            throw new InvalidOperationException(
                $"RateLimiting:ManagementPort must be greater than 0 (was {options.ManagementPort}).");
        }

        if (options.ManagementPort == options.PublicPort)
        {
            throw new InvalidOperationException(
                $"RateLimiting:ManagementPort must differ from RateLimiting:PublicPort ({options.PublicPort}), " +
                "otherwise /health and /metrics on the public port skip the global limiter.");
        }
    }
}
