# HOWTO — lage fliser med Escherize

Praktisk bruksanvisning. `SPEC.md` er spesifikasjonen, `DECISIONS.md` er loggen over
valg som ble tatt underveis; dette dokumentet handler bare om hvordan du får ut en flis.

---

## 0. Bygg én gang

```powershell
cd <der-du-klonet-repoet>
.\build.ps1
```

Verktøyet ligger etterpå her:

```
src\Escherize.Cli\bin\Release\net8.0\escherize.dll
```

Kjør det med `dotnet`. Lag gjerne en snarvei så resten blir kortere å skrive:

```powershell
$esc = Resolve-Path .\src\Escherize.Cli\bin\Release\net8.0\escherize.dll
function escherize { dotnet $esc @args }
```

Alle eksemplene under antar den funksjonen.

---

## 1. Kortversjonen

```powershell
escherize run --input katt.png --out ut
```

Etter noen sekunder ligger det i `ut\`:

| Fil | Hva det er |
|---|---|
| `summary.json` | **Polygonene som tall.** Alle topp-K kandidatene. |
| `contact_sheet.svg` | Oversiktsark med alle kandidatene side om side. |
| `rank01_IH6_tile.svg` | Flis nr. 1, med silhuetten stiplet bak. |
| `rank01_IH6_tiling.svg` | Flis nr. 1 lagt ut som mønster i planet. |
| `rank01_IH6_tile.dxf` | **Flis nr. 1 som DXF i millimeter**, klar for CAD/CNC. |
| `goal.svg` | Silhuetten slik programmet oppfattet den. |

Åpne `contact_sheet.svg` først. Velg en flis du liker. Bruk `.dxf`-fila til den, eller
hent koordinatene fra `summary.json`.

---

## 2. Få formen inn

Tre inndatatyper, gjenkjent på filendelsen.

### PNG (eller JPG/BMP/GIF/TGA)

```powershell
escherize run --input katt.png --out ut
```

- **Mørk form på lys bakgrunn** er standard. Er det motsatt: `--invert`.
- Terskelen settes automatisk med Otsu. Overstyr med `--threshold 128` (0–255) hvis
  automatikken bommer.
- Gjennomsiktige piksler (alfa < 128) regnes som bakgrunn, så PNG med alfakanal
  fungerer rett fram.
- Bare den største sammenhengende formen brukes, og hull i den fylles igjen. Du trenger
  ikke rydde bort småflekker selv.

### Polygon (JSON eller CSV)

JSON:

```json
{ "points": [[0,0], [4,0], [4,1], [1,1], [1,4], [0,4]] }
```

CSV, med valgfri header:

```
x,y
0,0
4,0
4,1
```

```powershell
escherize run --input form.json --out ut
escherize run --input form.csv  --out ut
```

Omløpsretningen spiller ingen rolle — programmet snur til positiv orientering selv.

### GeoJSON (land, fylker, øyer)

```powershell
escherize run --input norge.geojson --out ut
```

- `Polygon`, `MultiPolygon`, `Feature` og `FeatureCollection` støttes.
- Konturen projiseres med Lambert asimutal flatetro projeksjon, sentrert i bbox-senteret.
- Ved flere ringer velges den med størst areal — for Norge blir det fastlandet. Overstyr
  med `--ring-index 2`.
- Flere features: `--feature-name Norge` velger én, ellers brukes den første.

---

## 3. Sjekk silhuetten før du søker

Verdt å gjøre hvis resultatet ser rart ut. `preprocess` kjører bare innlesing, glatting
og resampling, og skriver `goal.svg`:

```powershell
escherize preprocess --input katt.png --n 48 --out sjekk
```

Åpne `sjekk\goal.svg`. Du skal kjenne igjen formen din, med punktnummer for hvert
tiende punkt. Ser den feil ut, er det her problemet ligger — ikke i søket:

- **Formen er invertert** (bakgrunnen ble til formen) → `--invert`
- **Formen er en klump uten detaljer** → for lav `--n`, eller for hard glatting
- **Formen er hakkete** → øk `--smooth`
- **Detaljer er visket bort** → senk `--smooth`, f.eks. `--smooth 40`, eller `--smooth 0`
  for å skru glattingen helt av

---

## 4. Søk

```powershell
escherize run --input katt.png --n 48 --top 20 --render-top 5 --out ut
```

Konsollen viser fortløpende:

```
rank  type   rms %   neck   k
   1  IH6     3.83  0.243  [12,2,9,5]
   2  IH4     3.84  0.254  [9,6,5,1,12]
