# HOWTO — making tiles with Escherize

The practical guide. `SPEC.md` is the specification and `DECISIONS.md` is the log of
choices made along the way; this document is only about how to get a tile out.

---

## 0. Build it once

```powershell
cd <where-you-cloned-the-repo>
.\build.ps1
```

The tool then sits here:

```
src\Escherize.Cli\bin\Release\net8.0\escherize.dll
```

Run it with `dotnet`. A shortcut keeps the rest of this shorter to type:

```powershell
$esc = Resolve-Path .\src\Escherize.Cli\bin\Release\net8.0\escherize.dll
function escherize { dotnet $esc @args }
```

Every example below assumes that function.

---

## 1. The short version

```powershell
escherize run --input cat.png --out out
```

A few seconds later, `out\` holds:

| File | What it is |
|---|---|
| `summary.json` | **The polygons as numbers.** Every candidate in the top K. |
| `contact_sheet.svg` | A sheet of all the candidates side by side. |
| `rank01_IH6_tile.svg` | Tile 1, with the silhouette dashed behind it. |
| `rank01_IH6_tiling.svg` | Tile 1 laid out as a pattern in the plane. |
| `rank01_IH6_tile.dxf` | **Tile 1 as DXF in millimetres**, ready for CAD/CNC. |
| `goal.svg` | The silhouette as the program understood it. |

Open `contact_sheet.svg` first. Pick a tile you like. Use its `.dxf`, or take the
coordinates from `summary.json`.

---

## 2. Getting a shape in

Three input kinds, recognised by the file extension.

### PNG (or JPG/BMP/GIF/TGA)

```powershell
escherize run --input cat.png --out out
```

- **A dark shape on a light background** is the default. If it is the other way round, use
  `--invert`.
- The threshold is chosen automatically with Otsu. Override with `--threshold 128` (0–255)
  if the automatic choice misses.
- Transparent pixels (alpha < 128) count as background, so a PNG with an alpha channel
  works straight away.
- Only the largest connected shape is used, and holes inside it are filled. You do not have
  to clean up stray specks yourself.

### Polygon (JSON or CSV)

JSON:

```json
{ "points": [[0,0], [4,0], [4,1], [1,1], [1,4], [0,4]] }
```

CSV, with an optional header:

```
x,y
0,0
4,0
4,1
```

```powershell
escherize run --input shape.json --out out
escherize run --input shape.csv  --out out
```

The winding direction does not matter — the program orients it positively itself.

### GeoJSON (countries, counties, islands)

```powershell
escherize run --input norway.geojson --out out
```

- `Polygon`, `MultiPolygon`, `Feature` and `FeatureCollection` are supported.
- The contour is projected with the Lambert azimuthal equal-area projection, centred on the
  bounding box centre.
- With several rings, the one with the largest area wins — for Norway that is the mainland.
  Override with `--ring-index 2`.
- Several features: `--feature-name Norway` picks one, otherwise the first is used.

---

## 3. Check the silhouette before searching

Worth doing when the result looks odd. `preprocess` runs only the import, the smoothing and
the resampling, and writes `goal.svg`:

```powershell
escherize preprocess --input cat.png --n 48 --out check
```

Open `check\goal.svg`. You should recognise your shape, with a point number every tenth
point. If it looks wrong, that is where the problem is — not in the search:

- **The shape is inverted** (the background became the shape) → `--invert`
- **The shape is a blob with no detail** → `--n` too low, or the smoothing too hard
- **The shape is jagged** → raise `--smooth`
- **Detail has been wiped away** → lower `--smooth`, say `--smooth 40`, or `--smooth 0` to
  switch smoothing off entirely

---

## 4. Search

```powershell
escherize run --input cat.png --n 48 --top 20 --render-top 5 --out out
```

The console reports as it goes:

```
rank  type   rms %   neck   k
   1  IH6     3.83  0.243  [12,2,9,5]
   2  IH4     3.84  0.254  [9,6,5,1,12]
