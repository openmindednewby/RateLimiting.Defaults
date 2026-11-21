# Changelog

## 1.3.0

- The global limiter never limits a request whose `Connection.LocalPort` is the management port (`RateLimiting:ManagementPort`, default 8081), so kubelet probes and Prometheus scrapes never get 429 under user load (D-SVCDEF-36). Keyed on the local port, never the Host header.

## 1.2.0

- `PublicWrite` policy (`RateLimitPolicies.PublicWrite`): anonymous writes that create something, partitioned by client IP, 5 per 24 h in 24 hourly segments (Retry-After 3600 s). Configurable under `RateLimiting:PublicWrite`.
- `PublicWrite` keys IPv4-mapped IPv6 on the IPv4 address and IPv6 on its /64 prefix (`RateLimiting:PublicWriteIpv6PrefixLength`, default 64); a request with no client IP shares one `unknown` bucket.
- `AddClientIpForwarding()`: trusts `X-Forwarded-For` only from configured proxy networks, so per-IP policies key on the real client IP behind a reverse proxy.
