# Escherize

Find tiles that tile the plane and look like a shape you give it.

Hand it a silhouette — an image, a polygon, or the outline of a country — and it searches
the nine general isohedral tiling types for the tiles that resemble your shape most closely
while still tiling the plane with no gaps and no overlaps. It is what M.C. Escher did by
hand with lizards and birds, solved as an optimisation problem.

What comes out is the polygon: as JSON, as SVG, as DXF in millimetres for a cutter, and as
a watertight STL for 3D printing.

![Norway's coastline, fitted to an isohedral tile, which then tiles the plane](docs/images/pipeline.svg)

## Example

Mainland Norway, fitted to IH4, tiled:

```
escherize run --input norway.geojson --n 64 --min-neck 0.12 --out out
```

```
rank  type   rms %   neck   k
   1  IH4     4.54  0.122  [2,11,17,20,6]
   2  IH4     4.58  0.122  [1,12,17,22,5]
```

The coastline is the real one, deformed by 4.5 % — enough to close the tiling, little
enough that the shape is still Norway.

`--min-neck 0.12` is what makes the tile producible. Norway's narrowest point is 6.3 km,
and that waist is exactly where a tile wants to snap; the bound forces a neck of around
6 mm at a 120 mm tile size. Without it there are closer fits, but they do not survive being
picked up.

### Printed

<!-- Replace with a photograph of the printed tiles, ideally several locked together and
     shot from overhead. Put the image in docs/images/ and update the path below. -->

`produserbar/rank01_IH4_tile.stl` — 120 × 102 × 6 mm, 252 triangles, watertight. Flat on
the plate, no supports.

## Getting started

Needs .NET 8. Windows is the tested platform, but the code is pure BCL and should run
wherever .NET 8 does.

```powershell
.\build.ps1                                        # builds and runs the tests
escherize run --input my-shape.png --out out       # search
escherize verify --result out\summary.json         # confirm the tiles really do tile
```

`HOWTO.md` is the practical guide: how to get a shape in, what the knobs do, and how to get
the polygon out.

## The method

The exhaustive variant of the Koizumi–Sugihara formulation, as described in Nagata &
Imahori, *An Efficient Algorithm for the Escherization Problem in the Polygon
Representation*, [arXiv:1912.09605](https://arxiv.org/abs/1912.09605) (2019).

In short: each tiling type gives a linear subspace of admissible tile shapes. The distance
from the silhouette to the nearest point in that subspace has a closed form, so the search
can walk every combination of type, distribution of interior points, start offset and
orientation, and rank them exactly. One evaluation costs constant time regardless of how
many points the tile has.

## The project

```
src/Escherize.Core/       geometry, templates, parametrisation, search, rendering (BCL only)
src/Escherize.Imaging/    PNG → binary mask → contour
src/Escherize.Cli/        the command line tool
tests/Escherize.Tests/    213 tests, with MathNet as an independent oracle
bench/Escherize.Bench/    performance measurement
```

- `SPEC.md` — the specification the program was built against
- `HOWTO.md` — user guide
- `DECISIONS.md` — choices that do not follow from the specification, with the measurements
  behind them

## Performance

All nine types, both orientations, eight threads:

| n | Time |
|---|---|
| 60 | 3.7 s |
| 96 | 24 s |
| 120 | 66 s |

One evaluation of IH4 at n = 120 takes 294 ns.

## Licence

MIT, see `LICENSE`.

The dependencies are StbImageSharp (public domain) and — in the tests and benchmarks only —
MathNet.Numerics, xUnit and BenchmarkDotNet (MIT/Apache-2.0).

The paper is not distributed with this repository; download it from the arXiv link above.
