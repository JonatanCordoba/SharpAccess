# Authentication rate limiting

SharpAccess local policies use `IAuthRateLimitPartitionKeyProvider`. Partition values contain the operation, observed client IP, and keyed truncated hashes for optional normalized-account and category inputs. Raw email addresses, provider subjects, token families, or other account identifiers are not used as partition keys.

The built-in limiter is process-local. In a multi-instance deployment it cannot provide a global budget. Replace or front it with a host-owned distributed limiter using the same partition-key provider. Configure ASP.NET Core forwarded headers only for known proxies and networks before authentication and rate-limiting middleware; otherwise the observed address may be the reverse proxy or may be spoofable.

Recommended partitions:

- login: client IP plus normalized-account keyed hash;
- registration: client IP;
- forgot password: client IP plus normalized-account keyed hash;
- verification: client IP plus purpose category;
- refresh: client IP plus a non-secret family category;
- OAuth/OIDC: client IP plus provider category.

Whenever any mapped rate-limited feature is enabled, `RateLimits.PartitionKey` is mandatory in every environment and must decode to at least 32 bytes. The mapped features are password login, registration, password reset and email verification, refresh tokens, and any enabled OpenID Connect provider. A configured-but-disabled provider does not activate the requirement.

The partition key is a dedicated secret: there is no fallback to JWT signing or token-hashing material, and validation rejects reuse of signing keys, token-hashing keys, password peppers, HMAC signing-ring keys, or OpenID Connect client secrets. Generate and rotate it independently. Host endpoint adapters can place normalized-account and category values into their distributed limiter before calling SharpAccess; never persist or emit the resulting partition value as a user identifier.

The values in `.env.example` and the sample appsettings are development placeholders. Production validation rejects sample, development, replacement, default, repeated, and otherwise predictable partition material; production hosts must supply independently generated random bytes through their secret manager.
