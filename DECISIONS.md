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

### 2026-09-25 - F1: preprocessing

Choices the specification leaves open, all local to the import path:

- **Marching squares ambiguity (SPEC §4.1).** Cases 5 and 10 (two diagonally opposite
  foreground corners) are resolved so that the foreground stays connected, which matches
  the eight connectivity used when the largest component is selected. The background is
  therefore four connected, which is also what the hole filling flood fill assumes.
- **Exact contour linking.** On a binary mask the iso 0.5 crossing always lands on the
  midpoint of a cell edge, so every contour coordinate is a multiple of one half. Segment
  endpoints are keyed as integers at twice the grid resolution and linked exactly, with no
  distance tolerance.
- **Otsu histogram (SPEC §4.1).** Pixels with alpha below 128 count as background and are
  left out of the histogram, so a transparent border does not drag the threshold.
- **Diameter.** Computed exactly, as the largest distance between two convex hull
  vertices. Several tolerances are expressed as a fraction of it.
- **Ambiguous `.json` extension.** A `.json` file may hold either the polygon form of
  SPEC §4.2 or GeoJSON, so the first 512 characters are inspected to choose the reader.
  `.geojson` always goes to the GeoJSON reader.
- **Orientation after smoothing.** The Fourier filter can in principle fold a contour, so
  the positive orientation of SPEC §3 is re-established after smoothing as well as at
  import.
- **Unknown CLI flags are an error** rather than being ignored, so that a typo such as
  `--smoothe 8` cannot silently fall back to the default.

Measured against the SPEC §8.1 tolerances: the synthetic disc of radius 100 px and the
square smoothed with H = 8 both pass, as do the equal arc length and projection tests.
