# DECISIONS

Empirical choices and findings that are deliberately not fixed by `SPEC.md`.
Every entry records what was decided, how it was established and when.

## Open decisions

| Id | Decision | Determined by | Status |
|---|---|---|---|
| D1 | Glide axis (X or Y) per G pair, for IH2, IH3, IH5 and IH6 | Template validity test, SPEC §8.2 | **Settled**, F2 |
| D2 | Sign of the rotation angle θ, for IH7, IH21 and IH28 | Template validity test, SPEC §8.2 | **Settled**, F2 |
| D3 | Degrees of freedom `md` of the vertex parametrisation per template | SPEC §5.3, asserted `md >= 3` | **Recorded**, F2 |

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

### 2026-09-25 - F2: templates, parametrisation, and decisions D1 to D3

The validation of SPEC §8.2 was run over every configuration of every template: all axis
combinations for the glide pairs and both rotation signs, 22 configurations in total.
A configuration passes only if it produces a simple tile whose neighbour isometries cover
its edges to 1e-9 and whose patch covers the sampling disc with every sample in exactly
one tile.

#### D1 - glide axes

| Template | Glide pairs | Passes | Fails |
|---|---|---|---|
| IH2 | B, C | XX, YY | XY, YX |
| IH3 | B, C | XX, YY | XY, YX |
| IH5 | B | X, Y | none; the two are equivalent |
| IH6 | A, B | XY, YX | XX, YY |

**Decision: IH2, IH3 and IH5 put every glide pair on the same axis (X). IH6 is the one
template whose two glide pairs need different axes (A on X, B on Y).**

Within a template the passing configurations are the same tiling turned a quarter turn,
so the choice between them is a convention. The choice *between the groups* is not:

- IH2 with mixed axes is degenerate. The corner conditions give edge vector E2 = F E1, and
  with F = diag(1,-1) forced against a second pair on the other axis the closure condition
  collapses to E1x = 0, hence E2 = -E1 and V3 = V1. No simple outline exists, and no draw
  in 50 attempts produced one.
- IH3 with mixed axes stays non-degenerate but does not tile: its outlines are simple and
  its edge coverage is exact, yet the patch piles 6 or 7 tiles on the same point.
- IH6 with matching axes likewise produces simple tiles whose neighbours overlap them.

The edge coverage check alone does not separate these: it holds to about 1e-17 for every
configuration, correct or not, because it only tests the relation the isometry was built
from. The covering test is what discriminates.

#### D2 - sign of theta

**Decision: every rotation angle is negative, so IH7 uses -120 degrees, IH21 uses -120 and
-60, and IH28 uses -90.**

Both alternatives were tried for IH7, IH21 and IH28; the positive sign never produced a
simple outline in 50 draws, and the negative sign tiles for all 20 seeds. This follows
from the counter-clockwise convention of SPEC §3: at a rotation centre with interior angle
alpha, the relation b(i) - V = R(theta) (a(k+1-i) - V) of SPEC §5.1 holds with
theta = -alpha. A counter-clockwise square, for instance, has interior angle 90 degrees at
a vertex and satisfies the relation with theta = -90.

#### D3 - vertex degrees of freedom

| Template | IH1 | IH2 | IH3 | IH4 | IH5 | IH6 | IH7 | IH21 | IH28 |
|---|---|---|---|---|---|---|---|---|---|
| nv | 6 | 6 | 6 | 6 | 6 | 6 | 6 | 5 | 5 |
| md | 8 | 7 | 7 | 10 | 8 | 8 | 6 | 6 | 6 |

Every value is at least 3 as SPEC §5.3 requires; two translations, a rotation and a scale
are always free. IH4 is the largest at 10 because its four C edges impose no condition on
the vertices at all, leaving only the single translation pair.

#### Two readings of SPEC §8.2 step 2, and which was taken

1. **The perturbation is scaled to a unit direction.** The step says
   `u = B(B^T w) + 0.05 B r` with r normally distributed. Taken literally, with
   r ~ N(0, I) over m columns, the nudge has norm 0.05 sqrt(m), which is about 0.32 at
   n = 36 against a tile of norm at most 1: a 32 per cent perturbation, not 5 per cent.
   Measured over 200 draws per configuration, that folds the outline so often that only
   4 to 14 draws in 200 stay simple, and the 50 attempt budget then fails a valid
   configuration about half the time. Scaling r to unit length makes the perturbation the
   5 per cent it reads as, and the same measurement gives 112 to 196 simple draws out of
   200 for valid configurations while the invalid ones stay at 0 to 30. Both readings pick
   the same winners; only the scaled one does so reliably, so that is what is implemented.
2. **A redraw is a whole redraw.** "Trekk på nytt ved selvkryssing" is taken to repeat
   steps 1 and 2 together, so each attempt draws a fresh k vector, goal and noise vector.
   Keeping the k vector fixed can make a trial hopeless, because a very lopsided k such as
   [2, 2, 26] puts 26 interior points on one edge and practically always folds.

