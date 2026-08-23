# ReplayTimerMod — Full Verification Checklist

The definitive pre-release checklist. Phase 0 is automated; everything else is
manual, ordered so earlier failures invalidate later sections (don't test
leaderboards before uploads work). The **Modifiers** section is the deep-dive
for the newest feature.

Platform key — run platform-tagged items on each: **[SS]** Silksong,
**[HK78]** Hollow Knight 1.5.78, **[HK21]** Hollow Knight 1.2.2.1,
**[ALL]** all three.

---

## Phase 0 — Automated (run before any manual testing)

- [ ] `dotnet test tests/ReplayTimerMod.Tests/ReplayTimerMod.Tests.csproj` — all green.
      Covers: RTM3/RTMX encode-decode, FrameCodec, ApiJson/JsonText, NameValidator,
      ModifierMask semantics, RouteView collapse/filter/re-rank, LeaderboardCache,
      PBManager (PB / mask-PB / prune / import), DataStore persistence,
      GhostSettings, UploadWorker retry/backoff, ReplaySharing codes, TimeUtil.
- [ ] `deno test --allow-read supabase/functions/tests/` — all green.
      Covers: server validators (device id, run payload, display name) and the
      RTM3 wire-format contract (real C# encoder blobs → `verifyReplayConsistency`).
- [ ] `dotnet build ReplayTimerMod.slnx -c Release` — all three plugins + tests compile, 0 warnings.
- [ ] If you changed `NameValidator.cs` or `validate.ts`: word lists/rules updated **in both**, and `tests/shared/name-cases.json` extended to cover the change.
- [ ] If you changed the RTM3 format intentionally: `RTM_WRITE_FIXTURES=1 dotnet test ...`, commit the regenerated `tests/shared/rtm3-fixtures.json`, and re-run the Deno suite.

**Known automated-coverage gaps (must be verified manually below):** everything
Unity-side (RoomTracker, FrameRecorder, GhostPlayback, LoadRemover, HUD, UI
widgets, GameHooks), `ModifierRegistry` probes (empty table in tests), real
HTTP (`HttpService`), `NetworkClient`'s polling state machine, and the actual
Postgres RPCs.

---

## Phase 1 — Backend deploy state (BEFORE any online testing)

The repo and the live Supabase project can diverge. Confirm what's live; deploy
what isn't (deploy commands are yours to run — nothing here happens
automatically). Ledger of repo changes possibly awaiting deploy:

- [ ] **Modifiers migration** — `supabase/migrations/20260704120000_modifiers.sql`
      (adds `runs.modifiers` + index, rewrites `upload_run` with `p_modifiers` +
      per-mask prune, rewrites `get_leaderboard` to best-per-(runner, mask) rows
      with `modifiers` + `rid`). Paste into the SQL editor.
- [ ] **Pre-release wipe** (AFTER the migration, BEFORE releasing the modifiers
      client — commented at the bottom of the migration): `TRUNCATE runs, shares,
      replay_objects;` then **bump** (never reset) `scene_versions`/`room_versions`.
- [ ] **Edge functions current**: `supabase functions deploy runs --no-verify-jwt`
      (passes `p_modifiers`, replay verification, no error-message leak),
      `deploy share --no-verify-jwt`, `deploy replay --no-verify-jwt` (binary
      downloads). Deploy order: migration → wipe → deploy runs → release client.
- [ ] Earlier items if never applied: perf migration
      `20260628120000_networking_perf.sql`; quota migration
      `20260629140000_per_device_upload_quota.sql`;
      `supabase functions delete config` / `delete manifest` (dead API);
      optional `DROP TABLE IF EXISTS replay_blobs;`.
- [ ] Sanity-probe the live API with curl (no game needed):
  - [ ] `GET <ApiBaseUrl>/init?game=silksong` → config + scene index in one response.
  - [ ] `GET .../scenes?game=silksong&v=<current>` → tiny `{"v":N}` no-change response.
  - [ ] `POST .../runs` with a junk `replay_data` → rejected (replay verification live).
  - [ ] `POST .../runs` twice within 2 s for one route → second gets HTTP 429 (`rate_limited`).
  - [ ] Response of a valid upload includes `run_id`, `rank`, `total_runners`, `is_pb`.
  - [ ] `GET .../leaderboard?game=&scene=` rows include `modifiers` and `rid` (migration live).

---

## Phase 2 — Install & boot [ALL]

- [ ] **[SS]** Release build produced a Thunderstore zip in `thunderstore/dist/`; DLL lands in the BepInEx plugins folder; game boots with no BepInEx console errors from the mod.
- [ ] **[HK78]/[HK21]** Copy `ReplayTimerMod.HK.dll` into `.../Managed/Mods/ReplayTimerMod`; mod appears in the Mods list; ModLog clean.
- [ ] **ILRepack regression check [HK78] [HK21]** (this exact failure shipped before): build **twice**, then confirm the second build's DLL still has SharpZipLib merged — output size stable, and no `ICSharpCode.SharpZipLib` in
      `[Reflection.Assembly]::LoadFile("<dll>").GetReferencedAssemblies()`. A run must save without `FileNotFoundException` on room exit.
