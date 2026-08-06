# ADR 0004: Target .NET 10 only for stable 1.0

## Status

Accepted on 2026-07-12.

## Context

Multi-targeting would multiply build, analyzer, package, runtime, security, and provider-validation combinations before the package family has reached its first stable release.

## Decision

SharpAccess stable `1.0.0` targets `net10.0` only. All package projects, samples, tests, tools, package-consumer checks, and release workflows must use the repository's approved .NET 10 SDK policy.

## Consequences

- The initial compatibility and validation matrix remains bounded.
- Consumers requiring older target frameworks are outside the stable 1.0 support contract.
- A future target-framework expansion requires a new compatibility decision and complete validation evidence.

## Guardrails

- `Directory.Build.props` remains the target-framework source of truth unless a project has an explicitly documented exception.
- CI and release verification must validate the approved Windows .NET 10 environment with PowerShell 7.
- Documentation and package metadata must not imply support for another target framework.
