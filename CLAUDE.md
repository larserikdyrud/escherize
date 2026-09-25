# Escherize

- Spesifikasjonen er SPEC.md; empiriske valg står i DECISIONS.md.
- Jobb fase for fase (SPEC §10). Kjør ./build.ps1 før hver commit.
- Endre aldri toleranser eller tester for å få grønt – stopp og spør.
- Escherize.Core skal kun bruke BCL. Nye pakker krever godkjenning.
- Hot path (SPEC §6.3): ingen allokering, ingen LINQ.
- Target: net8.0 (Visual Studio 2022).
