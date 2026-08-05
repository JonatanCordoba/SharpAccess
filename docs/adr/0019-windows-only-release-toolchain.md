# ADR 0019: Use a Windows-only PowerShell release toolchain

## Status

Accepted on 2026-07-25.

## Context

SharpAccess previously maintained PowerShell and Bash implementations of repository verification, packaging, coverage, provider evidence, recovery, and release orchestration. The duplicated implementations increased review surface, drift risk, and maintenance cost. Optional Docker and Compose environments also duplicated host-native capabilities and were not part of the intended maintainer workflow.

The supported package and release environment is Windows. PowerShell 7 runs consistently on supported Windows developer machines and GitHub-hosted Windows runners.

## Decision

SharpAccess engineering, CI, evidence generation, release preparation, and supported deployment are Windows-only.

Repository automation uses PowerShell 7 as the single implementation language. Bash scripts, shell-parity requirements, Dockerfiles, Compose files, service containers, and local container orchestration are prohibited.

PostgreSQL validation uses either:

- a native Windows PostgreSQL installation with native client tools; or
- an approved managed scratch database exposed through protected configuration.

## Consequences

Positive:

- one reviewable automation implementation;
- lower drift and maintenance risk;
- simpler CI and release evidence;
- no local container runtime requirement;
- clearer supported-platform contract.

Trade-offs:

- Linux and macOS engineering and deployment are unsupported;
- contributors must use Windows and PowerShell 7;
- PostgreSQL evidence requires native tooling or approved managed infrastructure;
- a future cross-platform expansion requires a new decision and complete platform evidence.

## Guardrails

- `scripts/verify-structure.ps1` rejects Bash, container topology, and non-Windows workflows.
- GitHub Actions jobs use `windows-latest`.
- PowerShell scripts require PowerShell 7 and strict mode.
- Documentation and package claims must not imply Linux or macOS support.
- Removing container tooling does not remove real-engine PostgreSQL evidence requirements.
