# Escherize

- The specification is SPEC.md; empirical choices are in DECISIONS.md.
- Work phase by phase (SPEC §10). Run ./build.ps1 before every commit.
- Never change tolerances or tests to get green – stop and ask.
- Escherize.Core uses the BCL only. New packages need approval.
- Hot path (SPEC §6.3): no allocation, no LINQ.
- Target: net8.0 (Visual Studio 2022).