```

- **rms %** — how far the tile sits from the silhouette, as a percentage. Lower is closer.
  Below about 4 % is usually well recognisable; 10 % and up becomes a suggestion of the
  shape.
- **neck** — the narrowest waist in the tile, relative to √area. Below about 0.1 the tile
  gets fragile to make physically.
- **k** — how the points are distributed over the edges. Only interesting if you want to
  reproduce exactly the same candidate later.

**Note:** the lowest rms is not always the nicest tile. The distance measure is not the same
thing as human recognition — which is why you get a top list to choose from rather than one
answer. Look through `contact_sheet.svg`.

### How long it takes

All nine types, both orientations, 16 cores:

| `--n` | Time |
|---|---|
| 24 | 0.2 s |
| 36 | 1.3 s |
| 48 | 1.7 s |
| 64 | 3.3 s |
| 96 | ~25 s |
| 120 | ~65 s |

A higher `--n` gives more detail in the tile, but the growth is steep. **48–64 is a good
starting point.** Go to 96 and beyond only once you have found a shape worth refining.

Restrict to particular types to go faster:

```powershell
escherize run --input cat.png --types IH4,IH6 --out out
```

---

## 5. Getting the polygon out

This is usually the point of the whole exercise.

### For CAD, a laser cutter or CNC — DXF

```powershell
escherize run --input cat.png --render-top 3 --tile-size-mm 250 --out out
```

Gives `out\rank01_*.dxf`, `rank02_*.dxf`, `rank03_*.dxf`. Each file is:

- ASCII DXF R12, one closed `POLYLINE` on the layer `TILE`
- scaled so that **the longest side of the bounding box becomes `--tile-size-mm`**
  (default 200)
- centred on the origin
- `$INSUNITS = 4`, that is, millimetres

Opens directly in Fusion 360, LibreCAD, Inkscape, QCAD and most other things.

### For your own code — JSON

`summary.json` holds every candidate in the top K, not just the ones that were drawn:

```json
{
  "candidates": [
    {
      "rank": 1,
      "type": "IH6",
      "heesch": "CG1CG2G1G2",
      "k": [2, 7, 9, 3],
      "j": 12,
      "reversed": false,
      "error": 0.001814,
      "rmsPercent": 4.26,
      "neckWidthRel": 0.252,
      "vertices": [0, 3, 11, 14, 24, 32],
      "tile": [[137.72, -213.98], [152.03, -206.78], ...],
      "edges": [{ "kind": "G", "pair": "A", "from": 0, "to": 3 }, ...],
      "isometries": [{ "edge": 0, "m": [a, b, c, d, tx, ty] }, ...]
    }
  ]
}
```

What you need to know about `tile`:

- **The coordinates are in input units.** Pixels for a PNG (with y up — the image is
  mirrored on import), kilometres for GeoJSON, your own units for a polygon file.
- **The polygon is not closed.** The last point connects to the first implicitly.
- **Positive winding** (counter-clockwise).
- `vertices` says which indices in `tile` are the tile's corners. The points between two
  corners make up one edge.

Extracting it with Python:

```python
import json
d = json.load(open("out/summary.json"))
tile = d["candidates"][0]["tile"]          # a list of [x, y]
print(len(tile), "points")
```

Extracting it with PowerShell:

```powershell
$d = Get-Content out\summary.json | ConvertFrom-Json
$d.candidates[0].tile | ForEach-Object { "{0},{1}" -f $_[0], $_[1] } | Set-Content tile.csv
```

### For 3D printing — STL

```powershell
escherize run --input cat.png --render-top 3 --tile-size-mm 120 --stl-height-mm 6 --out out
```

`--stl-height-mm` is the thickness in millimetres. Without the flag, no STL is written.
You get `out\rank01_*.stl`:

- binary STL, which is what most slicers expect
- the longest side of the footprint becomes `--tile-size-mm`, the thickness becomes
  `--stl-height-mm`
- centred on the origin with its base on z = 0, so it lies flat on the build plate
- **watertight**: every edge is shared by exactly two triangles, and every normal points
  outwards

STL carries no unit in the format, but every common slicer reads the numbers as
millimetres, and that is what they are.

Practical notes for printing:

- **Lay it flat.** The tile is a plate; it needs neither supports nor a brim.
- **Thin necks come out weak.** A neck of 0.1 of √area on a 120 mm tile is around 4 mm
  across. Use `--min-neck` if the tiles will be handled, or raise `--tile-size-mm`.
- **Want the tiles to click together?** Print several and lay them out following
  `*_tiling.svg`. They fit exactly — but print them with a little clearance if your printer
  lays down an outline, or they will be too tight against one another.

### For looking at — SVG

`rank01_*_tile.svg` shows the tile with its edges coloured per pair and the silhouette
dashed behind it. `rank01_*_tiling.svg` shows the pattern. The number of tiles in the
pattern is set with `--tiles 80` (30–80).

### Tiles that fit together

`isometries` are the affine maps that put the neighbouring tiles in place — a 2×3 matrix
`[a, b, c, d, tx, ty]` per edge, where the point (x, y) goes to
`(a·x + b·y + tx, c·x + d·y + ty)`. The neighbour across edge *e* of a tile placed with `M`
is `M ∘ g_e`. That is how `*_tiling.svg` is generated, and you can use the same thing to lay
out the pattern yourself.

---

## 6. Check that the tile really does tile

The tiles tile by virtue of how they are constructed, and the test suite checks that for all
nine types. To see it confirmed for your particular tile, before you spend material on it:

```powershell
escherize verify --result out\summary.json
```

```
rank  type   tiles  patch  edge error  neck   area      verdict
   1  IH4       61      5     4.4e-17  0.122 347605.974 TILES
   2  IH4       61      5     8.3e-17  0.122 351431.630 TILES
