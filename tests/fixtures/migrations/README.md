# Migration fixtures

Provider fixture directories lock already-published migration sources and identifiers. Historical migration SQL must not be edited in place. Corrections are additive migrations with new identifiers.

The SQLite lock records Git blob identifiers from the Phase 3 base commit. `validate-phase-4.ps1` verifies those blobs before building or testing the modified tree.
