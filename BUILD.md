# Build & Publish

## Prerequisites

- .NET 10 SDK
- NuGet.org API key (for publishing)

## Build Locally

```bash
cd src/RateLimiting.Defaults
dotnet restore
dotnet build -c Release
```

## Run Tests

```bash
# No test project yet — add RateLimiting.Defaults.Tests when needed
```

## Publish to NuGet.org

```powershell
.\publish.ps1 -Bump patch -ApiKey YOUR_API_KEY
```

Bump options: `patch` (1.0.0 → 1.0.1), `minor` (1.0.0 → 1.1.0), `major` (1.0.0 → 2.0.0).

The script automatically:
1. Reads the current version from `Directory.Build.props`
2. Bumps the version
3. Restores, builds, packs
4. Pushes to NuGet.org
5. Rolls back the version bump on failure

## Publish All Packages

From the `NuGetPackages/` root:

```powershell
.\publish-all.ps1 -Bump patch -ApiKey YOUR_API_KEY
```

This publishes all packages in dependency order.