```

The command rebuilds each candidate and checks two things:

- **edge error** — that the neighbour isometry of every edge lays the partner edge exactly
  on top of it. The number should be around 1e-16, that is, machine precision. The
  requirement is 1e-9.
- **TILES** — that a patch 5 tiles deep neither overlaps nor has gaps. Overlaps are found by
  sampling 20 000 points; gaps by counting edges, where every edge must be shared by exactly
  two tiles.

`tiles` is how many tiles in the patch have a complete ring of neighbours around them.

Exit code 0 means everything passed, 1 that something failed. If something fails, it is a
bug in the program — please report it. Use `--rank 3` to check just one.

The visual check is `*_tiling.svg`: if you see white gaps or tiles lying on top of each
other, something is wrong. But the numeric check above is stricter than the eye.

## 7. Controlling what you get

| Flag | Default | What it does |
|---|---|---|
| `--n 64` | 64 | Number of points in the tile. More detail, but steeper time. |
| `--smooth 24` | 24 | Smoothing. Lower = rounder, higher = more detail. `0` = off. |
| `--dp 0.005` | — | Douglas–Peucker instead of Fourier smoothing. |
| `--types IH4,IH6` | all nine | Restrict to particular isohedral types. |
| `--top 20` | 20 | How many candidates end up in `summary.json`. |
| `--render-top 5` | 5 | How many get their own SVG and DXF files. |
| `--min-neck 0.15` | 0 (off) | Discard tiles with a thinner neck than this. |
| `--diversity 0.02` | 0.02 | How different the candidates must be. Higher = more variety. |
| `--tile-size-mm 200` | 200 | Longest side of the DXF and STL tile, in mm. |
| `--stl-height-mm 6` | 0 (off) | Thickness of the STL. Without it, no STL is written. |
| `--tiles 60` | 60 | Number of tiles in the pattern drawing (30–80). |
| `--threads 8` | all cores | Number of threads. |
| `--min-k 0` | 0 | Lower bound on points per edge. |

Two worth knowing well:

**`--min-neck`** — if the tile is to be cut in stone, wood or metal, a thin neck is where it
breaks. `--min-neck 0.15` weeds out the most fragile. The default is off, because the bound
depends entirely on your material.

**`--diversity`** — the same tile tends to turn up in several guises (the same shape
expressed through IH3, IH4 *and* IH6). The default 0.02 removes the obvious repeats. Raise
it to 0.1 if the top list still looks monotonous; set it to 0 to see absolutely everything.

---

## 8. Emphasising the details that matter (landmarks)

Some features matter more to recognition than others — the ears on a cat, Nordkapp on
Norway. Put them in a file:

```json
{ "landmarks": [
  { "name": "left ear",  "x": 86,  "y": 95, "weight": 8, "sigma": 0.04 },
  { "name": "right ear", "x": 170, "y": 95, "weight": 8, "sigma": 0.04 }
] }
```

```powershell
escherize run --input cat.png --landmarks ears.json --out out
```

- `x`/`y` are **in the input file's own coordinates**: pixels for a PNG (with y downwards,
  as you read them off in an image editor), longitude and latitude for GeoJSON.
- `weight` ≥ 1 says how much more the point counts. 4–10 is a usable range.
- `sigma` is how far the influence reaches, as a fraction of the perimeter. 0.03 is the
  default.

You then get two columns:

```
rank  type   rms %   weighted   neck   k
   1  IH4     4.43      3.40  0.256  [7,4,3,9,0]