```

- **rms %** — hvor langt flisen ligger fra silhuetten, i prosent. Lavere er nærmere.
  Under ~4 % er vanligvis godt gjenkjennelig; 10 % og oppover blir en antydning.
- **neck** — smaleste hals i flisen, relativt til √areal. Under ~0.1 blir flisen skjør
  å produsere fysisk.
- **k** — hvordan punktene er fordelt på kantene. Bare interessant hvis du vil gjenskape
  akkurat samme kandidat senere.

**Merk:** laveste rms er ikke alltid den peneste flisen. Avstandsmålet er ikke det samme
som menneskelig gjenkjennelse — derfor får du en topp-liste å velge fra, ikke ett svar.
Se gjennom `contact_sheet.svg`.

### Hvor mye tid tar det

Alle ni typer, begge omløpsretninger, 16 kjerner:

| `--n` | Tid |
|---|---|
| 24 | 0,2 s |
| 36 | 1,3 s |
| 48 | 1,7 s |
| 64 | 3,3 s |
| 96 | ~25 s |
| 120 | ~65 s |

Høyere `--n` gir mer detalj i flisen, men veksten er bratt. **48–64 er et godt
utgangspunkt.** Gå til 96+ bare når du har funnet en form du vil forfine.

Begrens til enkelte typer for å gå fortere:

```powershell
escherize run --input katt.png --types IH4,IH6 --out ut
```

---

## 5. Hent ut polygonet

Dette er som regel poenget med hele øvelsen.

### Til CAD, laserkutter eller CNC — DXF

```powershell
escherize run --input katt.png --render-top 3 --tile-size-mm 250 --out ut
```

Gir `ut\rank01_*.dxf`, `rank02_*.dxf`, `rank03_*.dxf`. Hver fil er:

- ASCII DXF R12, én lukket `POLYLINE` på laget `TILE`
- skalert slik at **lengste side av bounding-boksen blir `--tile-size-mm`** (standard 200)
- sentrert om origo
- `$INSUNITS = 4`, altså millimeter

Kan åpnes direkte i Fusion 360, LibreCAD, Inkscape, QCAD og det meste annet.

### Til egen kode — JSON

`summary.json` inneholder alle kandidatene i topp-K, ikke bare de som ble tegnet:

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

Det du trenger å vite om `tile`:

- **Koordinatene er i inndataenheter.** Piksler for PNG (med y opp — bildet er speilvendt
  ved import), kilometer for GeoJSON, dine egne enheter for polygonfiler.
- **Polygonet er ikke lukket.** Siste punkt kobles til det første implisitt.
- **Positiv omløpsretning** (mot klokka).
- `vertices` sier hvilke indekser i `tile` som er flisens hjørner. Punktene mellom to
  hjørner utgjør én kant.

Uthenting med Python:

```python
import json
d = json.load(open("ut/summary.json"))
tile = d["candidates"][0]["tile"]          # liste med [x, y]
print(len(tile), "punkter")
```

Uthenting med PowerShell:

```powershell
$d = Get-Content ut\summary.json | ConvertFrom-Json
$d.candidates[0].tile | ForEach-Object { "{0},{1}" -f $_[0], $_[1] } | Set-Content flis.csv
```

### Til 3D-print — STL

```powershell
escherize run --input katt.png --render-top 3 --tile-size-mm 120 --stl-height-mm 6 --out ut
```

`--stl-height-mm` er tykkelsen i millimeter. Uten flagget skrives ingen STL.
Du får `ut\rank01_*.stl`:

- binær STL, som er det skjærere flest forventer
- lengste side av grunnflaten blir `--tile-size-mm`, tykkelsen blir `--stl-height-mm`
- sentrert om origo, med bunnen på z = 0, så den ligger flatt på byggeplata
- **vanntett**: hver kant deles av nøyaktig to trekanter, og alle normaler peker utover

STL har ingen enhet i formatet, men alle vanlige skjærere leser tallene som millimeter,
og det er det de er.

Praktiske tips for print:

- **Legg den flatt.** Flisen er en plate; den trenger verken støtte eller brim.
- **Tynne halser blir svake.** En hals på 0,1 av √areal på en 120 mm flis er rundt 4 mm
  bredt. Bruk `--min-neck` hvis flisene skal håndteres, eller øk `--tile-size-mm`.
- **Vil du at flisene skal klikke sammen?** Skriv ut flere og legg dem etter mønsteret i
  `*_tiling.svg`. De passer eksakt — men skriv dem ut med litt klaring hvis printeren din
  legger på en kantlinje, ellers blir de for trange mot hverandre.

### Til visning — SVG

`rank01_*_tile.svg` viser flisen med kantene fargelagt per par og silhuetten stiplet bak.
`rank01_*_tiling.svg` viser mønsteret. Antall fliser i mønsteret styres med `--tiles 80`
(30–80).

### Fliser som henger sammen

`isometries` er de affine avbildningene som legger naboflisene på plass — en 2×3-matrise
`[a, b, c, d, tx, ty]` per kant, der punktet (x, y) går til
`(a·x + b·y + tx, c·x + d·y + ty)`. Naboen over kant *e* av en flis plassert med `M` er
`M ∘ g_e`. Det er slik `*_tiling.svg` genereres, og du kan bruke det samme til å legge ut
mønsteret selv.

---

## 6. Sjekk at flisen faktisk tiler

Flisene tiler i kraft av hvordan de er konstruert, og testsuiten sjekker det for alle ni
typene. Vil du se det bekreftet for akkurat din flis, før du bruker materiale på den:

```powershell
escherize verify --result ut\summary.json
```

```
rank  type   tiles  patch  edge error  neck   area      verdict
   1  IH4       61      5     4.4e-17  0.122 347605.974 TILES
   2  IH4       61      5     8.3e-17  0.122 351431.630 TILES
