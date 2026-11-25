# LyricPulse — Quality Assurance Verification Report

## Phase 1 QA Gate

- **Target Scope:** Phase 1 (`LyricPulse.Core` + `LyricPulse.Tests`, 5-project `.NET 10` solution scaffold)
- **Audited Commit:** `ed79508e` (`.NET 10.0.404` SDK, `net10.0-windows10.0.19041.0`)
- **Initial Baseline Verdict (`ed79508e` unmodified):** **FAIL** (2 HIGH blockers: `73.78%` branch coverage `< 80%` gate, and transient HTTP/429/5xx errors swallowed as `NotFound` poisoning the 24h negative disk cache)
- **Post-Remediation Verdict (working tree on top of `ed79508e`):** **PASS** (0 CRITICAL, 0 unresolved HIGH; both HIGH blockers remediated via TDD and verified with 0 build warnings, 139/139 passing offline tests, `96.78%` line coverage, and `86.82%` branch coverage)

---

### 1. Build, Test & Coverage Summary

| Verification Gate | Baseline (`ed79508e`) | Post-Remediation (Working Tree) | Requirement / Target | Status |
|---|---:|---:|---|---|
| `dotnet build LyricPulse.sln --no-restore` (Debug) | 0 warnings, 0 errors | 0 warnings, 0 errors | 0 warnings / 0 errors (`CORE-01`) | **PASS** |
| `dotnet build LyricPulse.sln -c Release --no-restore` (Release) | 0 warnings, 0 errors | 0 warnings, 0 errors | 0 AOT/trim/analyzer warnings (`CORE-01`) | **PASS** |
| `dotnet test --no-build` (offline deterministic) | 126 passed, 0 failed | **139 passed, 0 failed** | 100% pass, zero live network (`QA-01`) | **PASS** |
| `LyricPulse.Core` Line Coverage (`coverlet.runsettings`) | 89.79% (`3,035 / 3,380`) | **96.78%** (`3,311 / 3,421`) | `>= 80%` (`QA-01`) | **PASS** |
| `LyricPulse.Core` Branch Coverage (`coverlet.runsettings`) | **73.78%** (`1,106 / 1,499`) — *FAIL* | **86.82%** (`1,331 / 1,533`) | `>= 80%` (`QA-01`) | **PASS** |
| `LyricPulse.Core` SkiaSharp / Avalonia / HarfBuzz Refs | 0 direct, 0 transitive | 0 direct, 0 transitive | Strictly 0 (`ROADMAP` Decision #2) | **PASS** |

*Note on coverage scope:* `tests/LyricPulse.Tests/coverlet.runsettings` excludes generated regex/JSON/WinRT projection namespaces and `CoreLogMessages`; `WindowsGsmtcSessionProvider` and `GsmtcSessionHandle` carry `[ExcludeFromCodeCoverage]` as thin WinRT COM interop shells while all pure session-selection, clock, and thumbnail-deduplication logic is tested in `MediaSessionCoordinator`, `MediaSessionSelector`, `PlayerClassifier`, and `MonotonicPlaybackClock`.

#### `LyricPulse.Core` Per-File Coverage Table (Post-Remediation)

| Source File | Line Coverage | Lines | Branch Coverage | Branches |
|---|---:|---:|---:|---:|
| `Color/OklabColorMath.cs` | 98.57% | 138 / 140 | 94.74% | 36 / 38 |
| `Color/OklabPaletteExtractor.cs` | 98.20% | 218 / 222 | 86.36% | 76 / 88 |
| `Lyrics/CandidateScorer.cs` | 98.43% | 125 / 127 | 92.22% | 83 / 90 |
| `Lyrics/DemoSongProvider.cs` | 96.88% | 31 / 32 | 76.47% | 13 / 17 |
| `Lyrics/DiskLyricsCache.cs` | 91.39% | 244 / 267 | 83.05% | 98 / 118 |
| `Lyrics/Dtos/ProviderDtos.cs` | 93.55% | 58 / 62 | 100.00% | 0 / 0 |
| `Lyrics/LocalLrcSidecarProvider.cs` | 90.00% | 90 / 100 | 66.00% | 33 / 50 |
| `Lyrics/LrcLibLyricsProvider.cs` | 97.42% | 227 / 233 | 82.41% | 89 / 108 |
| `Lyrics/LyricsOrchestrator.cs` | 95.29% | 81 / 85 | 91.67% | 33 / 36 |
| `Lyrics/LyricsParser.cs` | 98.40% | 308 / 313 | 89.29% | 150 / 168 |
| `Lyrics/NetEaseLyricsProvider.cs` | 95.78% | 159 / 166 | 77.78% | 70 / 90 |
| `Lyrics/ProviderHttpResilience.cs` | 89.83% | 106 / 118 | 80.43% | 37 / 46 |
| `Lyrics/TimingOffsetService.cs` | 98.34% | 178 / 181 | 90.00% | 54 / 60 |
| `Lyrics/TitleNormalizer.cs` | 99.25% | 133 / 134 | 87.93% | 51 / 58 |
| `Lyrics/WordTimingInterpolator.cs` | 100.00% | 164 / 164 | 90.57% | 96 / 106 |
| `Media/MediaSessionCoordinator.cs` | 99.17% | 120 / 121 | 84.85% | 56 / 66 |
| `Media/MediaSessionSelector.cs` | 95.74% | 135 / 141 | 87.21% | 75 / 86 |
| `Media/MonotonicPlaybackClock.cs` | 98.44% | 126 / 128 | 92.59% | 50 / 54 |
| `Media/PlayerClassifier.cs` | 100.00% | 96 / 96 | 94.23% | 49 / 52 |
| `Models/AppSettings.cs` | 100.00% | 68 / 68 | 100.00% | 32 / 32 |
| `Models/ColorModels.cs` | 100.00% | 36 / 36 | 100.00% | 22 / 22 |
| `Models/LyricsModels.cs` | 100.00% | 46 / 46 | 100.00% | 0 / 0 |
| `Models/MediaModels.cs` | 100.00% | 39 / 39 | 100.00% | 0 / 0 |
| `ServiceCollectionExtensions.cs` | 100.00% | 29 / 29 | 100.00% | 0 / 0 |
| `Settings/JsonSettingsStore.cs` | 92.68% | 76 / 82 | 87.50% | 14 / 16 |
| `Visuals/LyricTextNormalizationData.cs` | 96.89% | 156 / 161 | 83.33% | 30 / 36 |
| `Visuals/StarterLexiconVisualMatcher.cs` | 95.38% | 124 / 130 | 87.50% | 84 / 96 |
| **Total (`LyricPulse.Core`)** | **96.78%** | **3,311 / 3,421** | **86.82%** | **1,331 / 1,533** |

---

### 2. Critical Invariant & Edge-Case Audit Matrix

| Invariant / Edge Case | Verification Evidence | Verdict |
|---|---|---|
| **Timing Offset Sign Convention** (`-0.5s` = Earlier, `+0.5s` = Later, `Clamp(Position - Offset, 0, Duration)`) | Verified in `src/LyricPulse.Core/Lyrics/TimingOffsetService.cs:218-235` (`interpolatedPosition - timingOffset`, clamped `[0, trackDuration]`) and `TimingOffsetServiceTests.cs:21-48`. Clamping to `[-15s, +15s]` verified in `LyricsModels.cs:97-104` and `TimingOffsetServiceTests.cs:64-69`. | **PASS** |
| **Immutability (`record` / `ImmutableArray`)** | All models in `src/LyricPulse.Core/Models/*.cs` and DTOs in `src/LyricPulse.Core/Lyrics/Dtos/ProviderDtos.cs` are `sealed record` or `readonly record struct` with `init`/positional properties and `ImmutableArray<T>`/`ImmutableDictionary<K,V>`. Zero public `set;` properties in Models/DTOs. | **PASS** |
| **AOT JSON (Zero Reflection Serialization)** | Every `JsonSerializer.SerializeAsync` / `DeserializeAsync` call in `src/` passes source-generated `LyricPulseJsonContext.Default.*` or `LyricPulseIndentedJsonContext.Default.*` (`Serialization/LyricPulseJsonContext.cs`). `IsAotCompatible`, `EnableTrimAnalyzer`, and `EnableAotAnalyzer` enabled with 0 warnings in Debug & Release. | **PASS** |
| **Zero Hardcoded Secrets** | Full regex scan across `src/**/*.cs` found 0 API keys, secrets, bearer tokens, or passwords. | **PASS** |
| **Zero Silent Error Swallowing / Empty Catches** | Audited all 16 `catch` blocks across `src/`. All catches are typed/filtered (`JsonException`, `IOException`, `COMException`, `OperationCanceledException`, `HttpRequestException`). **HIGH-1** (provider transient HTTP/429/5xx failures silently collapsed to `NotFound`) was found and fixed (see Findings). | **PASS (after HIGH-1 fix)** |
| **`LyricPulse.Core` Free of SkiaSharp / Avalonia** | Verified `src/LyricPulse.Core/LyricPulse.Core.csproj` and `src/LyricPulse.Core/obj/project.assets.json` (17 total libraries, 0 SkiaSharp/Avalonia/HarfBuzz direct or transitive). | **PASS** |
| **Empty Title Edge Case** | Verified `TrackIdentity.IsValid`, `PlayerClassifier.ClassifyItem` (`Unknown`), `MediaSessionSelector.FilterEligible`, `TitleNormalizer`, `LrcLibLyricsProvider`, `NetEaseLyricsProvider`, `LocalLrcSidecarProvider`, and `LyricsOrchestrator`. | **PASS** |
| **Instrumental & Negative Cache** | Verified 30d `Instrumental`, 24h `NotFound`, 7d `PlainOnly`, 90d `Synced` TTLs in `DiskLyricsCache`, plus `LyricsOrchestrator` NOT caching `LyricsStatus.Error` on transient failures (fixed in HIGH-1). | **PASS (after HIGH-1 fix)** |
| **Rapid Track Skip / Coalescing** | Verified `LyricsOrchestrator` in-flight request coalescing + per-caller `CancellationToken` (`WaitAsync`), `MediaSessionSelector` 1,500 ms track-transition hysteresis, and rapid sequential track switches in `MediaSessionCoordinator`. | **PASS** |
| **Corrupted `settings.json` Recovery** | Verified `JsonSettingsStore.LoadAsync` recovers cleanly to `AppSettings.Default` on missing or corrupt JSON (`JsonSettingsStoreTests.cs:92-108`) and clamps `NaN`/out-of-range fields (`QaGateRegressionAndCoverageTests.cs`). | **PASS** |
| **B&W Album Art Monochrome Fallback** | Verified `OklabPaletteExtractor.cs:38-43` (`C_p85 < 0.035` -> `AlbumAccentPalette.MonochromeStudio` `#F1F5F9`/`#CBD5E1`/`#94A3B8`/`#0F0F23`) across B&W checkerboard, pure black, and pure white (`OklabColorMathTests.cs:30-44,89-97`). | **PASS** |
| **Neon Horizon 12/12 Keywords** | Verified 12 lines, 36.0s Enhanced LRC (`Assets/Demo/neon-horizon.lrc`), curated palette (`#5EEAD4`/`#818CF8`/`#94A3B8`/`#0F0F23`), and 12/12 exact Fluent Icon + Emoji matches (`DemoSongProviderTests.cs`, `StarterLexiconVisualMatcherTests.cs`). | **PASS** |

---

### 3. Audit Findings

#### Remediated During Phase 1 QA Gate (CRITICAL / HIGH)

1. **[HIGH — FIXED] Transient HTTP / 429 Cooldown / 5xx Provider Errors Swallowed as `NotFound`, Poisoning the 24-Hour Negative Disk Cache**
   - **Locations:**
     - `src/LyricPulse.Core/Lyrics/LrcLibLyricsProvider.cs:71-170` (original lines `115, 127-130, 152-155`)
     - `src/LyricPulse.Core/Lyrics/NetEaseLyricsProvider.cs:69-202` (original lines `69-79, 118-121, 184-187`)
     - `src/LyricPulse.Core/Lyrics/LyricsOrchestrator.cs:112-131`
   - **Details:** `ProviderHttpResilience.SendWithResilienceAsync` returns `null` on `HttpRequestException`, timeout, rate-limit rejection, or active 429 cooldown, and returns the final `5xx` `HttpResponseMessage` once retries are exhausted. Both `LrcLibLyricsProvider` and `NetEaseLyricsProvider` treated `response is null || response.StatusCode != HttpStatusCode.OK` identically to `404 NotFound` and never returned `LyricsStatus.Error`. As a result, `LyricsOrchestrator` left `hadProviderError == false` during real offline/timeout/429/5xx outages and wrote a **24-hour `NotFound` negative cache file** to `DiskLyricsCache`, blocking lyrics recovery for 24 hours once connectivity returned. Additionally, `LrcLibLyricsProvider` wasted Steps 1B and 1C after Step 1A already failed transiently.
   - **TDD Remediation Applied:**
     - Added failing regression tests (`ProvidersAndOrchestrator_TransientHttpAndCooldownErrors_ReturnErrorWithoutPoisoningNegativeCache`, `LrcLibAndNetEase_429CooldownTripsErrorStatusNotNotFound`) in `tests/LyricPulse.Tests/Lyrics/QaGateRegressionAndCoverageTests.cs` (confirmed RED on `ed79508e`).
     - Added internal `HttpFetchResult<T>(T Value, bool IsTransientError)` propagation in `LrcLibLyricsProvider` and `NetEaseLyricsProvider`, treating `response is null`, `429`, `408`, and `>= 500` as `LyricsStatus.Error` (short-circuiting wasted follow-up steps while preserving `plainFallback` when already retrieved, `404` -> `NotFound`, and HTTP 200 malformed-JSON resilience). Confirmed GREEN.

2. **[HIGH — FIXED] `LyricPulse.Core` Branch Coverage (`73.78%`) Below Locked `>= 80%` QA-01 Gate + Unexercised >1,000-Entry LRU Eviction Test**
   - **Locations:**
     - `tests/LyricPulse.Tests/Lyrics/EdgeCaseResilienceAndCacheTests.cs:224-239` (`TrackOffsetStore_EvictsLruEntriesOver1000AndRecoversFromCorruptJson` only inserted 1 key `"k1"` and never exercised `TrackOffsetStore.EvictLeastRecentlyUsed`)
     - `tests/LyricPulse.Tests/Lyrics/QaGateRegressionAndCoverageTests.cs` (added 13 comprehensive edge-case/regression tests)
   - **Details:** On `ed79508e`, `LyricPulse.Core` achieved `89.79%` line coverage but only `73.78%` (`1,106 / 1,499`) branch coverage, failing the mandatory `>= 80%` line and branch gate in `ROADMAP.md` / `REQUIREMENTS.md` (`QA-01`). Missing coverage included >1,000-entry `TrackOffsetStore` LRU eviction, rapid-skip caller cancellation in `LyricsOrchestrator`, `LocalLrcSidecarProvider` fallback ordering/sanitization/empty/plain files (`48%` baseline branch), `CandidateScorer` intermediate score bands (`66.67%`), `ProviderHttpResilience` HTTP-date `Retry-After` & exhausted retries (`67.39%`), `DiskLyricsCache` bilingual round-trip & corrupt/schema-mismatch pruning, `AppSettings` `SourcePolicy` & `NaN` guards (`59.38%`), and real `-s`/`-ves`/doubled-consonant stemming in `StarterLexiconVisualMatcher`.
   - **Remediation Applied:** Added `tests/LyricPulse.Tests/Lyrics/QaGateRegressionAndCoverageTests.cs`, raising `LyricPulse.Core` coverage to **`96.78%` line** (`3,311 / 3,421`) and **`86.82%` branch** (`1,331 / 1,533`) with 139/139 passing tests.

---

#### Non-Blocking Findings Logged for Phase 2 / Phase 3 Follow-Up (MEDIUM / LOW — `src/` Left Untouched)

3. **[MEDIUM] CJK Word-Synced (YRC / Enhanced LRC) Spacing Corrupted on Disk Cache Rehydration**
   - **Location:** `src/LyricPulse.Core/Lyrics/LyricsParser.cs:221` (interacting with `src/LyricPulse.Core/Lyrics/DiskLyricsCache.cs:281-293`)
   - **Details:** `BuildEnhancedWordLine` sets `IsSpaceSeparated: token.SpaceAfter || i < rawTokens.Count - 1`. When a NetEase CJK `WordNative` YRC document (where each character has `IsSpaceSeparated = false`, e.g., `"星光夜"`) is serialized by `DiskLyricsCache` as `<00:04.000>星<00:04.500>光<00:05.000>夜` and rehydrated via `ParseLrc`, the `|| i < rawTokens.Count - 1` clause forces spaces between every CJK character (`"星 光 夜"`).
   - **Recommendation:** Only set `IsSpaceSeparated = true` when `token.SpaceAfter` is true (or both adjacent tokens satisfy `WordTimingInterpolator.IsLatinToken`).

4. **[MEDIUM] Stale `AlbumArtHash` & Transient `TrackChanged(null)` Churn in `MediaSessionCoordinator`**
   - **Location:** `src/LyricPulse.Core/Media/MediaSessionCoordinator.cs:147-184`
   - **Details:**
     1. Line 171 copies `_cachedAlbumArt?.Sha256Hash` into every new `MediaSessionSnapshot` without clearing `_cachedAlbumArt` when `trackChanged == true`, so a newly started track initially advertises the previous track's `AlbumArtHash` until `GetCurrentAlbumArtAsync` runs.
     2. During `MediaSessionSelector`'s 1,500 ms track-transition hysteresis (`MediaPlaybackState.Changing` with empty `Title`), line 147 resolves `track = null`, switches `_clock` `trackKey` to the AUMID, and immediately fires `TrackChanged(null)`, briefly clearing downstream lyrics during track transitions.
   - **Recommendation:** Clear `_cachedAlbumArt` on real track changes (and suppress `TrackChanged(null)` while the active session is held in `Changing` hysteresis) before wiring live overlay rendering in Phase 2/3.

5. **[MEDIUM] Unsynchronized Async State Mutation in `TimingOffsetService` & Captive Typed `HttpClient` Registration**
   - **Locations:**
     - `src/LyricPulse.Core/Lyrics/TimingOffsetService.cs:258-299`
     - `src/LyricPulse.Core/ServiceCollectionExtensions.cs:31-43`
   - **Details:**
     1. `TimingOffsetService.NudgeAsync`, `ResetAsync`, and `OnTrackChangedAsync` read `_current`, `await` disk I/O, and then assign `_current` with no synchronization (`SemaphoreSlim`), allowing rapid global-hotkey nudges or concurrent track changes to drop updates or restore stale-track offsets.
     2. `AddHttpClient<LrcLibLyricsProvider>()` / `AddHttpClient<NetEaseLyricsProvider>()` register transient typed clients, which are then captured as singletons via `AddSingleton<ILyricsProvider>(sp => sp.GetRequiredService<...>())`. Capturing factory-issued `HttpClient` instances in singletons prevents handler/DNS rotation, and resolving concrete provider types directly creates unshared rate-limiter/cooldown instances.
   - **Recommendation:** Guard `TimingOffsetService` mutations with a `SemaphoreSlim(1, 1)` (or `Interlocked` state update before debounced persist) and inject `IHttpClientFactory` into singleton providers.

6. **[LOW] Semantic Mismatch in `StarterLexicon.tsv` / `IrregularLemmas` (`"living"` -> `skull` 💀)**
   - **Locations:** `src/LyricPulse.Core/Visuals/LyricTextNormalizationData.cs:123`, `src/LyricPulse.Core/Resources/StarterLexicon.tsv:79`
   - **Details:** `"living"`, `"life"`, `"alive"`, `"birth"`, `"born"` are listed under the `skull` concept (`💀` / `ic_fluent_warning_24_filled`), causing Line 12 of `Neon Horizon` (`"Living inside this electric dream"`) to pair `"Living"` with a skull/warning icon. Address during Phase 3 `VIS-01` lexicon expansion (~300 concepts).

7. **[LOW] Minor Cleanup & Diagnostics Hygiene**
   - `src/LyricPulse.Core/Lyrics/NetEaseLyricsProvider.cs:76-79`: `nolyric == true` currently maps to `LyricsStatus.NotFound` (24h TTL) rather than `LyricsStatus.Instrumental` (30d TTL).
   - `src/LyricPulse.Core/Lyrics/Dtos/ProviderDtos.cs:18-21`: `LrclibErrorDto` is unused (`0/4` lines).
   - `src/LyricPulse.Core/Settings/JsonSettingsStore.cs:56` & `src/LyricPulse.Core/Lyrics/TimingOffsetService.cs:139`: Corrupt-JSON fallback does not emit a warning log (optional `ILogger` recommended).
   - `src/LyricPulse.Core/Color/OklabPaletteExtractor.cs:27`: Use `(long)width * height * 4` to guard against 32-bit `int` overflow on malformed dimensions.
   - `src/LyricPulse.Core/Lyrics/TimingOffsetService.cs:46-52`: `TryGetOffsetAsync` does not refresh `LastAccessedUtc` (eviction is last-written rather than last-read LRU).

---

### 4. Modified / Added Files Pending Commit

- `src/LyricPulse.Core/Lyrics/LrcLibLyricsProvider.cs` — Propagate transient HTTP/429/5xx failures as `LyricsStatus.Error` instead of `NotFound`.
- `src/LyricPulse.Core/Lyrics/NetEaseLyricsProvider.cs` — Propagate transient HTTP/429/5xx failures as `LyricsStatus.Error` instead of `NotFound`.
- `tests/LyricPulse.Tests/Lyrics/QaGateRegressionAndCoverageTests.cs` — 13 deterministic offline regression & edge-case tests lifting `LyricPulse.Core` coverage to `96.78%` line / `86.82%` branch.
- `.planning/QA_REPORT.md` — This Phase 1 QA Gate verification report.


---

## Milestone 1A (Phases 1-3) QA Gate

- **Target Scope:** Milestone 1A — Phases 1–3 (`LyricPulse.Core`, `LyricPulse.Rendering`, `LyricPulse.App`, `LyricPulse.Tests`)
- **Audited Commit:** `371bfa5` (`.NET 10.0.404` SDK, `net10.0-windows10.0.19041.0`, READ-ONLY audit on `src/`)
- **Verdict:** **FAIL** (0 CRITICAL, **4 HIGH** blockers, 5 MEDIUM, 3 LOW)
  - Build (`Debug` & `Release`, AOT/Trim/SingleFile analyzers), test execution (`234 / 234` passing, 0 skipped, 0 live network), Release `--smoke-test` (`ExitCode = 0`), Core dependency isolation, and Forbidden API checks all **PASS**.
  - Blocked by: (1) `LyricPulse.Rendering` branch coverage `72.62% < 80%` gate + false-green 64 MP decompression-bomb test, (2) unsynchronized UI-thread (RAF) vs. Avalonia Compositor-thread mutation & teardown across renderers/`LyricCanvasControl`, (3) native resource + root DI container leaks (`LyricStyleRouter`, per-frame `SKRoundRect`, fallback `SKTypeface`), and (4) frozen `BoundsConfig` closure preventing live `AnchorZone`/`SafeMargin`/`PerDisplayBounds` updates.

---

### 1. Build, Test, Smoke & Coverage Summary (`371bfa5`)

| Verification Gate | Measured Result (`371bfa5`) | Requirement / Target | Status |
|---|---|---|---|
| `dotnet build LyricPulse.sln -c Debug --no-restore` | **0 warnings, 0 errors** (Exit 0, `5.42s`) | 0 warnings / 0 errors (`CORE-01`) | **PASS** |
| `dotnet build LyricPulse.sln -c Release --no-restore` | **0 warnings, 0 errors** (Exit 0, `4.16s`) | 0 AOT/trim/single-file warnings (`CORE-01`) | **PASS** |
| `dotnet test --no-build --collect:"XPlat Code Coverage"` | **234 passed, 0 failed, 0 skipped** (`32.07s`) | 100% deterministic offline pass (`QA-01`/`QA-02`) | **PASS** |
| Release `LyricPulse.App.exe --smoke-test` | **Exit 0** (`[PASS] ... Fonts=5 \| Shaders=OK \| DemoLines=12 \| Styles=Fisheye,Codestyle,VisualStack \| TrayHeader='Demo: Neon Horizon'`) | Clean headless startup & MVP render (`QA-03`) | **PASS** |
| `LyricPulse.Core` Line Coverage (`coverlet.runsettings`) | **96.59%** (`4,028 / 4,170`) | `>= 80%` (`QA-01`) | **PASS** |
| `LyricPulse.Core` Branch Coverage (`coverlet.runsettings`) | **86.58%** (`1,504 / 1,737`) | `>= 80%` (`QA-01`) | **PASS** |
| `LyricPulse.Rendering` Line Coverage (`coverlet.runsettings`) | **88.25%** (`2,569 / 2,911`) | `>= 80%` (`QA-02`/`QA-04`) | **PASS** |
| `LyricPulse.Rendering` Branch Coverage (`coverlet.runsettings`) | **72.62%** (`703 / 968`) *(76.27% excl. `LyricCanvasControl`)* | `>= 80%` (`QA-02`/`QA-04`) | **FAIL** |
| `LyricPulse.App` Coverage *(informational — Win32/Avalonia shells)* | `40.49%` line (`758 / 1,872`), `40.00%` branch (`266 / 665`) | Pure logic tested (`Bounds`/`Hotkeys`/`Tray`/`Guard`) | **INFO** |
| `LyricPulse.Core` SkiaSharp / Avalonia / HarfBuzz Isolation | **0 / 17** libraries (`project.assets.json`) | Strictly 0 direct or transitive | **PASS** |
| Forbidden API Scan (`src/**/*.cs`) | **0 hits** (`[DllImport]`, `SKTypeface.FromStream`, `SKPaint.TextSize`, `SKFilterQuality`) | Strictly 0 (`RND-02`, AOT rules) | **PASS** |

#### `LyricPulse.Rendering` Per-File Coverage Breakdown (`371bfa5`)

| Source File | Line Coverage | Lines | Branch Coverage | Branches |
|---|---:|---:|---:|---:|
| `Color/SkColorExtensions.cs` | 100.00% | 7 / 7 | 50.00% | 1 / 2 |
| `Color/SkiaAlbumColorExtractor.cs` | 82.05% | 64 / 78 | 69.44% | 25 / 36 |
| `Fonts/FontAssetRegistry.cs` | 92.69% | 241 / 260 | 76.74% | 66 / 86 |
| `Fonts/LyricFontId.cs` | 100.00% | 8 / 8 | 100.00% | 0 / 0 |
| `Fonts/LyricPulseFontCollection.cs` | 100.00% | 5 / 5 | 100.00% | 0 / 0 |
| `Fonts/TextShaperService.cs` | 85.19% | 46 / 54 | 63.33% | 19 / 30 |
| `Host/FrameBudgetTelemetry.cs` | 100.00% | 85 / 85 | 87.50% | 21 / 24 |
| `Host/FrameRenderPacket.cs` | 100.00% | 47 / 47 | 100.00% | 0 / 0 |
| `Host/LyricCanvasControl.cs` | 47.71% | 104 / 218 | 22.73% | 15 / 66 |
| `Host/OffscreenSurfacePool.cs` | 87.14% | 61 / 70 | 61.76% | 21 / 34 |
| `Host/OverlayFrameAdapter.cs` | 100.00% | 31 / 31 | 62.50% | 5 / 8 |
| `Host/SolidDiagnosticSceneRenderer.cs` | 98.80% | 82 / 83 | 90.91% | 20 / 22 |
| `Legibility/LegibilityPipeline.cs` | 92.78% | 167 / 180 | 65.79% | 25 / 38 |
| `Legibility/LegibilityStyleSpec.cs` | 92.31% | 48 / 52 | 71.43% | 15 / 21 |
| `Motion/Ease.cs` | 93.75% | 30 / 32 | 75.00% | 9 / 12 |
| `Motion/SpringParams.cs` | 94.44% | 17 / 18 | 66.67% | 4 / 6 |
| `Motion/SpringState.cs` | 96.77% | 60 / 62 | 86.36% | 19 / 22 |
| `Motion/SpringTokens.cs` | 100.00% | 7 / 7 | 100.00% | 0 / 0 |
| `RenderingModule.cs` | 100.00% | 1 / 1 | 100.00% | 0 / 0 |
| `RenderingServiceCollectionExtensions.cs` | 100.00% | 15 / 15 | 100.00% | 0 / 0 |
| `Shaders/MotionBlurMath.cs` | 100.00% | 28 / 28 | 88.89% | 16 / 18 |
| `Shaders/ShaderLibrary.cs` | 92.64% | 151 / 163 | 84.29% | 59 / 70 |
| `Styles/CodestyleStyleRenderer.cs` | 88.42% | 252 / 285 | 74.74% | 71 / 95 |
| `Styles/FisheyeStyleRenderer.cs` | 97.02% | 293 / 302 | 84.48% | 49 / 58 |
| `Styles/LyricStyleRouter.cs` | 93.26% | 83 / 89 | 77.78% | 28 / 36 |
| `Styles/PlaybackLifecycleChoreographer.cs` | 82.42% | 211 / 256 | 76.32% | 87 / 114 |
| `Styles/StyleLayoutHelper.cs` | 72.64% | 77 / 106 | 50.00% | 28 / 56 |
| `Styles/VisualStackStyleRenderer.cs` | 94.31% | 348 / 369 | 87.72% | 100 / 114 |
| **Total (`LyricPulse.Rendering`)** | **88.25%** | **2,569 / 2,911** | **72.62%** | **703 / 968** |

---

### 2. Test Rigor & Phase 1–3 Verification Audit

| Audit Dimension | Verification Evidence | Verdict |
|---|---|---|
| **Phase 1 Polish Regressions** | All Phase 1 MEDIUM/LOW items (CJK YRC zero-space cache round-trip, stale `AlbumArtHash` + `Changing` flicker suppression, concurrent `TimingOffsetService` nudges, true singleton `IHttpClientFactory` providers, `"living"` -> positive `life` concept, NetEase `nolyric` -> `Instrumental`, 32-bit overflow guard) verified with passing regression tests in `tests/LyricPulse.Tests/Phase3QaPolishRegressionTests.cs`. | **PASS** |
| **Real Pixel Tests (Fisheye, Codestyle, VisualStack)** | `KineticStylesMvpTests.cs` renders all 3 MVP styles onto `SKSurface` across 9 timestamps on the 36s `Neon Horizon` timeline (with `AllowCpuShadersForTesting = true`), asserts `>150` lit pixels, 3 distinct SHA-256 frame hashes, Reduced Motion, High-Contrast scrim, and compositing onto `#FFFFFF` & `#0D0D14` backdrops. | **PASS** *(gaps logged in HIGH-1)* |
| **SkSL Shader Compile, Convexity & Motion Blur** | `ShaderLibraryTests.cs` compiles both embedded `.sksl` shaders with 0 errors, proves outward convex magnification (`bulgedWidth > 82px` for `80px` source square, no center `pow(0, p)` NaN, transparent corners), verifies 13-tap vertical pixel spread, dead-zone bypass (`<1.25 * dpiScale`), `tanh` ceiling (`<=22 * dpiScale`), and software/Reduced Motion bypass. | **PASS** |
| **Lifecycle Choreography Tests** | `PlaybackLifecycleChoreographerTests.cs` verifies 300ms pause dim, 8.0s->8.45s auto-hide to RAF idle, 450ms vocal pre-wake out of instrumental gap, `HideDuringInstrumentals`, 1.2s offset toast with Unicode `−` (`U+2212`), and non-empty rendered pixels. | **PARTIAL** *(spec value mismatch in MED-1; `!hasRenderableLyrics` fade untested in HIGH-1)* |
| **TrayMenuState & Adaptive TrayIcon** | `TrayAndLaunchTests.cs` verifies live/demo/idle/hidden/error states, `<=38` char middle-ellipsis header, `<=96` char tooltip, zero emojis in chrome, pinned/unpinned radio sync, `U+2212` offset labels, structural equality, and 8 distinct valid PNGs across 4 states × Dark/Light taskbar polarity. | **PASS** |
| **Zero Skipped / Flaky Tests** | 0 `Skip=` attributes, 0 `Thread.Sleep` calls, 0 live network dependencies; `OverlayRuntimeControllerTests.cs:162` uses a cancellable `Task.Delay` cancelled deterministically via `CancellationToken`. | **PASS** |
| **Forbidden APIs & AOT Safety** | 0 `[DllImport]` (100% `[LibraryImport]` in `Win32Native.cs`), 0 `SKTypeface.FromStream` (`SKData.Create` -> `SKTypeface.FromData`), 0 `SKPaint.TextSize`, 0 `SKFilterQuality`, 0 trim/AOT/single-file warnings in Release. | **PASS** |

---

### 3. Severity-Classified Findings (`371bfa5` — Read-Only Audit)

#### CRITICAL Findings

*None.*

#### HIGH Findings (Milestone 1A Gate Blockers)

1. **[HIGH] `LyricPulse.Rendering` Branch Coverage (`72.62%`) Below `>= 80%` Gate + False-Green 64 MP Decompression-Bomb Test**
   - **Locations:**
     - `src/LyricPulse.Rendering/` (`72.62%` branch, `703 / 968`; `76.27%` even excluding `Host/LyricCanvasControl.cs`)
     - `tests/LyricPulse.Tests/Rendering/SkiaAlbumColorExtractorTests.cs:83-86,125-145` vs. `src/LyricPulse.Rendering/Color/SkiaAlbumColorExtractor.cs:62-65`
     - `src/LyricPulse.Rendering/Styles/StyleLayoutHelper.cs:149-157` (`50.00%` branch)
     - `src/LyricPulse.Rendering/Styles/PlaybackLifecycleChoreographer.cs:67-93,125-139` (`76.32%` branch)
     - `src/LyricPulse.Rendering/Styles/CodestyleStyleRenderer.cs:179-190,223-227,267-271,339-340` (`74.74%` branch)
     - `src/LyricPulse.Rendering/Host/OffscreenSurfacePool.cs:48-54` (`61.76%` branch)
   - **Details:**
     - `LyricPulse.Rendering` meets the line coverage gate (`88.25%`) but fails the `>= 80%` branch coverage gate (`72.62%`).
     - `SkiaAlbumColorExtractorTests.BuildSyntheticPngHeader(10_000, 10_000)` omits an `IDAT` chunk so `SKCodec.Create` returns `null` at `SkiaAlbumColorExtractor.cs:48` before line `62` executes; lines `63-65` (the 64 MP decompression-bomb guard) have **0 hits**, producing a false-green security test.
     - Untested production branches include: Emoji fallback rendering in `StyleLayoutHelper.DrawVisualGlyph` (`lines 149-157`), `!hasRenderableLyrics` fade-out and `Reset()` in `PlaybackLifecycleChoreographer` (`lines 67-93, 125-139`), Codestyle ghost next-line preview / `ScrimIntensity.Off|Subtle|Strong` / empty-words plain line / cursor blink-off (`lines 179-190, 223-227, 267-271, 339-340`), `LyricStyleRouter.Render` drawing instrumental indicators (`lines 113-115`), and `OffscreenSurfacePool.EnsureContextCurrent` purge path.
   - **Remediation:** Fix the synthetic PNG bomb fixture (or use a valid minimal BMP/PNG header accepted by `SKCodec.Create` so `codec.Info` reports `10_000x10_000`), add targeted branch/regression tests across the uncovered paths above, and isolate/annotate thin Avalonia visual-tree/lease shell methods on `LyricCanvasControl` so `LyricPulse.Rendering` reaches `>= 80%` branch coverage cleanly.

2. **[HIGH] Unsynchronized Cross-Thread State Mutation & Native Teardown Between Avalonia UI Thread (RAF) and Compositor Render Thread**
   - **Locations:**
     - `src/LyricPulse.Rendering/Styles/VisualStackStyleRenderer.cs:25,58-66,141-147,320-325` (`_rowScratch`)
     - `src/LyricPulse.Rendering/Styles/LyricStyleRouter.cs:23,62-121` (`_lastLifecycle`, `renderer.ResetState()`)
     - `src/LyricPulse.Rendering/Styles/FisheyeStyleRenderer.cs:22-25,46-122,200,245,281-344`
     - `src/LyricPulse.Rendering/Styles/CodestyleStyleRenderer.cs:33-36,49-97,145-148,337`
     - `src/LyricPulse.Rendering/Host/LyricCanvasControl.cs:91-109,128-135,197-212,253-295,318-337`
     - `src/LyricPulse.App/App.axaml.cs:317-353`
   - **Details:**
     - `LyricCanvasControl.OnAnimationFrame` calls `_renderer.AdvancePhysics(in packet)` on the **UI thread**, while `LyricDrawOperation.Render` calls `renderer.Render(in renderCtx)` on Avalonia's **Compositor/Render thread** concurrently with zero synchronization or immutable physics snapshotting.
     - `VisualStackStyleRenderer` clears and repopulates the same `List<VisualStackRow> _rowScratch` in both `Advance()` (UI thread, line 58) and `Render()` (Render thread, lines 141, 325), risking `ArgumentOutOfRangeException`, `IndexOutOfRangeException`, or corrupted list state during live playback.
     - `LyricStyleRouter` mutates multi-field struct `_lastLifecycle` (torn-read risk) and calls `renderer.ResetState()` on the UI thread while the render thread is actively reading `_lastLifecycle` and per-style spring fields.
     - `LyricCanvasControl.Dispose()` (`lines 96-109`), `OnDetachedFromVisualTree()` (`lines 128-135`), and `ReleaseGpuResources()` (`lines 91-94`) run on the UI thread and dispose `_surfacePool` (`SKSurface`) and `_legibility` (`SKPaint`) immediately while an in-flight `LyricDrawOperation` may still be executing on the Compositor thread (native use-after-free / `ObjectDisposedException` crash risk).
     - `App.CleanupDesktopHost()` (`lines 317-353`) disposes `_runtime` before disposing `_overlayCoordinator`, even though live overlay canvas packet factories capture `runtime` and invoke `runtime.SampleFrame()`.
   - **Remediation:** Synchronize physics/render state (either snapshot immutable per-frame physics state on UI thread into `LyricDrawOperation` or guard `AdvancePhysics`/`Render`/teardown with a lightweight per-canvas render lock, and defer GPU surface release/disposal until no `LyricDrawOperation` holds the lock). Keep `_rowScratch` separate per thread or local/snapshotted. Dispose `_overlayCoordinator` before `_runtime` during shutdown.

3. **[HIGH] Native Resource & Root DI Container Leaks: Undisposed `LyricStyleRouter`, Per-Frame `SKRoundRect`, and Unowned Fallback `SKTypeface`**
   - **Locations:**
     - `src/LyricPulse.App/App.axaml.cs:137` + `src/LyricPulse.Rendering/RenderingServiceCollectionExtensions.cs:28-31` + `src/LyricPulse.Rendering/Host/LyricCanvasControl.cs:96-109`
     - `src/LyricPulse.Rendering/Styles/CodestyleStyleRenderer.cs:143`
     - `src/LyricPulse.Rendering/Fonts/FontAssetRegistry.cs:203-206,235-282`
   - **Details:**
     - **Root DI transient `IDisposable` leak + missing disposal:** `ILyricSceneRenderer` (`LyricStyleRouter`), `IKineticStyleRenderer`s, and `PlaybackLifecycleChoreographer` are registered `AddTransient` and implement `IDisposable` (owning 12+ native `SKPaint`s). `App.CreateOverlayCanvas` (`line 137`) resolves `ILyricSceneRenderer` directly from the root `ServiceProvider` (`_services`), causing MS.DI to retain strong references to every transient `IDisposable` for the lifetime of the process; furthermore, `LyricCanvasControl.Dispose()` never disposes `_renderer`. Each monitor plug/unplug or display toggle leaks native `SKPaint` handles and managed renderer graphs.
     - **Per-frame native `SKRoundRect` leak:** `CodestyleStyleRenderer.cs:143` calls `context.Canvas.ClipRoundRect(new SKRoundRect(cardRect, 14f, 14f), antialias: true);` without `using` disposal. `SKRoundRect` wraps a native `sk_rrect_t*`, leaking 60–144 native instances/sec until finalizer cleanup.
     - **Fallback `SKTypeface` native leak + cache eviction race:** `FontAssetRegistry.MatchFallbackForCodepoint` (`line 238`) returns a newly allocated native `SKTypeface` from `SKFontManager.Default.MatchCharacter(codepoint)` (or registry-owned `_fallbackEmoji`) without caching or disposal ownership; callers of `ResolveTypefaceForText` (`lines 245-282`) cannot safely dispose the return value, leaking native `SKTypeface` handles on non-Latin glyphs. Additionally, `GetOrCreateFont` (`lines 203-206`) disposes all cached `SKFont` instances via `ClearFontCacheLocked()` when count reaches 64, which can dispose an `SKFont` actively in use by another monitor/thread.
   - **Remediation:** Construct per-window `LyricStyleRouter` via a non-root-tracked factory or per-overlay `IServiceScope` and dispose it in `LyricCanvasControl.Dispose()` (under render synchronization). Wrap `new SKRoundRect(...)` in `using` (or reuse a disposable field cleared/set via `SetRectRadii`). Cache fallback `SKTypeface` instances inside `FontAssetRegistry` keyed by family/codepoint (disposing them only on registry `Dispose()`), and avoid disposing in-use `SKFont` entries under active readers.

4. **[HIGH] Live `AnchorZone`, `SafeMarginPx`, and `PerDisplayBounds` Settings Changes Ignored on Existing Overlays**
   - **Locations:**
     - `src/LyricPulse.App/App.axaml.cs:132-157,288-294`
     - `src/LyricPulse.App/Platform/MultiMonitorOverlayCoordinator.cs:126-133`
     - `src/LyricPulse.App/Platform/OverlayWindow.cs:56-64`
   - **Details:**
     - `App.CreateOverlayCanvas` (`line 155`) captures `placement.BoundsConfig` by closure at window creation time and passes it as `boundsOverride` on every frame.
     - When settings update, `MultiMonitorOverlayCoordinator.ReconcileNow()` (`line 128`) reuses existing `OverlayWindow`s and calls `existing.ApplyPlacement(placement)`, which only updates window `Position`/`Width`/`Height` — never updating the frozen `boundsOverride`. Consequently, changing `AnchorZone`, `SafeMarginPx`, or `PerDisplayBounds` has zero effect on rendered lyric placement until the monitor is removed or the app restarts.
     - Additionally, `App.ShouldReconcileOverlayLayout` (`line 294`) only checks `previous.PerDisplayBounds.Count != current.PerDisplayBounds.Count`, missing value changes to an existing display's bounds entry.
   - **Remediation:** Store the latest `OverlayBoundsConfig` on `OverlayWindow` (updated in `ApplyPlacement`, read atomically via `Volatile.Read` inside `packetFactory`) and compare `PerDisplayBounds` key/value equality in `ShouldReconcileOverlayLayout`.

---

#### MEDIUM Findings

5. **[MEDIUM] Pause Dim Opacity (`0.45f`) Diverges from Locked Spec (`0.62f`) & Live Track Switches Flash Amber `Error` Tray Icon**
   - **Locations:**
     - `src/LyricPulse.Rendering/Styles/PlaybackLifecycleChoreographer.cs:32,42` (spec: `design-system/lyricpulse/MOTION_AND_STYLES.md:641`, `.planning/REQUIREMENTS.md:64` `STY-04`)
     - `src/LyricPulse.Core/Runtime/OverlayRuntimeController.cs:382` + `src/LyricPulse.App/Tray/TrayMenuState.cs:259`
   - **Details:** `MOTION_AND_STYLES.md:641` and `STY-04` specify pausing dims to `0.62×` over `300ms`, whereas `PlaybackLifecycleChoreographer.cs:42` uses `PauseDimOpacity = 0.45f`. Separately, `OverlayRuntimeController.OnMediaTrackChanged` (`line 382`) sets `LiveLyricsStatus = LyricsStatus.NotFound` (redundant ternary `track is null ? NotFound : NotFound`) while lyrics are still fetching asynchronously, causing `TrayMenuState.ResolveIconState` (`line 259`) to flash the amber `Error` icon and `"No synced lyrics found"` on every track change.

6. **[MEDIUM] Silent Unobserved Exceptions in Fire-and-Forget Host/Tray Tasks, Unsynchronized `TrayController._lastState`, and Over-Broad `AggregateException` Catch**
   - **Locations:**
     - `src/LyricPulse.App/App.axaml.cs:93,96,106-130,167,178,207,217-241,251-269`
     - `src/LyricPulse.App/Tray/TrayController.cs:44-60,102-190,193-207`
     - `src/LyricPulse.App/Platform/FullscreenAutoHideMonitor.cs:67-70`
   - **Details:** Fire-and-forget `_ = ...` calls in `App` and `TrayController` only catch `OperationCanceledException` (and `ObjectDisposedException`), leaving `IOException` or unexpected errors unobserved and unlogged. `App.OnRuntimeStateChanged` (`line 167`) invokes `_trayController.ApplySnapshot` on a background thread while `ApplySnapshotToHost` (`line 178`) invokes it on the UI thread, racing on unsynchronized `_lastState` (`TrayController.cs:53-59`). `FullscreenAutoHideMonitor.Stop()` (`line 67`) catches bare `AggregateException` even though `PollLoopAsync` already handles `OperationCanceledException`, masking real probe/handler errors.

7. **[MEDIUM] Non-Latin / Multi-Script Lyrics Bypass Fallback Typeface Resolution and HarfBuzz Shaping in Active Renderers (`RND-02`)**
   - **Locations:**
     - `src/LyricPulse.Rendering/Legibility/LegibilityPipeline.cs:144-203`
     - `src/LyricPulse.Rendering/Styles/FisheyeStyleRenderer.cs:136,374-384`
     - `src/LyricPulse.Rendering/Styles/CodestyleStyleRenderer.cs:111,386-395`
     - `src/LyricPulse.Rendering/Styles/VisualStackStyleRenderer.cs:150,409-434`
   - **Details:** `FontAssetRegistry.ResolveTypefaceForText` and `TextShaperService.ShapeToBlob` are tested in isolation but never invoked by `LegibilityPipeline.DrawLegibleText` or the 3 MVP renderers. Non-Latin live lyrics (CJK/Arabic/Devanagari/Emoji via NetEase/LRCLIB) drawn via primary bundled Latin TTFs will render tofu boxes until fallback shaping is wired into the draw path (with cached fallback typefaces per HIGH-3).

8. **[MEDIUM] Singleton `ShaderLibrary` Shared Mutable Uniforms/Paint Lack Locking & Null-Snapshot Guard**
   - **Locations:** `src/LyricPulse.Rendering/Shaders/ShaderLibrary.cs:21-29,136-178,208-243,286-299`
   - **Details:** Mutable `_bulgeUniforms`, `_bulgeChildren`, `_motionBlurUniforms`, `_motionBlurChildren`, and `_blitPaint` are shared across all monitors/canvases via DI singleton without synchronization (unsafe once Phase 5 Gallery previews or concurrent renders run). Additionally, `offscreenSurface.Snapshot(...)` (`lines 136, 208`) is not checked for `null` before `.ToShader(...)` / `BlitUnshaded(...)` (TDR/device-lost risk).

9. **[MEDIUM] `GlobalHotkeyManager` `ManualResetEventSlim` Disposal Race on Timeout & Unsynchronized Shutdown Flags**
   - **Locations:** `src/LyricPulse.App/Platform/GlobalHotkeyManager.cs:24-25,54-68,101-112,115-130,229`
   - **Details:** If `done.Wait(2s)` times out in `TryRegister`/`UnregisterAll`, `using var done` disposes the event while `HotkeyCommand` remains queued; `DrainCommandsOnMessageThread` (`line 229`) then calls `cmd.Completion.Set()`, throwing an unhandled `ObjectDisposedException` on the dedicated pump thread and killing all global hotkeys. `_disposed` and `_messageHwnd` also lack `volatile`/`Volatile.Read`.

---

#### LOW Findings

10. **[LOW] `HotkeyGestureParser.Tokenize` Cannot Parse Trailing Plus Key (`"Ctrl+Shift++"`)**
    - **Location:** `src/LyricPulse.App/Platform/HotkeyGestureParser.cs:83`
    - **Details:** Preceding `'+'` sets `currentStart = i + 1`, so at the trailing `'+'` (`i == input.Length - 1`), `i > currentStart` is `false` (`i == currentStart`), dropping the `+` key token.

11. **[LOW] Redundant Dual Fullscreen Polling Timers & Unsynchronized Demo Cache / Dispose Race in `OverlayRuntimeController`**
    - **Locations:** `src/LyricPulse.App/App.axaml.cs:82-87`, `src/LyricPulse.Core/Runtime/OverlayRuntimeController.cs:102-106,127-128,316-332,609-620`
    - **Details:** `FullscreenAutoHideMonitor` polls every `1.0s` while `OverlayRuntimeController` spawns a redundant `1.5s` `Timer` polling `RefreshFullscreenState()`, which does not check `_disposed` and can fire `StateChanged` during teardown. `GetOrCreateAnnotatedDemoDocument` (`lines 609-620`) also reads/writes two separate cache fields without lock synchronization against `SampleFrame()`.

12. **[LOW] Minor Robustness & Index-Packing Hygiene**
    - `src/LyricPulse.Rendering/Styles/VisualStackStyleRenderer.cs:66` & `src/LyricPulse.Rendering/Styles/StyleLayoutHelper.cs:78`: Integer packing `(line * 100) + row` / `(line * 1000) + word` should use `(int Line, int Index)` tuples to prevent collisions on long lines.
    - `src/LyricPulse.App/Platform/MultiMonitorOverlayCoordinator.cs:139`: Guard against duplicate `DisplayKey` overwriting an existing undisposed `OverlayWindow`.
    - `src/LyricPulse.App/Tray/TrayMenuState.cs`: `All Styles ▸` submenu (`UI-01`) deferred cleanly to Phase 4/5 when >3 styles exist.


---

### Milestone 1A Re-Verification (`c6420bb`)

- **Audited Commits:** `70876e0` (Core) + `c6420bb` (Rendering/App) on `master` (`.NET 10.0.404` SDK, READ-ONLY on `src/`)
- **Re-Verification Verdict:** **PASS** (**0 CRITICAL, 0 HIGH blockers remaining**; all 4 HIGH blockers and all 5 MEDIUM findings from `371bfa5` remediated and verified)

#### Updated Verification Metrics (`371bfa5` → `c6420bb`)

| Verification Gate | Baseline (`371bfa5`) | Re-Verified (`c6420bb`) | Target | Status |
|---|---:|---:|---|---|
| `dotnet build LyricPulse.sln -c Debug --no-restore` | 0 warnings, 0 errors | **0 warnings, 0 errors** (`44.75s`) | 0 warnings / 0 errors | **PASS** |
| `dotnet build LyricPulse.sln -c Release --no-restore` | 0 warnings, 0 errors | **0 warnings, 0 errors** (`16.08s`) | 0 AOT/trim/single-file warnings | **PASS** |
| `dotnet test --no-build` (offline deterministic) | 234 passed, 0 failed | **262 passed, 0 failed, 0 skipped** (`52.04s`) | 100% pass, 0 skipped | **PASS** |
| Release `LyricPulse.App.exe --smoke-test` | Exit 0 (`[PASS]`) | **Exit 0 (`[PASS]`)** | Clean headless startup | **PASS** |
| `LyricPulse.Core` Line / Branch Coverage | `96.59%` / `86.58%` | **`96.13%`** (`4,208/4,377`) / **`86.05%`** (`1,574/1,829`) | `>= 80%` line & branch | **PASS** |
| `LyricPulse.Rendering` Line / Branch Coverage | `88.25%` / `72.62%` (*FAIL*) | **`94.66%`** (`3,085/3,259`) / **`83.57%`** (`926/1,108`) | `>= 80%` line & branch | **PASS** |
| `LyricPulse.App` Line / Branch *(info)* | `40.49%` / `40.00%` | `40.47%` (`875/2,162`) / `43.09%` (`343/796`) | Pure logic tested | **INFO** |
| `LyricPulse.Core` Skia / Avalonia / HarfBuzz Refs | 0 / 17 libraries | **0 / 17 libraries** | Strictly 0 | **PASS** |
| Forbidden API Scan (`src/**/*.cs`) | 0 violations | **0 violations** | Strictly 0 | **PASS** |

#### Blocker-by-Blocker Closure Proof

1. **`QA-HIGH-1` — CLOSED (`PASS`):**
   - `LyricPulse.Rendering` rose from `88.25%` line / `72.62%` branch to **`94.66%` line / `83.57%` branch** (`+28` new remediation tests, `262/262` passing). Only thin Avalonia visual-tree/lease shell methods on `LyricCanvasControl` carry `[ExcludeFromCodeCoverage]`, and `coverlet.runsettings` only added generated `AppLogMessages`.
   - `SkiaAlbumColorExtractorTests.cs:85-95` now probes a valid `9,000x9,000` (`81 MP`) BMP header accepted by `SKCodec.Create`; Cobertura confirms **`SkiaAlbumColorExtractor.cs:62` (`100% 2/2` branch)** and **`lines 63-65` (`hits = 1`)** genuinely execute the `> 64 MP` decompression-bomb guard.
   - `StyleLayoutHelper` reached `100.00%` line / `96.77%` branch, `CodestyleStyleRenderer` `96.60%` / `86.67%`, `PlaybackLifecycleChoreographer` `95.65%` / `84.17%`, `LyricStyleRouter` `94.23%` / `82.50%`, `LegibilityPipeline` `93.94%` / `81.94%`, `OffscreenSurfacePool` `94.20%` / `73.53%`.

2. **`QA-HIGH-2` — CLOSED (`PASS`):**
   - `LyricCanvasControl.cs:30,76,113,124,270,292` synchronizes `AdvanceAnimationFrame`, `RenderSynchronized`, `ReleaseGpuResources`, `SetRenderer`, and `Dispose` under `_renderSync`.
   - `LyricStyleRouter.cs:20,62,76,109,151` guards `LastLifecycleState`, `AdvancePhysics`, `Render`, and `Dispose` under `_sync` (and preserves spring velocity on `OffsetGeneration` nudges).
   - `VisualStackStyleRenderer.cs:32-33` splits `_advanceRows` and `_renderRows` (zero shared-list mutation).
   - `App.axaml.cs:436-519` (`CleanupDesktopHost`) uses `Interlocked.Exchange(ref _disposed, true)`, disposes `_trayController`, `_overlayCoordinator`, `_fullscreenMonitor`, and `_hotkeys` FIRST on UI thread (halting RAF/`SampleFrame()`), then stops/disposes `_runtime` and `_services` off-UI-thread via `Task.Run` with a bounded `1500ms` timeout.

3. **`QA-HIGH-3` — CLOSED (`PASS`):**
   - `CodestyleStyleRenderer.cs:32-33,152-153,219-220,366` reuses field-owned `_clipRoundRect` (`SetRectRadii`) and `_cursorBlur` (`SKImageFilter`) disposed in `Dispose()`; `LegibilityPipeline.cs:24,248-264,275` caches glow blur filters in `_blurCache` (zero per-frame `SKRoundRect` or `SKImageFilter` allocations).
   - `FontAssetRegistry.cs:90-94,210-219,305-338,430-467` caches and owns all fallback `SKTypeface`s in `_ownedFallbackTypefaces` (disposed on registry `Dispose()`), retires evicted `SKFont`s into `_retiredFonts` instead of mid-frame disposal, and wires HarfBuzz fallback shaping into `LegibilityPipeline.DrawLegibleText`.
   - `ILyricSceneRendererFactory` (`src/LyricPulse.Rendering/Host/ILyricSceneRendererFactory.cs`) constructs per-overlay `LyricStyleRouter`s outside root MS.DI tracking; `App.axaml.cs:194-217` passes `ownsRenderer: true` and `LyricCanvasControl.Dispose()` (`lines 136-139`) disposes `_renderer` cleanly under `_renderSync`.

4. **`QA-HIGH-4` — CLOSED (`PASS`):**
   - `OverlayWindow.cs:21,63,68` maintains `CurrentBoundsConfig` via `Volatile.Read`/`Volatile.Write` updated on every `ApplyPlacement` call; `App.axaml.cs:189-216` reads `overlayWindow.CurrentBoundsConfig` dynamically inside `packetFactory`.
   - `App.axaml.cs:61-98` (`ShouldReconcileOverlayLayout` / `ArePerDisplayBoundsEqual`) performs full key/value equality across `PerDisplayBounds` (verified in `Milestone1ARemediationTests.cs:389-401`).

#### Non-Blocking Phase 4/5 Hygiene Notes (LOW)
- `src/LyricPulse.Rendering/Fonts/FontAssetRegistry.cs:518`: Wrap one-time startup `new SKFontStyle(...)` in `using` (`SKFontStyle` is `IDisposable`).
- `src/LyricPulse.Rendering/Fonts/FontAssetRegistry.cs:265-281` & `TextShaperService.cs:54-55`: Before adding concurrent Phase 5 Gallery thumbnail renderers, guard shared cached `SKShaper` instances against multi-threaded `Shape()` calls (currently safe because all `MeasureWidth`/`ShapeToBlob` calls run serialized on Avalonia's single Win32 compositor thread).