```

The ranking follows `weighted`. Note that `rms %` usually gets slightly **worse** — that is
the intent: the tile gives up a little on the whole in order to hit what you pointed at. On
the cat test that gives clearer ears and a looser body.

---

## 9. Redraw one candidate

If you want rank 7 as DXF, or a larger pattern of rank 2, you do not have to search again:

```powershell
escherize render --result out\summary.json --rank 7 --tiles 80 --out out2
```

The candidate is rebuilt from the type, the k vector and the offset recorded in
`summary.json`. **The input file has to still be where it was** — the path from the original
run is used to recreate the silhouette.

Two traps:

- `render` **does not inherit** `--tile-size-mm` from the original run; it starts at 200
  again. If you ran with `--tile-size-mm 250`, repeat the flag here.
- `--rank` counts in the order `summary.json` has. If you ran with landmarks, that is the
  weighted order.

---

## 10. Repeating a run later

Put the settings in a file rather than typing them each time:

```json
{
  "input": "cat.png",
  "n": 48,
  "smooth": 24,
  "types": "IH4,IH6",
  "top": 10,
  "renderTop": 3,
  "minNeck": 0.15,
  "tileSizeMm": 150,
  "out": "cat-out"
}
```

```powershell
escherize run --config job.json
```

The field names are the same as the flags, in camelCase. Relative paths are read from where
you are standing. Flags on the command line override the file:

```powershell
escherize run --config job.json --n 64
```

---

## 11. When something goes wrong

| Symptom | Cause and remedy |
|---|---|
| "does not exist" | Wrong path. Exit code 1. |
| "Unknown flag --xyz" | A typo in a flag — they are rejected deliberately rather than ignored. |
| The shape is inverted | `--invert`, or set `--threshold` by hand. |
| Every tile looks the same | Raise `--diversity` to 0.05–0.1. |
| Every tile is a round blob | `--n` is too low, or `--smooth` too hard. Try `--n 64 --smooth 32`. |
| Thin necks in the tile | `--min-neck 0.15`. |
| Too slow | Lower `--n`, or restrict `--types`. |
| No candidates at all | `--min-neck` is set too strictly. Lower it. |
| `rms` above 10 % whatever you do | The shape is hard to tile. Try a simpler silhouette, or accept that it will be a suggestion. |

Exit codes: `0` fine, `1` invalid input, `2` internal error. Error messages go to stderr.

---

## 12. A full working pass

From image to cutting file:

```powershell
# 1. Check that the silhouette is read correctly
escherize preprocess --input cat.png --n 48 --out check
#    → open check\goal.svg

# 2. A quick coarse search, to see what is there
escherize run --input cat.png --n 48 --top 20 --render-top 0 --out coarse
#    → open coarse\contact_sheet.svg

# 3. A fine search using what you learned: emphasise the ears, demand a solid neck
escherize run --input cat.png --n 64 --landmarks ears.json `
              --min-neck 0.15 --top 12 --render-top 5 `
              --tile-size-mm 250 --out fine
#    → open fine\contact_sheet.svg, pick a rank

# 4. Redraw the chosen one, larger pattern. Repeat --tile-size-mm: render does not inherit it.
escherize render --result fine\summary.json --rank 3 --tiles 80 `
                 --tile-size-mm 250 --out final
#    → final\rank03_*.dxf goes to the cutter
```