- [ ] First boot creates `<plugin>/ReplayMod/settings.txt` and `<plugin>/ReplayMod/data/` on first save.
- [ ] Legacy data migration: put a scene JSON in the pre-1.0 location → moved into the current data dir on boot, never overwriting a newer file.
- [ ] Version bumped in **both** the `.csproj`(s) and `Directory.Build.props` if releasing.

## Phase 3 — Core loop: timer, recording, ghost [ALL]

- [ ] Enter a room, cross it, exit: HUD shows a running timer that **freezes during loads/menus/cutscenes** (LoadRemover gate) and a finished time on exit.
- [ ] First completion of a route logs `FirstRun` and saves; repeating faster logs `NewPB`; slower with the same loadout logs `MissedPB` and does NOT write to disk (check file mtime).
- [ ] Ghost replays the PB on the next attempt from the same entry; position/facing/animation look right; ghost color/alpha follow Config; per-snapshot color override wins over global.
- [ ] Route identity: same room entered from a different scene, or exited to a different scene, is a **separate** `(scene, entryFrom, exitTo)` route with its own PB/ghost.
- [ ] Death mid-room and re-entry: recording discards cleanly, no stuck timer, next run records normally (GameHooks death path).
- [ ] A run longer than `MAX_ROOM_TIME` (180 s) is discarded.
- [ ] Options behave: TrackingEnabled off → nothing records; GhostEnabled off → no ghost; SaveAllRuns on → slower runs become history; SkipBacktrackRuns/SkipBacktrackTimer suppress exitTo == entryFrom runs/HUD respectively; MultiReplay + chain timers behave as labeled.
- [ ] Settings persist across a full game restart (`settings.txt`, not the BepInEx .cfg).

## Phase 4 — Modifiers (the new feature) 

**Bit-table finalization (blocking for release):**
- [ ] **[SS]** Equip each crest (Hunter incl. v2/v3 upgrades, Reaper, Wanderer, Beast, Witch, Architect, Shaman, Cursed) → correct bit 12–19 set; watch the log for `unmapped crest id` lines — any hit means `CrestIdToBit` needs fixing **before release** (bits are frozen forever once shipped).
- [ ] **[SS]** Equip Silkspeed Anklets → bit 20 sets (asset name "Sprintmaster"); the tracker logs equipped tool names on every change — confirm no movement tool you care about is missing from the alias table.
- [ ] **[SS]** Abilities: Swift Step / Cling Grip / Faydown Cloak / Drifter's Cloak / Clawline / Silk Soar each set bits 0/1/2/8/9/10 by **possession** (unlocked = set, even if unused). Bit 11 (the old usage-based "Sprint") is **retired** — it must never be set on a new run; old masks that carry it still display harmlessly.
- [ ] **[HK78]/[HK21]** Mothwing Cloak / Mantis Claw / Monarch Wings / Shade Cloak / Crystal Heart / Isma's Tear set bits 0/1/2/8/9/10 by possession; charms Sharp Shadow / Dashmaster / Sprintmaster set bits 11/12/13 only while **equipped**.
- [ ] Mid-room pickup/equip: gaining an ability or equipping a charm mid-run ORs the bit in for that run (per-frame poll).

**Local behavior:**
- [ ] Run rows in the Runs tab show the "?" marker; hovering it (marker only, not the row) shows the loadout tooltip; pre-feature replays show "unknown (old recording)".
- [ ] `NewMaskPB`: set a PB with full loadout, then run slower with a different loadout → logged as mask PB, stored, and visible in history alongside the overall PB; HUD delta still compares vs the **overall** PB; ghost still plays the **overall** PB.
- [ ] Prune exemption: with MaxSavedReplaysPerRoute small, each loadout's best survives pruning (capped at 8 distinct masks); restart the game and confirm masks reload from disk correctly (RTMX trailer is the authority over the JSON field).
- [ ] Filters (Runs + Leaderboard tabs): each ability row's Any/With/Without works; crest selector cycles Any → each crest → Any and is single-choice; summary line reflects the state; Reset clears; filter state persists across restart; unknown-mask runs appear only when no filter is active.

**Wire/back-compat (partly covered by tests, spot-check live):**
- [ ] Copy a replay with a known mask → paste into an **old** (pre-modifiers) build → decodes fine (trailer ignored). Paste an old build's replay into the new build → imports as unknown mask.
- [ ] Upload payload contains `modifiers` (BepInEx console / proxy); leaderboard rows come back with `modifiers` + `rid`.
- [ ] Leaderboard shows one row per runner (best across masks) with **contiguous ranks** when unfiltered; applying a filter re-ranks the surviving rows 1..n with no gaps; your own row/rank stays consistent after collapsing.
- [ ] Upload gating: only FirstRun / NewPB / NewMaskPB upload — a slower same-mask run must NOT hit the network.

## Phase 5 — Persistence & data safety [ALL]

