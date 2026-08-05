# Vendored SBOM schemas

These schemas are vendored so release evidence is validated offline against reviewed standards instead of a mutable network response.

| File | Tagged upstream source | SHA-256 |
|---|---|---|
| `cyclonedx/bom-1.6.schema.json` | `https://raw.githubusercontent.com/CycloneDX/specification/1.6/schema/bom-1.6.schema.json` | `3E92DDDBC30CF7F6A02B80F0942B1A4CFD4FB1C26F1DFC4310AFA9D613CAFB93` |
| `cyclonedx/jsf-0.82.schema.json` | `https://raw.githubusercontent.com/CycloneDX/specification/1.6/schema/jsf-0.82.schema.json` | `8BAE002C25E723DB7EE1F26AFDE680AE1A2B1A8F6B4B4B0FD65DC3BECB090AAE` |
| `cyclonedx/spdx.schema.json` | `https://raw.githubusercontent.com/CycloneDX/specification/1.6/schema/spdx.schema.json` | `BAA9D3BD1ED57B6751B0887EDEAD6B5063FF53FF7429CF85D476C6C94AF0166E` |
| `spdx/spdx-2.3.schema.json` | `https://raw.githubusercontent.com/spdx/spdx-spec/v2.3/schemas/spdx-schema.json` | `3EC6CD5B8BA0C9A3E821DA48536FA1B814567DC7E4376EFE98D3E7B2A7A8D230` |

The SPDX source has one canonical terminal LF added for repository text handling; its tagged upstream byte hash before that normalization is `239208B7AC287B3CF5D9A9AF23F9D69863971102A5E1587A27A398B43490B89B`. Updating a schema requires a deliberate repository diff, an updated vendored hash in this file and its package test, and a successful fixed-input reproducibility run through both SBOM wrappers.
