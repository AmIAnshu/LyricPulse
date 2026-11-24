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
