# Escherize – spesifikasjon

Automatisk generering av fliser som tiler planet og ligner en gitt silhuett (Escherization), for C#/.NET.
Metoden er den uttømmende varianten av Koizumi–Sugiharas formulering slik den er beskrevet i
Nagata & Imahori, *An Efficient Algorithm for the Escherization Problem in the Polygon Representation*
(arXiv:1912.09605, 2019). Paragrafhenvisninger «[NI §x]» viser til den artikkelen.

---

## 0. Instruksjoner til Claude Code

1. Les hele dokumentet før du begynner. Artikkelen finnes på https://arxiv.org/abs/1912.09605
   (last den ned selv; den distribueres ikke med dette repoet).
2. Implementer fase for fase i rekkefølgen i §10. Etter hver fase skal `dotnet build` gi null advarsler og `dotnet test` være grønt før du går videre. Lag én commit per fase.
3. Ikke svekk toleranser, marker tester som `Skip` eller slett tester for å få dem grønne. Avdekker en test at spesifikasjonen er feil, skal du stoppe, beskrive funnet i `DECISIONS.md` og spørre.
4. To detaljer er bevisst **ikke** låst i spesifikasjonen: glideaksen (X/Y) per glidepar og fortegnet på rotasjonsvinkelen θ. De avgjøres empirisk av valideringstesten i §8.2, loggføres i `DECISIONS.md` og hardkodes deretter.
5. Ingen NuGet-avhengigheter utover listen i §2.
6. Den innerste søkeløkken (§6.3) skal ikke allokere, ikke bruke LINQ og ikke kalle virtuelle metoder.
7. Offentlige API-er skal ha XML-dokumentasjon. Kodekommentarer og identifikatorer skrives på engelsk.

---

## 1. Mål og omfang

**Mål:** Gitt en lukket silhuett (bilde, polygon eller landkontur) skal programmet finne de flisene som (a) tiler planet isohedralt og (b) ligger nærmest silhuetten. Resultatet er topp-K kandidater for manuell vurdering, siden avstandsmålet ikke samsvarer helt med menneskelig gjenkjennelse [NI, supplement].

**Bruksområde:** kunst og fysiske belegningsstein. Gjenkjennelighet er viktigere enn minimal feil, og fysisk produserbarhet (ingen tynne halser) er et eget kriterium.

**Innenfor v1:**
- Inndata: PNG-silhuett, polygon (JSON/CSV), GeoJSON (land/regioner).
- Forbehandling: kontur, glatting, resampling, normalisering, landemerker.
- Søk: 9 generelle isohedrale typer × alle punktfordelinger k × alle startpunkt j × begge omløpsretninger.
- Etterbehandling: selvkryssingsfilter, mangfoldsfilter, halsbredde, vektet omrangering.
- Utdata: JSON, SVG (flis, tiling, oversiktsark), DXF (flisomriss i mm).

**Utenfor v1:** GUI, U- og I-kanter, tilinger med flere ulike fliser, ikke-lineær etteroptimering av flisen.

---

## 2. Plattform, struktur og avhengigheter

- **.NET 8 (LTS), C# 12**, Visual Studio 2022 17.8+. Ikke .NET 9/10, siden brukeren kjører VS 2022.
- Alle prosjekter skal ha `<Nullable>enable</Nullable>`, `<TreatWarningsAsErrors>true</TreatWarningsAsErrors>`, `<Deterministic>true</Deterministic>` og `<InvariantGlobalization>true</InvariantGlobalization>`.
- All tallformatering til fil skal bruke `CultureInfo.InvariantCulture`.

```
Escherize.sln
src/
  Escherize.Core/        # geometri, maler, parametrisering, evaluering, søk, rendering (kun BCL)
  Escherize.Imaging/     # PNG → binærmaske → kontur (StbImageSharp)
  Escherize.Cli/         # konsollapp, argument-parsing for hånd (ingen CLI-pakke)
tests/
  Escherize.Tests/       # xUnit + orakel med MathNet.Numerics
  fixtures/              # testformer (JSON), genereres av en testhjelper
bench/
  Escherize.Bench/       # BenchmarkDotNet (valgfritt, fase F4)
DECISIONS.md
SPEC.md
```

