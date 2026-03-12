# RateLimiting.Defaults

Production-ready rate limiting defaults for ASP.NET Core services. Pre-configured sliding window policies for common scenarios. One-line adoption.

## Installation

```bash
dotnet add package RateLimiting.Defaults
```

## Quick Start

```csharp
using RateLimiting.Defaults.Extensions;

// In Program.cs or ProgramExtensions.cs:
builder.AddRateLimitingDefaults();

var app = builder.Build();

// Add middleware after UseAuthorization(), before endpoints
app.UseAuthentication();
app.UseAuthorization();
app.UseRateLimiter();  // <-- rate limiting middleware
```

## Pre-Configured Policies

| Policy | Default Limit | Partition Key | Use Case |
|--------|--------------|---------------|----------|
| **Api** (global) | 100/min | User ID or IP | Default for all endpoints |
| **Auth** | 5/min | IP only | Login, OTP, password reset |
| **Public** | 300/min | IP only | Public-facing pages |
| **Upload** | 10/min | User ID or IP | File uploads |

The **Api** policy is applied globally to all requests. Other policies can be applied to specific endpoints.

## Applying Policies to Endpoints

### FastEndpoints

```csharp
using RateLimiting.Defaults.Policies;

public override void Configure()
{
    Post("/api/auth/login");
    AllowAnonymous();
    Options(x => x.RequireRateLimiting(RateLimitPolicies.Auth));
}
```

### Minimal APIs

```csharp
app.MapPost("/api/upload", HandleUpload)
   .RequireRateLimiting(RateLimitPolicies.Upload);
```

### Controllers

```csharp
[EnableRateLimiting(RateLimitPolicies.Auth)]
[HttpPost("login")]
public async Task<IActionResult> Login(LoginRequest request) { ... }
```

## Configuration via appsettings.json

Override defaults per-service:

```json
{
  "RateLimiting": {
    "Auth": { "PermitLimit": 10, "WindowSeconds": 60 },
    "Api": { "PermitLimit": 200, "WindowSeconds": 60 },
    "Public": { "PermitLimit": 500, "WindowSeconds": 60 },
    "Upload": { "PermitLimit": 20, "WindowSeconds": 60 }
  }
}
```

## Configuration via Code

```csharp
builder.AddRateLimitingDefaults(opts =>
{
    opts.Auth.PermitLimit = 3;  // Extra strict for this service
    opts.Api.PermitLimit = 200; // Higher limit for this service
});
```

## Rejection Response

When a client exceeds the rate limit, they receive:

- **HTTP 429 Too Many Requests**
- `Retry-After` header (seconds)
- JSON body:

```json
{
  "status": 429,
  "title": "Too Many Requests",
  "detail": "Rate limit exceeded. Please try again later.",
  "retryAfterSeconds": 15
}
```

## Architecture

- Uses ASP.NET Core built-in `Microsoft.AspNetCore.RateLimiting` (no external dependencies)
- Sliding window algorithm for smooth request distribution
- In-memory rate limiter (per-instance) — suitable for single-instance and development
- Partition keys: authenticated users by `sub` claim (user ID), anonymous by IP

## Future: Redis-Backed Distributed Limiting

When scaling to multiple replicas, swap to Redis-backed rate limiting by creating a custom `IRateLimiterPolicy` that uses `StackExchange.Redis`. The policy names and configuration remain the same.
