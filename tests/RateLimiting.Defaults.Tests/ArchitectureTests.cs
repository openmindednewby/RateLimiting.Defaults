namespace RateLimiting.Defaults.Tests;

/// <summary>
/// Architecture tests that verify all backend services have rate limiting configured.
/// These tests scan Program.cs files across all services to ensure no service
/// skips rate limiting registration.
/// </summary>
public class ArchitectureTests
{
    private const string SaasRoot = @"C:\desktopContents\projects\SaaS";

    /// <summary>
    /// All backend services that must register rate limiting.
    /// Maps service name to its Program.cs path relative to SaaS root.
    /// </summary>
    private static readonly Dictionary<string, string> ServiceProgramFiles = new()
    {
        ["TenantService"] = @"TenantService\src\TenantService.API",
        ["QuestionerService"] = @"QuestionerService\Questioner\src\Questioner.Web",
        ["OnlineMenuService"] = @"OnlineMenuSaaS\OnlineMenuService\OnlineMenu\src\OnlineMenu.Web",
        ["ContentService"] = @"ContentService\Content\src\Content.Web",
        ["NotificationService"] = @"NotificationService\Notification\src\Notification.Web",
    };

    [Theory]
    [MemberData(nameof(GetServiceNames))]
    public void Service_MustCallAddRateLimitingDefaults(string serviceName)
    {
        var servicePath = ServiceProgramFiles[serviceName];
        var serviceDir = Path.Combine(SaasRoot, servicePath);

        // Search all .cs files in the service for AddRateLimitingDefaults
        var csFiles = Directory.GetFiles(serviceDir, "*.cs", SearchOption.TopDirectoryOnly);
        var hasRegistration = csFiles.Any(file =>
        {
            var content = File.ReadAllText(file);
            return content.Contains("AddRateLimitingDefaults");
        });

        Assert.True(hasRegistration,
            $"Service '{serviceName}' does not call AddRateLimitingDefaults(). " +
            $"Add 'builder.AddRateLimitingDefaults();' to Program.cs or ProgramExtensions.cs. " +
            $"Searched in: {serviceDir}");
    }

    [Theory]
    [MemberData(nameof(GetServiceNames))]
    public void Service_MustCallUseRateLimiter(string serviceName)
    {
        var servicePath = ServiceProgramFiles[serviceName];
        var serviceDir = Path.Combine(SaasRoot, servicePath);

        var csFiles = Directory.GetFiles(serviceDir, "*.cs", SearchOption.TopDirectoryOnly);
        var hasMiddleware = csFiles.Any(file =>
        {
            var content = File.ReadAllText(file);
            return content.Contains("UseRateLimiter");
        });

        Assert.True(hasMiddleware,
            $"Service '{serviceName}' does not call UseRateLimiter(). " +
            $"Add 'app.UseRateLimiter();' after UseAuthorization() in Program.cs. " +
            $"Searched in: {serviceDir}");
    }

    [Theory]
    [MemberData(nameof(GetServiceNames))]
    public void Service_MustHaveRateLimitingPackageReference(string serviceName)
    {
        var servicePath = ServiceProgramFiles[serviceName];
        var serviceDir = Path.Combine(SaasRoot, servicePath);

        var csprojFiles = Directory.GetFiles(serviceDir, "*.csproj");
        var hasPackageRef = csprojFiles.Any(file =>
        {
            var content = File.ReadAllText(file);
            return content.Contains("RateLimiting.Defaults");
        });

        Assert.True(hasPackageRef,
            $"Service '{serviceName}' does not reference the RateLimiting.Defaults package. " +
            $"Add '<PackageReference Include=\"RateLimiting.Defaults\" />' to the .csproj file.");
    }

    public static IEnumerable<object[]> GetServiceNames()
    {
        return ServiceProgramFiles.Keys.Select(name => new object[] { name });
    }
}