| Pakke | Prosjekt | Lisens | Formål |
|---|---|---|---|
| StbImageSharp | Imaging | Public domain | PNG-dekoding |
| xunit, xunit.runner.visualstudio, Microsoft.NET.Test.Sdk | Tests | Apache/MIT | Tester |
| MathNet.Numerics | **kun** Tests | MIT | SVD/egenverdi-orakel |
| BenchmarkDotNet | **kun** Bench | MIT | Ytelsesmåling |

`Escherize.Core` skal kun bruke BCL.

---

## 3. Konvensjoner og datamodell

- Alle indekser er 0-baserte. En polygon har n punkter indeksert t = 0..n−1, og indekser tolkes syklisk (mod n).
- **Koordinatvektor** (lengde 2n): `u[t] = x_t`, `u[n + t] = y_t`. Samme oppsett gjelder for målvektoren `w`.
- **Omløpsretning:** målpolygonet gjøres positivt orientert (mot klokka, fortegnet areal > 0 i y-opp-koordinater). Bildekoordinater har y nedover og må speilvendes (y ← −y) ved import.
- **Normalisering:** trekk fra punktgjennomsnittet og skaler slik at ‖w‖₂ = 1. Ta vare på `centroid` og `scale` for å kunne transformere tilbake.
- **Flismal (template):** nv tilingshjørner V₀..V_{nv−1} og nv kanter i randrekkefølge. Kant s går fra V_s til V_{(s+1) mod nv} og har k_s indre punkter.
  - h(0) = 0 og h(s+1) = h(s) + k_s + 1.
  - Hjørne V_s ligger på indeks h(s), og kantpunkt i (i = 1..k_s) ligger på indeks h(s) + i.
  - Notasjon: e_s(i) for i = 0..k_s+1, der e_s(0) = V_s og e_s(k_s+1) = V_{s+1}.
  - Det må gjelde at n = nv + Σ_s k_s.

Sentrale typer (forslag; navn kan justeres):

```csharp
public enum EdgeKind { C, T, G, R }          // C = S-kant (selv, 180°), T = translasjonspar, G = glidepar, R = rotasjonspar
public enum GlideAxis { None, X, Y }

public sealed record EdgeSpec(EdgeKind Kind, int PairId, int KVar, GlideAxis Axis = GlideAxis.None, double ThetaDeg = 0);
public sealed record TemplateSpec(string Name, string Heesch, EdgeSpec[] Edges)
{
    public bool UsesProcrustes => Edges.Any(e => e.Kind == EdgeKind.G);
}

public readonly record struct CandidateKey(int TypeIndex, int[] K, int J, bool Reversed);
public sealed record Candidate(CandidateKey Key, double Error /* e, §6.1 */);
```

---

## 4. Forbehandling

### 4.1 PNG → kontur (`Escherize.Imaging`)
1. Dekod bildet med StbImageSharp til RGBA.
2. Beregn luminans L = 0.299R + 0.587G + 0.114B. Piksler med alfa < 128 regnes som bakgrunn.
3. Terskle med Otsu som standard (`--threshold <0..255>` overstyrer). Forgrunnen er mørk som standard; `--invert` snur dette.
4. Behold største 8-sammenhengende forgrunnskomponent og fyll hull: flomfyll bakgrunnen fra bildekanten, og alt som ikke nås regnes som forgrunn.
5. Kjør marching squares på masken med isoverdi 0.5 og behold den lengste lukkede løkka (ytre rand).
6. Speilvend y og gjør polygonet positivt orientert.

### 4.2 Polygon-import
- JSON: `{ "points": [[x,y], ...] }`
- CSV: én `x,y` per linje, med valgfri header.
- Fjern påfølgende duplikater (avstand < 1e-12 · diameter) og orienter positivt.

### 4.3 GeoJSON (land)
- Støtt `Polygon`, `MultiPolygon`, `Feature` og `FeatureCollection` (første feature eller `--feature-name`).
- Projiser alle ytre ringer med **Lambert asimutal flatetro projeksjon** (sfærisk, R = 6371 km) sentrert i bbox-senteret (φ₁, λ₀):
  ```
  k' = sqrt(2 / (1 + sinφ₁ sinφ + cosφ₁ cosφ cos(λ−λ₀)))
  x  = R k' cosφ sin(λ−λ₀)
  y  = R k' (cosφ₁ sinφ − sinφ₁ cosφ cos(λ−λ₀))
  ```