Two further points where the implementation is deliberately stricter or more specific than
the text:

- **The fit uses the measure the template calls for.** SPEC §8.2 step 2 writes the plain
  projection B^T w, but for a glide template SPEC §6.1 says the right measure is
  Procrustes, with the rotation free. Since a glide template is pinned to an absolute
  orientation by its axes, the plain projection aligns the goal to those axes by accident
  and folds most draws. Fitting with `TileFit.Fit`, which picks the measure from the
  template, is what SPEC §6.1 prescribes and raises the success rate substantially.
- **The patch is grown to depth 5, not 3.** At depth 3 the patch of IH21 stops short of
  the sampling disc of radius 2 R around its six fold rotation centre, and the check then
  reports a gap that the tiling does not have. A deeper patch only adds tiles that must
  not overlap, so it makes the check stricter; the sampling disc keeps the radius the
  specification gives.

Only self intersection rejects a draw in §8.2, not orientation. A glide template naturally
produces a clockwise outline, and the search covers that by running on both W and W_rev
(SPEC §4.5). The positive orientation rule of SPEC §7.3 applies to search results, not to
the tiles generated here.

### 2026-09-25 - F3: evaluation, search and output

- **`seconds` in summary.json versus the determinism test.** SPEC §9.2 puts an elapsed
  time in `search.seconds`, and SPEC §8.7 requires that `--threads 1` and `--threads 8`
  produce a byte identical `summary.json`. Those cannot both hold: a run on one thread
  genuinely takes longer. The field is kept, because §9.2 asks for it, and the determinism
  test compares the two files byte for byte with that single line removed. Every other
  byte matches, including the candidate order, the tile coordinates and the isometries.
  Numbers are rounded to 9 decimals on the way out so that the comparison is not at the
  mercy of the last bit of a sum whose order depends on the partitioning.
- **The tile is rotated into the frame of the goal, and the isometries with it.** SPEC §6.4
  computes an optimal rotation theta for the overlay. The reconstructed tile is stored
  already turned by theta so that the drawings, the JSON coordinates and the DXF all line
  up with the silhouette. The neighbour isometries cannot simply be rebuilt from the
  rotated points, because a glide relation is stated against a fixed axis; they are
  conjugated by the same rotation instead, which keeps the patch exact. A test checks that
  every edge still maps onto its partner to 1e-9 after the rotation.
- **The diversity filter does not collapse the same tile at different offsets.** SPEC §7.3
  compares candidates point by point in goal index order, so two candidates that fit the
  same silhouette at different start offsets are compared point against point rather than
  shape against shape, and both survive. That is what the specification asks for and it is
  arguably what a user wants, since the two results align the tile to the silhouette
  differently. It does mean the top list can hold visually similar entries. The filter does
  collapse the common case it is aimed at: at the default 0.02 the top five for the star
  fixture would otherwise be the same tile expressed through IH3, IH4 and IH6.
- **Identical tiles are always duplicates.** SPEC §7.2 says candidates with an identical
  reconstructed tile count as duplicates within a margin of 1e-12. That is applied
  independently of `--diversity`, so `--diversity 0` still removes exact repeats.
- **Fixture snapshots.** The four shapes of SPEC §8.7 are generated by a test helper into
  `tests/fixtures` and their top five is recorded under `tests/fixtures/snapshots`. The
  snapshot is taken at n = 30. A missing snapshot is written out and the test fails once
  with a message saying to inspect and commit it, so a snapshot can never be created and
  accepted silently in the same run.

Measured with the dense evaluator on 16 logical cores: n = 36 over all nine types and both
orientations takes 1.6 s, n = 48 takes 6.2 s. The targets of SPEC §12 are for the fast
evaluator of phase F4 and are not claimed here.

### 2026-09-25 - F4: fast evaluator, oracle and performance

The fast evaluator of SPEC §6.3 is in place and the search uses it; the dense builder stays
for reconstruction, for the weighted reranking of phase F5 and as the reference the oracle
compares against.

- **One source of truth for the basis.** The parametrisation was refactored so that both
  evaluators work from the same description. `TemplatePlan` holds everything that depends
  only on the template, which is where the null space computation, the projected blocks X'
  and their Gram contributions live; `BasisPlan` adds the part that depends on the k vector,
  which is the run placements and the Cholesky factor. The dense builder now materialises B
  from that plan rather than deriving it separately, so the two cannot drift apart.
- **Run slots.** Every run carries the position it occupies among all the runs a template
  can have, counting the ones a small k leaves out. The evaluator indexes the template
  tables with it directly.
- **The tables are interleaved.** SPEC §6.3 describes eight two dimensional tables. They
  hold exactly the values the specification defines, but four are stored together so that
  the four products of one position are neighbours; the five prefix sums are interleaved
  the same way and padded to a cache line. Only the arrangement differs.
