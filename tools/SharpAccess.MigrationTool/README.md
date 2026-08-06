# SharpAccess migration tool

This repository tool provides `migrate`, `validate`, `status`, and `script` commands for the promoted SQLite provider. It uses the same public migration APIs as an application host and never logs the supplied connection string.

Run it through the PowerShell wrapper in `scripts/` so repository-root resolution and exit handling remain consistent with the Windows-only repository toolchain.