- Velg ringen med størst projisert areal (for Norge er det fastlandet), eller ringen gitt ved `--ring-index`.

### 4.4 Glatting
- Resample konturen til N_s = 2048 punkter med lik buelengde.
- Utfør DFT på zₖ = xₖ + i·yₖ (direkte sum er godt nok) og behold harmoniske med |h| ≤ H. Utfør deretter invers DFT.
- `--smooth H` har standardverdi 24. `--smooth 0` slår av glatting.
- Alternativ: `--dp <toleranse>` for Douglas–Peucker, som tolkes som en andel av diameteren.

### 4.5 Resampling og normalisering
- Resample til n punkter med lik buelengde (`--n`, standard 64). Startpunkt er konturindeks 0; startpunktet påvirker ikke resultatet fordi j-løkka dekker alle forskyvninger.
- Normaliser etter §3.
- Lag også det reverserte målet: `W_rev[t] = W[(n − t) mod n]`. Det har motsatt orientering, og det er tilsiktet: speilbilder av maler dekkes ved at søket kjøres på både W og W_rev (§7).

### 4.6 Landemerker og vekter (brukes kun i §7.4)
- Et landemerke har posisjon (i inndatakoordinater), vekt W_l ≥ 1 og bredde σ_l (andel av omkretsen, standard 0.03).
- Projiser posisjonen til nærmeste konturpunkt, som gir buelengdeparameteren s_l ∈ [0,1).
- Punktvekt: `ω_t = 1 + Σ_l (W_l − 1) · exp(−d(s_t, s_l)² / (2σ_l²))`, der d er syklisk buelengdeavstand.

---

## 5. Flismaler og parametrisering

### 5.1 Kantrelasjoner
Alle relasjoner er lineære i punktkoordinatene. For et par (a, b) har begge kanter samme k.

| Type | Relasjon (i = 0..k+1 når ikke annet står) | Nabo-isometri g over kant a (b: g⁻¹) |
|---|---|---|
| **C** (selv) | e(i) + e(k+1−i) = V_s + V_{s+1}, for i = 1..⌊k/2⌋. Ved odde k er midtpunktet e((k+1)/2) lik midten av kanten. | g(x) = V_s + V_{s+1} − x |
| **T** (a,b) | b(i) − b(0) = a(k+1−i) − a(k+1) | g(x) = x + a(k+1) − b(0) |
| **G** (a,b), akse | b(i) − b(0) = F·(a(i) − a(0)), der F_X = diag(1,−1) og F_Y = diag(−1,1) | g(x) = a(0) + F·(x − b(0)) |
| **R** (a,b), b = a+1, V = a(k+1) = b(0) | b(i) − V = R(θ)·(a(k+1−i) − V) | g(x) = V + R(−θ)·(x − V) |

Relasjonen for i = k+1 gir **hjørnebetingelsene**:
- T: b(k+1) − b(0) = a(0) − a(k+1)
- G: b(k+1) − b(0) = F·(a(k+1) − a(0))
- R: b(k+1) − V = R(θ)·(a(0) − V)
- C: ingen

### 5.2 Maltabell: de ni generelle typene [NI fig. 4]
Kantene er listet i randrekkefølge. Parene identifiseres ved `PairId` (A, B, C), og `KVar` angir hvilken k-variabel kanten bruker.

| Navn | Heesch | Kanter (Kind(PairId, KVar)) | Metrikk |
|---|---|---|---|
| IH1 | TTTTTT | T(A,0) T(B,1) T(C,2) T(A,0) T(B,1) T(C,2) | Euclid |
| IH2 | TG1G1TG2G2 | T(A,0) G(B,1) G(B,1) T(A,0) G(C,2) G(C,2) | Procrustes |
| IH3 | TG1G2TG2G1 | T(A,0) G(B,1) G(C,2) T(A,0) G(C,2) G(B,1) | Procrustes |
| IH4 | TCCTCC | T(A,0) C(1) C(2) T(A,0) C(3) C(4) | Euclid |
| IH5 | TCCTGG | T(A,0) G(B,1) G(B,1) T(A,0) C(2) C(3) | Procrustes |
| IH6 | CG1CG2G1G2 | G(A,0) G(B,1) G(A,0) C(2) G(B,1) C(3) | Procrustes |
| IH7 | C3C3C3C3C3C3 | R(A,0,120°) R(A,0,120°) R(B,1,120°) R(B,1,120°) R(C,2,120°) R(C,2,120°) | Euclid |
| IH21 | CC3C3C6C6 | C(0) R(A,1,120°) R(A,1,120°) R(B,2,60°) R(B,2,60°) | Euclid |
| IH28 | CC4C4C4C4 | C(0) R(A,1,90°) R(A,1,90°) R(B,2,90°) R(B,2,90°) | Euclid |