```

Kommandoen bygger opp hver kandidat på nytt og sjekker to ting:

- **edge error** — at naboisometrien til hver kant legger partnerkanten nøyaktig oppå den.
  Tallet skal være rundt 1e-16, altså maskinpresisjon. Kravet er 1e-9.
- **TILES** — at en lapp på 5 fliser i dybden hverken overlapper eller har hull. Overlapp
  finnes ved å trekke 20 000 punkter; hull ved å telle kanter, der hver kant må deles av
  nøyaktig to fliser.

`tiles` er hvor mange fliser i lappen som har en komplett ring av naboer rundt seg.

Returkode 0 betyr at alt består, 1 at noe feilet. Feiler noe, er det en feil i programmet
— si fra. Bruk `--rank 3` for å sjekke bare én.

Den visuelle sjekken er `*_tiling.svg`: ser du hvite glipper eller fliser som ligger oppå
hverandre, er noe galt. Men tallsjekken over er strengere enn øyet.

## 7. Styr hva du får

| Flagg | Standard | Hva det gjør |
|---|---|---|
| `--n 64` | 64 | Antall punkter i flisen. Mer detalj, men brattere tid. |
| `--smooth 24` | 24 | Glatting. Lavere = rundere, høyere = mer detalj. `0` = av. |
| `--dp 0.005` | — | Douglas–Peucker i stedet for Fourier-glatting. |
| `--types IH4,IH6` | alle ni | Begrens til enkelte isohedrale typer. |
| `--top 20` | 20 | Hvor mange kandidater som havner i `summary.json`. |
| `--render-top 5` | 5 | Hvor mange som får egne SVG- og DXF-filer. |
| `--min-neck 0.15` | 0 (av) | Forkast fliser med tynnere hals enn dette. |
| `--diversity 0.02` | 0.02 | Hvor ulike kandidatene må være. Høyere = mer variasjon. |
| `--tile-size-mm 200` | 200 | Lengste side av DXF- og STL-flisen, i mm. |
| `--stl-height-mm 6` | 0 (av) | Tykkelse på STL-en. Uten dette skrives ingen STL. |
| `--tiles 60` | 60 | Antall fliser i mønstertegningen (30–80). |
| `--threads 8` | alle kjerner | Antall tråder. |
| `--min-k 0` | 0 | Nedre grense for punkter per kant. |

To som er verdt å kjenne godt:

**`--min-neck`** — skal flisen kuttes i stein, tre eller metall, er en tynn hals et
bruddpunkt. `--min-neck 0.15` luker vekk de skjøreste. Standard er av, fordi grensen
avhenger helt av materialet ditt.

**`--diversity`** — samme flis dukker gjerne opp i flere varianter (samme form uttrykt
gjennom IH3, IH4 *og* IH6). Standard 0.02 fjerner de åpenbare gjengangerne. Øk til 0.1
hvis topp-listen fortsatt ser ensformig ut; sett til 0 for å se absolutt alt.

---

## 8. Fremhev viktige detaljer (landemerker)

Noen trekk betyr mer for gjenkjennelsen enn andre — ørene på en katt, Nordkapp på Norge.
Legg dem i en fil:

```json
{ "landmarks": [
  { "name": "venstre øre", "x": 86,  "y": 95, "weight": 8, "sigma": 0.04 },
  { "name": "høyre øre",   "x": 170, "y": 95, "weight": 8, "sigma": 0.04 }
] }
```

```powershell
escherize run --input katt.png --landmarks orer.json --out ut
```

- `x`/`y` er **i inndatafilas egne koordinater**: piksler for PNG (med y nedover, slik du
  leser dem av i et bilderedigeringsprogram), lengde- og breddegrad for GeoJSON.
- `weight` ≥ 1 sier hvor mye mer punktet teller. 4–10 er et brukbart område.
- `sigma` er hvor langt virkningen rekker, som andel av omkretsen. 0.03 er standard.

Du får da to kolonner:

```
rank  type   rms %   weighted   neck   k
   1  IH4     4.43      3.40  0.256  [7,4,3,9,0]