- [ ] Data files are one JSON per scene under `ReplayMod/data`; delete snapshot / route / scene / all from the UI updates both memory and disk; deleting the current PB promotes the next best.
- [ ] Corrupt file resilience: hand-mangle one scene JSON (truncate it) → boot skips it with a warning, other scenes load.
- [ ] Import: pasting a blob that already exists reports duplicate; pasting into a full route only keeps it if it ranks inside the window or is a new-mask best.
- [ ] A pre-modifiers `data` dir (from a real old install if available) loads clean; old snapshots show "?" masks and never upload a `-1` mask.

## Phase 6 — Networking [ALL — HK21 is the high-risk platform]

- [ ] Online is **opt-in**: with OnlineEnabled off, zero HTTP happens (watch console/proxy). Enabling it prompts for a display name; nothing starts until a name is set; device id generates once (32 hex) and never changes.
- [ ] Name rules match client & server: try a profane, a reserved ("admin"), a leet-obfuscated, and a fullwidth name → client blocks them; force one past the client (edit settings.txt) → server 422s it.
- [ ] Startup: one `GET /init`; scene list appears in the UI (online indicators). Scene index re-polls ~60 s with menu open / ~120 s in gameplay, sending `&v=` and getting the tiny no-change response when idle.
- [ ] Upload happy path: set a PB → rank toast on the HUD; your entry appears **instantly** in the leaderboard tab (optimistic update, `rid` −1) and survives the next real poll; `OnRunIdAssigned` fills the snapshot's server run id (share-by-pointer becomes instant).
- [ ] Leaderboard polling: viewing a room polls 5–12 s adaptively; two clients open on the same room see each other's new times within ~10 s; in-flight requests dedupe (no request pileup on slow connections).
- [ ] Failure handling: kill the network → ConnectionHealth degrades (log), poll intervals stretch, at 8 failures polling stops but uploads still retry with backoff (2/4/8/16/32 s, drop after 5); restore network → one success snaps everything back; queued PBs upload.
- [ ] Rate limit / quota: two PBs on one route within 2 s → second 429s and the client handles it quietly.
- [ ] Name change: `POST /set-name` → `ForceRefreshAll` → other client sees the new name on its next poll.
- [ ] Maintenance mode: set `MAINTENANCE_MODE` env on the functions → client stops uploads/polls and shows the announcement; unset restores.
- [ ] **[HK21] specifically** (worst TLS/HTTP stack): all of the above on real hardware — TLS 1.2 works (UnityWebRequest native stack), POSTs don't hang (chunked-transfer disabled), timeouts abort properly, and no `TypeInitializationException` from any share/copy action (net35 Regex.Compiled regression).

## Phase 7 — Sharing & downloads [ALL]

- [ ] Share your uploaded PB → bare short code on the clipboard (Mode A, instant when run id cached). Share a never-uploaded local replay → Mode B still mints a code.
- [ ] Paste a bare code, a `.../r/CODE` URL, and a `?code=CODE` URL → all resolve and import; junk text and raw blobs fall through to the blob importer, never misparsed as codes.
- [ ] Re-sharing the same replay returns the **same** code (idempotent).
- [ ] Download a leaderboard replay → ghost of another runner plays; raw octet-stream path works on all three platforms (`GET /replay`, `GET /share`).
- [ ] Cross-game guard: a code minted for HK doesn't import into Silksong (or is clearly rejected).
- [ ] Shared-replay GC: prune a shared run (upload enough newer PBs) → the share code still resolves (shares hold a ref-count on `replay_objects`).
- [ ] Hostile import: paste random garbage, a truncated blob, and a huge string → warning logs only, no crash, no partial state (bomb caps: 64 MB inflate, 20 k frames, 16 MB share string).

## Phase 8 — UI & cheat detection [ALL]

- [ ] Pause-menu panel opens/closes cleanly on all three (uGUI paths differ); tabs: Runs / Leaderboard / Config / SceneList all render; rebuilds happen on demand (no per-frame rebuild jank); scroll position survives filter toggles.
- [ ] Room timer HUD: enabled/disabled from config; chain mode shows two cards and slides correctly.
- [ ] DebugMod (HK): noclip, savestate load, and timescale change each **cancel the in-progress run** (log line); nothing saves or uploads; ghost keeps playing after a cheat-cancel; normal recording resumes next room.
- [ ] Quick warp (if used) doesn't produce a bogus route or time.

## Phase 9 — Release packaging

- [ ] `<Version>` bumped in `ReplayTimerMod.SS.csproj`, both HK csprojs, and `Directory.Build.props`; game tags remain exactly `silksong` / `hk_1578` / `hk_1221` end-to-end.
- [ ] SS Thunderstore zip contains the DLL + manifest/icon/readme; HK Release zip layout `dist/ReplayTimerMod/` correct; HK DLLs pass the double-build ILRepack check (Phase 2).
- [ ] Fresh-machine smoke test: clean game install + the packaged artifact (not your dev copy) → Phase 3 basics pass.
- [ ] Server deployed in order (migration → wipe → functions) **before** the client release goes public; old clients still work against the new server (missing `modifiers` defaults to 0).

---

*Automated suites live in `tests/ReplayTimerMod.Tests` (C#) and
`supabase/functions/tests` (Deno); cross-stack contracts in `tests/shared/`.
See the Testing section of CLAUDE.md for how they're wired.*