- **Glideakser:** ukjent per G-par. Prøv alle kombinasjoner i {X,Y}^(antall G-par) i §8.2.
- **θ-fortegn:** prøv alle-positive og alle-negative i §8.2.
- Den eller de konfigurasjonene som består, hardkodes i maltabellen og loggføres i `DECISIONS.md`.

### 5.3 Hjørneparametrisering B_v
- Ukjente: u_v (2nv), med samme x/y-blokkoppsett som i §3.
- Sett opp A_v fra hjørnebetingelsene i §5.1. Finn nullrommet ved Gauss-eliminasjon med full pivotering til redusert trappeform (toleranse 1e-12), og ortonormaliser med to-pass modifisert Gram–Schmidt (MGS2).
- Resultatet er B_v (2nv × md). Radene d_s = B_v[s,:] og d_{nv+s} = B_v[nv+s,:] parametriserer x og y for hjørne s.
- Assert md ≥ 3. Logg md per type i `DECISIONS.md`.

### 5.4 Kantparametere B_s (glisne, ortonormale kolonner)
Et **løp** er alle indre punktpar (p(i), q(i)) for én relasjon, der i = 1..k, eller i = 1..⌊k/2⌋ for C. Hvert løp gir to kolonner (en x-kolonne og en y-kolonne).

Kolonnene uttrykkes som lineærformer L over z = (x_p, y_p, x_q, y_q). Alle koeffisientene nedenfor er før skalering med 1/√2.

| Løp | p(i) | q(i) | L_x | L_y |
|---|---|---|---|---|
| C | h(s)+i | h(s)+k+1−i | (1,0,−1,0) | (0,1,0,−1) |
| T | h(a)+i | h(b)+k+1−i | (1,0,1,0) | (0,1,0,1) |
| G, X | h(a)+i | h(b)+i | (1,0,1,0) | (0,1,0,−1) |
| G, Y | h(a)+i | h(b)+i | (1,0,−1,0) | (0,1,0,1) |
| R(θ) | h(a)+k+1−i | h(b)+i | (1,0,cosθ,sinθ) | (0,1,−sinθ,cosθ) |

- Løpet er **anti-diagonalt** når p og q går i motsatt retning (C, T, R) og **diagonalt** når de går i samme retning (G).
- Kolonnene er ortonormale fordi ingen to kolonner deler en ikke-null rad. Hver kolonne har norm 1 og L_x ⟂ L_y.

### 5.5 Hjørnedel B_d → B'_d → B''_d [NI §3.3]
1. **Basisrader B_d** (2n × md), som uttrykker hvert punkt lineært ved hjørnene:
   - Hjørnet V_s: rad d_s (x) og d_{nv+s} (y).
   - T-løp: p har basis a(0), og q har basis b(k+1).
   - G-løp: p har basis a(0), og q har basis b(0).
   - R-løp: både p og q har basis V.
   - C-løp og C-midtpunkt: basis ½(V_s + V_{s+1}).
2. **Projiser ut B_s:** for hvert løp danner de fire koordinatradene (x_p, y_p, x_q, y_q) en blokk X (4 × md). Beregn `X' = (I − P)X` med `P = ½(L_x L_xᵀ + L_y L_yᵀ)`. Resultatet er konstant langs løpet. Hjørnerader og C-midtpunkt endres ikke.
3. **Gram-matrise:** `Gm = B'_dᵀ B'_d = Σ_hjørner (d dᵀ) + Σ_løp len · X'ᵀX' + Σ_midtpunkter (r rᵀ)`. Faktoriser med Cholesky: `Gm = LLᵀ`. Gm ≥ I fordi hjørneradene alene er ortonormale, så faktoriseringen er velkondisjonert.
4. Beregn `B''_d = B'_d L⁻ᵀ`. Da er `B = [B''_d | B_s]` ortonormal (2n × m, der m = md + ms).

