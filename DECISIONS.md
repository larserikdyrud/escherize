# DECISIONS

Empirical choices and findings that are deliberately not fixed by `SPEC.md`.
Every entry records what was decided, how it was established and when.

## Open decisions

| Id | Decision | Determined by | Status |
|---|---|---|---|
| D1 | Glide axis (X or Y) per G pair, for IH2, IH3, IH5 and IH6 | Template validity test, SPEC §8.2 | Open (phase F2) |
| D2 | Sign of the rotation angle θ, for IH7, IH21 and IH28 | Template validity test, SPEC §8.2 | Open (phase F2) |
| D3 | Degrees of freedom `md` of the vertex parametrisation per template | SPEC §5.3, asserted `md >= 3` | Open (phase F2) |

## Log

### 2026-09-25 — F0: solution scaffold

- Target framework is `net8.0` as required by SPEC §2. The machine has SDK 9.0.315 and no
  .NET 8 SDK, but the .NET 8 runtime (8.0.28) is installed, so `net8.0` builds and the tests
  run on the .NET 8 runtime. No `global.json` is pinned, because pinning to an SDK that is
  not installed would break the build.
- `Directory.Build.props` carries the settings SPEC §2 requires for every project, plus
  `GenerateDocumentationFile` (SPEC §0.7) and `EnforceCodeStyleInBuild`. Combined with
  `TreatWarningsAsErrors`, missing XML documentation on a public API fails the build.
- The CLI exposes `Program.WriteUsage` to the test project through `InternalsVisibleTo`
  rather than making it public, since the CLI has no public API surface.
