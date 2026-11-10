# Changelog

## 1.2.0

- `PublicWrite` policy (`RateLimitPolicies.PublicWrite`): anonymous writes that create something, partitioned by client IP, 5 per 24 h in 24 hourly segments (Retry-After 3600 s). Configurable under `RateLimiting:PublicWrite`.
- `AddClientIpForwarding()`: trusts `X-Forwarded-For` only from configured proxy networks, so per-IP policies key on the real client IP behind a reverse proxy.