Med den **tette byggeren** (fase F3) materialiseres B eksplisitt. Den **raske evaluatoren** (fase F4) bruker bare løpsbeskrivelsen, Gm og L.

### 5.6 Enumerering av k
- For en mal med v k-variabler og multiplisiteter m_v (2 for par, 1 for C) skal det gjelde at `Σ m_v k_v = n − nv` med k_v ≥ 0.
- Enumerer iterativt i leksikografisk rekkefølge. Rekkefølgen skal være deterministisk.
- `--min-k` (standard 0) gir nedre grense per kant.
- Skriv ut antall kombinasjoner per type før søket starter, slik at fremdriften kan vises.

---

## 6. Evaluering

### 6.1 Avstandsmål [NI §3.4, eq. 39–41]
Målvektoren w_j er målet forskjøvet slik at goal[(t + j) mod n] havner på malindeks t. Med ‖w‖ = 1:

- **Euclid** (maler uten G): `e = ‖w‖² − ‖Bᵀw‖²`
- **Procrustes** (maler med G, der rotasjonen er fri og skalaen fast):
  ```
  w_c = (y, −x)                       // w rotert −90°
  p = Bᵀw,  p_c = Bᵀw_c
  Gm2 = [[p·p, p·p_c], [p·p_c, p_c·p_c]]
  λ = (g11+g22)/2 + sqrt(((g11−g22)/2)² + g12²)
  e = ‖w‖² − λ
  ```

Egenskaper:
- Begge målene er ≥ 0 og kan sammenlignes på tvers av typer.
- √e er relativt RMS-avvik. Rapporter både e og √e i prosent.

### 6.2 Tett evaluator (fase F3)
Bruk eksplisitt B og regn direkte etter §6.1. Denne evaluatoren brukes til rekonstruksjon, vektet omrangering og som referanse i tester.

### 6.3 Rask evaluator (fase F4): O(1) i n per (k, j)
Forhåndsberegn tabeller én gang per målorientering (W og W_rev). Bruk doblede arrayer X[0..2n) og Y[0..2n) med X[i] = x_{i mod n}.

- **1D-prefiks:** S_x, S_y, S_xx, S_yy og S_xy, hver med lengde 2n+1.
- **Anti-diagonal:** `ANTI_fg[s][m] = Σ_{i<m} f(i)·g((s−i) mod n)` for s ∈ [0,n), m ∈ [0,2n] og fg ∈ {xx, xy, yx, yy}.
- **Diagonal:** `DIAG_fg[d][m] = Σ_{i<m} f(i)·g((i+d) mod n)`.
- Minnebruk er omtrent 8 · n · (2n+1) · 8 B per orientering, som er 1.9 MB for n = 120.

Per (k, j):

1. **Løpsmomenter.** For hvert løp med absolutt startpunkt P = p(1)+j, Q = q(1)+j og lengde ℓ: bygg momentmatrisen `M = Σ_i z_i z_iᵀ` (4 × 4). pp- og qq-blokkene hentes fra 1D-prefiks. pq-blokken hentes fra ANTI (s = (P+Q) mod n) eller DIAG (d = (Q−P) mod n) som differanser `T[·][P+ℓ] − T[·][P]`. For løp med synkende q, normaliser slik at p er den stigende indeksen. Vær nøye med at P+ℓ ≤ 2n.
2. **Hjørnedel.** `g = B'_dᵀ w = Σ_hjørner (d_x x_v + d_y y_v) + Σ_løp X'ᵀ·(ΣX_p, ΣY_p, ΣX_q, ΣY_q) + midtpunkter`. Løs deretter `c = L⁻¹ g`. For Procrustes gjøres det samme for w_c, som gir c_c; summene gjenbrukes via w_c = (y, −x).
3. **Kantdel.** Med `L̂ = L/√2` er `s_ww = Σ_løp Σ_{L∈{L_x,L_y}} L̂ᵀ M L̂`. For Procrustes trengs også `s_wwc = Σ L̂ᵀ M (Jᵀ L̂)` og `s_wcwc = Σ (JᵀL̂)ᵀ M (JᵀL̂)`, der J avbilder z → (y_p, −x_p, y_q, −x_q).
4. **Resultat:** `‖p‖² = ‖c‖² + s_ww`, `p·p_c = c·c_c + s_wwc` og `‖p_c‖² = ‖c_c‖² + s_wcwc`. Sett inn i §6.1.

