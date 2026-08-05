# Versioning policy

SharpAccess follows semantic versioning for its package family.

## Authoritative version

`eng/Version.props` owns the synchronized version for `SharpAccess.Core`, `SharpAccess.Sqlite`, and `SharpAccess.Postgres`. Package projects must not declare independent `<Version>` values.

## Development and release-candidate versions

The first public release candidate is `0.9.0-rc.1`, with signed tag `v0.9.0-rc.1`. Later candidates increment the prerelease ordinal (`0.9.0-rc.2`, and so on) unless a reviewed compatibility decision changes the base version.

`JonatanCordoba/dotnet-auth` is the private development-history repository. It may create prerelease evidence artifacts, but public NuGet release-candidate and stable packages are published only from the exact verified clean root in `JonatanCordoba/SharpAccess`.

Stable `1.0.0` is a separate post-RC release. The build rejects a stable package unless an explicit release-only override and the exact canonical GitHub repository identity are both present.

## Additive changes

Additive changes introduce new APIs without removing or changing existing stable behavior. They normally require a minor version after 1.0. During the pre-1.0 period they still require public API review, compatibility evidence, and release notes.

## Behavioral changes

Behavioral changes preserve signatures but alter documented behavior. Security corrections may ship in a patch release when they restore the documented contract. Material behavior changes require clear release notes and compatibility tests.

## Breaking changes

Breaking changes remove or rename public APIs, change required configuration, alter persisted contracts, or invalidate documented consumer behavior. After 1.0 they require a major version unless a security advisory requires an exceptional response.

## Public API evidence

The `eng/public-api` baseline files and package-surface tests define the reviewed exported type surface. Every public API change must update the relevant baseline and include a compatibility assessment.