```

Rangeringen følger `weighted`. Legg merke til at `rms %` gjerne blir litt **dårligere** —
det er meningen: flisen ofrer litt på helheten for å treffe det du pekte ut. På kattetesten
gir det tydeligere ører og en løsere kropp.

---

## 9. Tegn én kandidat på nytt

Vil du ha rang 7 i DXF, eller et større mønster av rang 2, trenger du ikke søke om igjen:

```powershell
escherize render --result ut\summary.json --rank 7 --tiles 80 --out ut2
```

Kandidaten bygges opp igjen fra typen, k-vektoren og forskyvningen som står i
`summary.json`. **Inndatafila må ligge der den lå** — stien fra det opprinnelige kjøret
brukes til å gjenskape silhuetten.

To fallgruver:

- `render` **arver ikke** `--tile-size-mm` fra det opprinnelige kjøret; den starter på 200
  igjen. Kjørte du med `--tile-size-mm 250`, må du gjenta flagget her.
- `--rank` teller i den rekkefølgen `summary.json` har. Kjørte du med landemerker, er det
  den vektede rekkefølgen.

---

## 10. Gjenta et kjør senere

Legg innstillingene i en fil i stedet for å skrive dem hver gang:

```json
{
  "input": "katt.png",
  "n": 48,
  "smooth": 24,
  "types": "IH4,IH6",
  "top": 10,
  "renderTop": 3,
  "minNeck": 0.15,
  "tileSizeMm": 150,
  "out": "katt-ut"
}
```

```powershell
escherize run --config jobb.json
```

Feltnavnene er de samme som flaggene, i camelCase. Relative stier tolkes fra der du står.
Flagg på kommandolinjen overstyrer fila:

```powershell
escherize run --config jobb.json --n 64
```

---

## 11. Når noe går galt

| Symptom | Årsak og botemiddel |
|---|---|
| «does not exist» | Feil sti. Returkode 1. |
| «Unknown flag --xyz» | Skrivefeil i et flagg — de avvises med vilje i stedet for å ignoreres. |
| Formen er invertert | `--invert`, eller sett `--threshold` manuelt. |
| Alle flisene ser like ut | Øk `--diversity` til 0.05–0.1. |
| Alle flisene er runde klumper | `--n` er for lav, eller `--smooth` for hard. Prøv `--n 64 --smooth 32`. |
| Tynne halser i flisen | `--min-neck 0.15`. |
| For treg | Senk `--n`, eller begrens `--types`. |
| Ingen kandidater i det hele tatt | `--min-neck` er satt for strengt. Senk den. |
| `rms` over 10 % uansett | Formen er vanskelig å flislegge. Prøv en enklere silhuett, eller godta at det blir en antydning. |

Returkoder: `0` greit, `1` ugyldig inndata, `2` intern feil. Feilmeldinger går til stderr.

---

## 12. En full arbeidsgang

Fra bilde til kuttefil:

```powershell
# 1. Sjekk at silhuetten leses riktig
escherize preprocess --input katt.png --n 48 --out sjekk
#    → åpne sjekk\goal.svg

# 2. Grovsøk, raskt, for å se hva som finnes
escherize run --input katt.png --n 48 --top 20 --render-top 0 --out grov
#    → åpne grov\contact_sheet.svg

# 3. Finsøk med det du lærte: fremhev ørene, krev solid hals
escherize run --input katt.png --n 64 --landmarks orer.json `
              --min-neck 0.15 --top 12 --render-top 5 `
              --tile-size-mm 250 --out fin
#    → åpne fin\contact_sheet.svg, velg en rang

# 4. Tegn den valgte på nytt, større mønster. Gjenta --tile-size-mm: render arver den ikke.
escherize render --result fin\summary.json --rank 3 --tiles 80 `
                 --tile-size-mm 250 --out ferdig
#    → ferdig\rank03_*.dxf går til kutteren
```