Per-k-oppsett gjøres én gang per k: løpslayout, X'-blokker, Gm og Cholesky.

### 6.4 Rekonstruksjon av flis
Bruk den tette byggeren for (type, k, j, rev):
- **Euclid:** ξ* = Bᵀw_j.
- **Procrustes:** ξ* = [p p_c]·v, der v er den enhetlige egenvektoren til Gm2 for λ. Da er ‖ξ*‖ = √λ.
- u* = Bξ*. For overlegg beregnes optimal rotasjon θ* = atan2(Σ(x_u y_w − y_u x_w), Σ(x_u x_w + y_u y_w)).
- Lagre flisen i malrekkefølge sammen med h(s), kantmetadata og avbildningen malindeks t → målindeks (t+j) mod n (via W_rev når rev = true).
- Denormaliser til inndataenheter med `scale` og `centroid`.

---

## 7. Søk

### 7.1 Løkkestruktur
```
for rev in {false, true}:
  for type in selectedTypes:
    parallel over k-vektorer (Partitioner, chunk ~ 64 k-vektorer):
      oppsett per k (§6.3)
      for j in 0..n−1:
        e = Eval(k, j)
        thread-local topK.Offer(e, key)
flett heaps → sorter deterministisk
```
- **Sorteringsnøkkel:** (e, typeIndex, k leksikografisk, j, rev). Resultatet skal være identisk uavhengig av antall tråder.
- `--threads` har standardverdi `Environment.ProcessorCount`.
- Støtt `CancellationToken` og `IProgress<double>`.

### 7.2 Rå kandidater
- Behold `K_raw = max(10·K, 200)` kandidater, der K = `--top` (standard 20).
- Filtrer med **≤ 1e-12 margin**: kandidater med samme nøkkel fjernes ikke, men kandidater med identisk rekonstruert flis (§7.3) regnes som duplikater.

### 7.3 Etterbehandling (i rekkefølge)
1. Rekonstruer flisen (§6.4).
2. **Selvkryssing:** O(n²) segmenttest for ikke-nabosegmenter. Berøring innen 1e-9 · diameter teller som kryssing. Forkast også fliser med negativt fortegnet areal.
3. **Halsbredde:** minste avstand mellom punkt og segment der den sykliske indeksavstanden er ≥ n/10. Rapporter verdien relativt til √areal. `--min-neck <andel>` forkaster kandidater under grensen (av som standard).
4. **Mangfold:** forkast en kandidat hvis Procrustes-avstanden (rotasjon + skala) til en allerede valgt flis er < δ (`--diversity`, standard 0.02). Flisene sammenlignes punktvis i målindeksrekkefølge.
5. Behold de K beste.

### 7.4 Vektet omrangering (når landemerker er gitt)
- For hver av de `K_raw` rå kandidatene: bygg `B̃ = Ω^{1/2}B` og ortonormaliser med tett QR eller MGS2.
- Sett `w̃ = Ω^{1/2}w`. Punktvekten virker isotropt per punkt, så rotasjon kommuterer med Ω, og formlene i §6.1 kan brukes uendret på (B̃, w̃).
- Ranger etter vektet e og kjør deretter §7.3. Rapporter både vektet og uvektet feil.

### 7.5 Beskjæring (fase F6, valgfri)
Implementer dekomposisjonsrelaksasjon [NI §4] for IH4, IH5 og IH6 **bare hvis ytelsesmålene i §12 ikke nås**. Grensen er den K_raw-te beste feilen hittil.

---

## 8. Validering og tester

### 8.1 Geometri og import
- Resampling: lik buelengde innen 1e-9.
- Orientering: fortegnet areal > 0 etter import.
- Glatting: en sirkel er uendret innen 1e-6. Et kvadrat med H = 8 har areal innen ±5 %.
- PNG: syntetisk sirkel med r = 100 px (generert i testen) gir kontur med areal innen 1 % av πr².
- LAEA: projeksjonssenteret avbildes til (0,0), og to punkter symmetriske om λ₀ gir speilsymmetrisk x.

### 8.2 Malvaliditet (kritisk; avgjør glideakser og θ-fortegn)
For hver type og hver konfigurasjon (akser og fortegn):