- **Nothing is allocated per evaluation.** A worker builds one evaluator per chunk and
  points it at each k vector with `Load`, so the inner loop allocates nothing, uses no LINQ
  and calls nothing virtual, as SPEC §0.6 requires. The collector keeps a threshold and
  only builds a candidate that can still make the list, which matters when tens of millions
  of evaluations produce a few hundred keepers.
- **Ordering is quantised.** Candidates are ordered by the error rounded to 1e-12, the
  margin of SPEC §7.2, before the key of SPEC §7.1 decides. Two candidates often have the
  same error for a real reason, usually a symmetric goal, and comparing raw doubles let the
  last few bits of a summation order decide the ranking instead of the specified key. The
  rounding is a plain function of the error, so the comparison stays a total order. The
  snapshots of SPEC §8.7 were regenerated when this landed: every error value is unchanged
  and only the tie break moved, in each case towards the key the specification gives.
- **The k vectors are enumerated once per template, not once per orientation.** Holding the
  list twice cost about 230 MB at n = 120 and pushed the peak working set to 564 MB, over
  the budget of SPEC §12. Sharing the list between the two orientations brings it to
  330 MB.

#### Measured against SPEC §12

Eight threads, Release, all nine types, both orientations, on a 256 by 256 pixel
silhouette. The machine has 16 logical cores, so `--threads 8` was passed to match the
condition the specification states.

| Target | Measured | |
|---|---|---|
| n = 60, all nine types, both orientations, at most 5 s | 3.7 s | met |
| n = 96, all nine types, at most 30 s | 24.4 s | met |
| n = 120, all nine types, at most 90 s | 65.5 s | met |
| Memory below 500 MB | 330 MB peak working set | met |
| One evaluation (IH4, n = 120, microbenchmark) | 294 ns | met |

All five targets are met. The microbenchmark, measured with BenchmarkDotNet as SPEC §12
asks, gives 294 ns for IH4 at n = 120, with IH1 at 192 ns and IH6 at 315 ns.

**A note on measuring this.** A hand written timing loop was used first and was badly
wrong: it reported about 2000 ns for the same IH4 case, some seven times the real figure,
swung by a third between identical runs, and showed a pattern, every template with a
translation run being several times slower than the structurally identical IH6, that turned
out to be an artefact of the harness rather than anything in the evaluator. Four
optimisations were made on the strength of those numbers before BenchmarkDotNet was run:
interleaving the tables, removing bounds checks from the md loops with reference
arithmetic, forcing the per run helpers to inline, and vectorising the md loops with
`Vector<double>`. They are all correct and are kept, and the interleaving and the
vectorisation are worth having, but the decision to chase them rested on a measurement that
should have been taken with the proper tool first.

The end to end figures imply about 0.54 us of thread time per evaluation, which is higher
than the microbenchmark because it includes building the plan for each k vector, the
enumeration itself and the collector. The paper reports 5.1 s for n = 60 on one thread with
an O(n) evaluator, roughly 0.18 us per evaluation, so the two implementations are in the
same range.

### 2026-09-25 - F5: landmarks, weighted reranking and the render command

- **Where a landmark lives.** SPEC §4.6 says a landmark is given in input coordinates, which
  means something different per input kind, so the importer now hands back the transform
  that takes a position into the frame the contour lives in: y is flipped for an image,
  because the contour was mirrored on import; a GeoJSON landmark is longitude and latitude
  and goes through the same projection as the outline; a plain polygon needs nothing. The
  landmark is then projected onto the outline, taking the nearest point on a segment rather
  than the nearest sample, so the arc length parameter is not quantised to 1/n.
- **The weighted goal is normalised.** SPEC §7.4 says the measures of SPEC §6.1 apply
  unchanged to the scaled pair, but those measures are stated for a goal of unit norm and
  the scaled goal is not. It is rescaled to unit norm, which is a scaling of the whole
  problem and leaves the ranking untouched, so the weighted error stays comparable with the
  plain one.
- **Both errors are reported.** The candidate carries the plain error and the weighted one,
  the summary writes both, and the console prints both columns when landmarks are in play.
  The ranking is by the weighted error; `error` in the summary remains the unweighted one.
- **The render command rebuilds rather than replays.** The summary records the template, the
  k vector, the offset, the orientation and the input path, which is enough to reconstruct
  the candidate exactly. `render` re-runs the preprocessing on the recorded input and
  rebuilds the candidate, so the drawings come from the same code path a run uses instead of
  from stored coordinates. It needs the original input file to still be there, and says so
  plainly when it is not.

On a 256 by 256 cat silhouette at n = 36, weighting the two ears with weight 8 and sigma
0.04 moves the top candidate from IH6 at 4.26 per cent to an IH4 whose plain error is worse,
4.43 per cent, but whose weighted error is 3.40 per cent: the ears come out sharper and the
body a little looser, which is the trade the flag exists to make.
