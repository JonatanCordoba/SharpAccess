# Breached-password validation

`IPasswordRiskValidator` remains replaceable. The built-in composite first applies local common/account-derived checks and optionally performs a k-anonymity range query.

```csharp
options.Passwords.BreachedPasswords.Enabled = true;
options.Passwords.BreachedPasswords.Timeout = TimeSpan.FromSeconds(2);
options.Passwords.BreachedPasswords.FailureMode = BreachedPasswordFailureMode.FailClosed;
options.Passwords.BreachedPasswords.MaximumCacheEntries = 2048;
```

SharpAccess computes a SHA-1 digest locally only because the range protocol requires it, sends the first five hexadecimal characters, and compares suffixes locally. The candidate password and complete digest are never sent. The client uses a bounded timeout, bounded cache, and a circuit breaker. Fail-open preserves availability during an upstream outage; fail-closed preserves the breach check but can block registration and password changes. Choose deliberately and monitor upstream health outside request logs.

Hosts may replace `IPasswordRiskValidator` with an offline corpus, commercial service, or deterministic test fake. Implementations must not log password material or include it in telemetry tags.