1. Lag et mål med 36 punkter (sirkel med seedet støy) og velg en tilfeldig k-vektor der alle k ≥ 2.
2. Beregn `u = B(Bᵀw) + 0.05·B·r`, der r er normalfordelt med fast seed. Trekk på nytt ved selvkryssing (maks 50 forsøk).
3. **Kantdekning:** for hver kant e skal g_e avbilde partnerkantens punkter på kant e sine punkter innen 1e-9.
4. **Flislegging:** bygg et lapp-mønster ved BFS med dybde 3 via isometriene (§9.3). Sampl 20 000 seedede punkter i en disk med radius 2·R_flis rundt basisflisens tyngdepunkt. Hvert punkt som ligger lenger enn 1e-6 · diameter fra alle rander, skal ligge i **nøyaktig én** flis. Bruk punkt-i-polygon med vindingstall.
5. Nøyaktig de ekvivalente riktige konfigurasjonene skal bestå. Minst én feil konfigurasjon skal feile, ellers er testen for svak.

Etter at `DECISIONS.md` er fylt ut: testen kjører den hardkodede konfigurasjonen for alle 9 typer med 20 seeds hver.

### 8.3 Parametrisering
- BᵀB = I innen 1e-10.
- A·(Bξ) = 0 for tilfeldig ξ, der A settes opp fra **alle** relasjoner i §5.1 (inkludert indre punkter).
- rank(B) = nullitet(A), der nulliteten regnes ut med MathNet SVD (toleranse 1e-9).

### 8.4 Orakel
For hver type: 50 tilfeldige (mål, k, j, rev).
- Orakelet bygger B fra SVD av nullrommet til A og beregner λ med MathNet sin symmetriske egenverdiløser på BᵀVB [NI eq. 9–10].
- Krav: |e_rask − e_tett| ≤ 1e-9 og |e_tett − e_orakel| ≤ 1e-9.

### 8.5 Invarians
- e er uendret når målet roteres (for alle typer) og når det forskyves.
- e for rev=false på W er lik e for rev=true på W_rev.

### 8.6 Kjent svar
Når målet selv er en gyldig flis generert fra (type, k, ξ), skal fullt søk gi e < 1e-10 på rang 1.

### 8.7 Determinisme og regresjon
- Samme inndata med `--threads 1` og `--threads 8` gir byte-identisk `summary.json`.
- Snapshot av topp-5 (type, k, j, rev, e avrundet til 1e-9) for fire faste testformer i `tests/fixtures`:
  - stjerne
  - L-form
  - avlang form med smal midje (Norge-lignende)
  - katt-lignende form

---

## 9. Utdata

### 9.1 Filer (`--out <dir>`)
```
summary.json
goal.svg                        # normalisert mål med punktindeks for hvert 10. punkt
contact_sheet.svg               # rutenett med topp-K: rang, type, √e %, halsbredde
rank01_IH4_tile.svg             # kanter farget per par, hjørner som prikker, mål stiplet grått
rank01_IH4_tiling.svg           # lapp med 30–80 fliser, 4–6 farger, klippet til rektangel
rank01_IH4_tile.dxf             # lukket omriss i mm
...
```
- `--render-top N` bestemmer hvor mange kandidater som får egne filer (standard 5).
- Alle kandidatene i topp-K lagres i JSON.

### 9.2 `summary.json`
```json
{
  "input": { "path": "norge.geojson", "n": 64, "smooth": 24, "centroid": [0,0], "scale": 1.0 },
  "search": { "types": ["IH1","IH2","IH3","IH4","IH5","IH6","IH7","IH21","IH28"], "evaluations": 0, "seconds": 0.0 },
  "candidates": [
    {
      "rank": 1, "type": "IH4", "heesch": "TCCTCC", "k": [5,9,11,8,14], "j": 17, "reversed": false,
      "error": 0.0123, "rmsPercent": 11.1, "weightedError": null, "neckWidthRel": 0.18,
      "vertices": [0, 6, 16, 28, 34, 43],
      "tile": [[x,y], ...],
      "edges": [{ "kind": "T", "pair": "A", "from": 0, "to": 6 }],
      "isometries": [{ "edge": 0, "m": [a,b,c,d,tx,ty] }]
    }
  ]
}
```

### 9.3 Tiling-generator
- Isometrier representeres som affine 2×3-matriser i double.
- BFS starter fra identitet. Naboen til en plassert flis M over kant e er M ∘ g_e.
- Dedupliser på kvantisert tyngdepunkt (1e-6 · diameter).
- Stopp når alle fliser som skjærer målrektangelet er plassert, eller ved 400 fliser.
- Farger: grådig fargelegging i BFS-rekkefølge, der naboskap betyr delt kant. Paletten har 6 dempede farger.

### 9.4 DXF
- ASCII DXF R12 med `POLYLINE`/`VERTEX`/`SEQEND` og lukket-flagg 1, én flis per fil, lag `TILE`.
- Skaler slik at bounding-boksens lengste side blir `--tile-size-mm` (standard 200).
- Ta med `$INSUNITS = 4` (mm) i headeren.

---

## 10. Faser og akseptkriterier

| Fase | Innhold | Akseptkriterium |
|---|---|---|
| **F0** | Solution, prosjekter, `.editorconfig`, `Directory.Build.props`, `build.ps1` (build + test) | Bygger uten advarsler; tom test er grønn |
| **F1** | §4 og tester i §8.1; CLI-kommandoen `preprocess` | `escherize preprocess --input x.png --out o` skriver `goal.svg` for PNG, JSON og GeoJSON |
| **F2** | §5 med tett bygger; §8.2 og §8.3; `DECISIONS.md` | Alle 9 typer består validiteten; md per type er loggført |
| **F3** | §6.1, §6.2, §6.4, §7.1–7.3 med tett evaluator (n ≤ 48); §9; §8.6 og §8.7 | `escherize run` gir topp-K i SVG, JSON og DXF ende til ende |
| **F4** | §6.3 rask evaluator og parallellisering; §8.4 og §8.5; Bench-prosjekt | Orakeltester er grønne; målene i §12 er nådd |
| **F5** | §4.6 og §7.4 landemerker og vektet omrangering; `--min-neck`; `render`-kommando | Vektet rangering endrer rekkefølgen på testformen med landemerker |
| **F6** | §7.5 beskjæring (kun ved behov) | Topp-K identisk med og uten beskjæring; raskere |

---

## 11. CLI

```
escherize preprocess --input <fil> [--n 64] [--smooth 24|--dp 0.005] [--invert] [--threshold T]
                     [--ring-index i] [--out dir]
escherize run        --input <fil> | --config job.json
                     [--n 64] [--types IH4,IH5,IH6|all] [--top 20] [--render-top 5]
                     [--min-k 0] [--diversity 0.02] [--min-neck 0.0] [--threads N]
                     [--landmarks lm.json] [--tile-size-mm 200] [--out dir]
escherize render     --result summary.json --rank 3 [--tiles 80] [--out dir]
```

- Returkoder: 0 ved suksess, 1 ved ugyldig inndata, 2 ved intern feil. Feilmeldinger skrives til stderr.
- `job.json` inneholder de samme feltene som CLI-flaggene (camelCase). CLI-flagg overstyrer filen.

`lm.json`:
```json
{ "landmarks": [ { "name": "Nordkapp", "x": 25.78, "y": 71.17, "weight": 4, "sigma": 0.03 } ] }
```
For GeoJSON-inndata er x/y lengde- og breddegrad og projiseres på samme måte som konturen.

---

## 12. Ytelsesmål (8 logiske kjerner, Release, rask evaluator)

| Oppgave | Mål |
|---|---|
| n = 60, alle 9 typer, begge orienteringer | ≤ 5 s |
| n = 96, alle 9 typer | ≤ 30 s |
| n = 120, alle 9 typer | ≤ 90 s |
| Én evaluering (IH4, n = 120, mikrobenchmark) | ≤ 0.3 µs |
| Minne | < 500 MB |

Til sammenligning: artikkelen rapporterer 5.1 s for n = 60 uten beskjæring (entråds C++, O(n) per evaluering) og 9 s for n = 120 med beskjæring.

---

## 13. Forslag til CLAUDE.md

```
- Spesifikasjonen er SPEC.md; empiriske valg står i DECISIONS.md.
- Jobb fase for fase (SPEC §10). Kjør ./build.ps1 før hver commit.
- Endre aldri toleranser eller tester for å få grønt – stopp og spør.
- Escherize.Core skal kun bruke BCL. Nye pakker krever godkjenning.
- Hot path (SPEC §6.3): ingen allokering, ingen LINQ.
- Target: net8.0 (Visual Studio 2022).
```
