# Tactile Issues for Android: native app plan

**Goal:** a sideloaded native Android app with the same look and the same Supabase data as the web and Windows apps. It should open instantly, work offline, and never lose an edit.

---

## 1. Why the PWA feels slow, and the fix for each cause

| Cause in the PWA | Effect on phone | Native fix |
|---|---|---|
| Startup first calls `/api/extract-title` as a "schema probe" (a Vercel serverless cold start of 1–3 s), *then* fetches Supabase | Blank or stale screen for seconds | No probe. Read the local Room DB, paint, and sync in the background after the first frame |
| `select *` on `issues` downloads every comment and every inline base64 image on every launch | Megabytes per launch on mobile data | Background sync only. New images go to Storage URLs; Coil caches images on disk |
| JS bundle (React + motion + supabase-js + @google/genai) + Google Fonts + service worker | Slow parse/JIT on mid-range phones | Kotlin compiled ahead of time with **Baseline Profiles**, R8, fonts bundled in the APK |
| `localStorage` JSON is re-parsed on the main thread several times at startup | Jank | Room (SQLite), queried off the main thread with Flow |
| Failed writes are silently dropped (`catch {}`) | Edits can be lost offline | A persistent **outbox** table, flushed by WorkManager with retry and backoff |

**Performance budget:** cold start to the first frame of the real list in under 500 ms on a mid-range device (Pixel 6a class), and under 250 ms for a warm start. Measure with Macrobenchmark (`StartupTimingMetric`).

## 2. Stack

| Concern | Choice | Why |
|---|---|---|
| Language/UI | Kotlin 2.x, **Jetpack Compose** (BOM 2026.x), no Material theme components except text fields | Full control over the custom look; springs are built in |
| Local DB | **Room** (KSP) | Instant, typed, observable |
| Network | **OkHttp + kotlinx.serialization**, talking to PostgREST directly | Four endpoints don't justify the supabase-kt dependency, and it keeps the APK small |
| Background sync | **WorkManager** (unique work, `NetworkType.CONNECTED`, exponential backoff) plus an in-process flush while the app is open | Survives the app being killed |
| Images | **Coil 3** (memory + disk cache, `size()` downsampling), Android **Photo Picker** | No storage permission needed |
| Sounds | **SoundPool** with short WAVs (see §7) | Low latency, can overlap |
| DI | Manual (one `AppContainer`) | Hilt adds startup cost; the app is small |
| Startup | Baseline Profile module, `ProfileInstaller`, R8 full mode, no splash delay | Main lever for cold start |
| minSdk / target | 26 / 36 | Covers essentially all current phones |

APK target: under 6 MB.

## 3. Data model and sync protocol (identical to Windows)

Keep exact wire compatibility with the existing Supabase tables, so the web app, the Windows app and Android all share data.

**`issues`:** `id text pk, project_id, section ('prompted'|'drafts'), title, completed bool, active bool, expanded bool, comments jsonb [{id,text,createdAt,images?}], created_at`
**`projects`:** `id, name, color_key, sort_order, created_at`

Room entities mirror these columns. `comments` is stored as a JSON string column because it's always read and written as a whole. Add local-only columns:
- `issues.local_order INT` (the web app keeps manual order in localStorage, Windows keeps it in its own file; it is never synced)
- `outbox(key TEXT PK, seq INT)` with keys `issue:<id>`, `issue-del:<id>`, `project:<id>`, `project-del:<id>`

**Rules** (port these from `windows/src/Store.cs`, where they're already proven by the self-test):
1. Every edit writes to Room and enqueues an outbox key in **one transaction**. A new upsert or delete for the same id replaces the older entry; only the latest intent matters.
2. The flusher reads the *current* row at send time and calls `POST /rest/v1/<table>?on_conflict=id` with `Prefer: resolution=merge-duplicates,return=minimal`, or `DELETE ?id=eq.<id>`. The entry is removed only if its `seq` didn't change while in flight.
3. Network errors, 5xx and 429 keep the entry and retry with backoff. Other 4xx responses drop the entry and log it, so the queue can't get stuck.
4. A pull (`GET issues?select=*`, `GET projects?select=*`) runs only after the outbox is empty. Its result is applied only if no local edit happened while it was in flight (check a change counter). Remote rows replace local rows; `local_order` and the `expanded` view state are kept.
5. Pull triggers: app start (after the first frame), returning to the foreground (throttled to 20 s), pull-to-refresh, and a 15-minute periodic WorkManager job (optional).

**Business logic:** port `windows/src/Model.cs` (itself a 1:1 port of `issuesLogic.ts` / `projectsLogic.ts`) into a pure Kotlin `domain` module: statuses, `nextStatus`, `applyStatus`, `reorder`, `createIssue` (the 100-character prompt mode, fallback title, prompt saved as the first comment), comment and image edits, project colors. Port `src/lib/*.test.ts` as JUnit tests.

## 4. Screens and interactions

There is one screen, as on the web.

```
┌───────────────────────────────┐
│ ● Main 22 │ ● Studio 3 │ ● …  │  ← project chips (LazyRow, snap), dots below
│ ▬ • •                          │
├───────────────────────────────┤
│ [Running ⋯] Title…      ✓ 💬2 │  ← tinted elevated card
│ [▶ Task]   Title…          💬 │
│ [💡 Idea]  Title… (56% alpha)  │
│ …                              │
├───────────────────────────────┤
│ (+) Add a task, prompt…  🖼 💡│  ← composer docked at the BOTTOM above the IME
│                 [✓ Completed 39 ▾]
└───────────────────────────────┘
```

The phone layout differs from desktop in a few deliberate ways:
- The **composer is at the bottom**, within thumb reach. Use `imePadding()` so it rides on top of the keyboard.
- **Completed** opens as a modal bottom sheet instead of an inline drawer.
- **Project chips:** tap selects, tap the dot on the active chip to cycle its color, long-press for a Rename / Color / Delete menu (deleting asks for confirmation, like on Windows). Swiping horizontally on the list area also switches project (`HorizontalPager` with one page per project; it pre-renders neighbors, so switching is instant).
- **Status pill:** tap cycles Task → Running → Idea → Done, exactly as on the web, with haptic feedback (`HapticFeedbackType.SegmentTick`, plus `Confirm` when completing).
- **Swipe actions** on a row: swipe right marks it Done; swipe left deletes it, with an **Undo** snackbar.
- **Reorder:** long-press anywhere on a row to drag (`Modifier.animateItem()` + drag-and-drop in LazyColumn). Dropping onto a row of another status takes on that status (the same `reorder()` rule).
- Tap a title to edit it inline (`BasicTextField`); clearing the title deletes the item, with Undo.
- **Comments drawer:** tap the comment pill to expand it with `animateContentSize` (spring). It shows comments, image thumbnails and an input; tap an image for a full-screen pinch-zoom viewer.
- **Images:** attach from the Photo Picker, paste from the clipboard (`ClipboardManager` image URIs), or **share from any app**.

## 5. Android-only features (they replace the PWA's "Install App" button)

1. **Share target:** an `ACTION_SEND` intent filter for `text/plain` and `image/*`. Sharing a link, text or screenshot from any app opens a small sheet with a project picker and adds it as a task.
2. **App shortcut** (long-press the launcher icon): "New task", which opens with the composer focused.
3. **Quick Settings tile:** "New task".
4. Optional: a **home-screen widget** (Glance) listing the Running items of the active project, with a Done button.

## 6. Matching the look

- **Tokens:** copy `PROJECT_THEMES` (7 colors) into `ProjectTheme.kt`. Animate color changes when the project changes (`animateColorAsState`, 220 ms). The top background gradient goes from `tintBgSubtle` to white over 170 dp, like the web app.
- **Fonts:** bundle Plus Jakarta Sans, Bricolage Grotesque and JetBrains Mono (all free under the OFL) in `res/font`. Android supports variable fonts, so the variable TTFs work directly.
- **Sizes:** the web px values map 1:1 to dp/sp (title 15sp/500, running title 15.5sp/600, pill 11.5sp/600, comment 13.5sp with 22sp line height). Use 48 dp minimum touch targets by padding around the visual pills; don't make the pills themselves bigger.
- **Tactile press:** the pill's 2 dp offset "lip" shadow disappears while pressed, and the pill scales to 0.9 and springs back.
- **Springs:** convert the web's motion values with `dampingRatio = damping / (2·√(stiffness·mass))`:
  - press: stiffness 550, damping 24 → `spring(dampingRatio = 0.51f, stiffness = 550f)`
  - row enter: stiffness 520, damping 34, mass 0.7 → ratio 0.89
  - drawer: stiffness 460, damping 34 → ratio 0.79
- The Running dots wave runs at 0.85 s (`infiniteRepeatable`), staggered by 0.16 s. Respect `Settings.Global.ANIMATOR_DURATION_SCALE == 0` by not animating.

## 7. Sounds

Generate the nine marimba sounds once, using the exact synth from `windows/src/Sound.cs` (sine + an overtone at ×2, 10 ms attack, exponential decay, biquad low-pass, soft clip). Either:
- **(recommended)** export them to `res/raw/*.wav` with a tiny JVM script at build time (about 40 KB in total), or
- render them into memory on first launch on a background thread.

Play them with `SoundPool` (maxStreams 4). Add a mute toggle in the overflow menu, and also honor the system's touch-sounds setting.

## 8. Gemini titles (prompts of 100+ characters)

Call `generativelanguage.googleapis.com/v1beta/models/{model}:generateContent` with the same system instruction and JSON schema as `api/extract-title.ts`. Models, with fallback: `gemini-3.5-flash-lite`, then `gemini-3.1-flash-lite-preview`. While waiting, the title pulses; if the call fails, the local fallback title stays. Put the key in `local.properties`, read it into `BuildConfig`, and **keep it out of git**. Note that a key inside an APK can be extracted, which is fine for a personal sideloaded app. If you ever share the app, move this call behind a Supabase Edge Function.

## 9. Security note (applies to all three apps today)

The `projects` / `issues` tables have an RLS policy of `using (true)` for `anon`. Because the publishable key ships inside the public web app, **anyone who opens the PWA's JS can read and overwrite your notes.** Recommended follow-up, which can be done independently of this plan:
1. Enable Supabase Auth (email magic link or Google), add an `owner uuid default auth.uid()` column, and change the policies to `owner = auth.uid()`.
2. The Android app keeps a session token in EncryptedSharedPreferences / DataStore and sends `Authorization: Bearer <access_token>`. Windows does the same.

Because the sync layer only changes the `Authorization` header, this switch is cheap to make later.

## 10. Project structure

```
android/
  app/                    Compose UI, MainActivity, share-target activity, tile, shortcuts
  core/domain/            pure Kotlin: models + logic (ported from Model.cs) + JUnit tests
  core/data/              Room DB, DAOs, OutboxRepository, SupabaseApi (OkHttp), SyncWorker
  core/designsystem/      tokens, fonts, Pill/TactileButton, springs, sounds
  baselineprofile/        Macrobenchmark + Baseline Profile generator
```

## 11. Milestones

| # | Deliverable | Done when |
|---|---|---|
| 1 | Skeleton + domain module + ported logic tests | `./gradlew test` passes the ported issues/projects tests |
| 2 | Room + outbox + Supabase API + SyncWorker | An instrumented test against `windows/tools/mock_supabase.py` (run with `adb reverse tcp:8765 tcp:8765`) runs the same sequence as `windows/src/SelfTest.cs` and passes |
| 3 | Main screen: carousel, list, pills, composer, completed sheet | Shows real data offline; edits persist across process death |
| 4 | Drawer, comments, images (picker/paste/upload/viewer), Gemini titles | Parity with the web feature list |
| 5 | Swipe actions, long-press reorder, haptics, sounds, theme animations | Everything feels at least as tactile as the web app |
| 6 | Share target, shortcuts, QS tile (optional widget) | Sharing a screenshot from another app creates a task |
| 7 | Baseline Profile, R8, startup benchmark, signed release APK | Cold start < 500 ms on a mid-range device; APK < 6 MB |

## 12. Build and install

1. Install Android Studio (or just the command-line tools + JDK 17).
2. Copy `local.properties.example` to `local.properties` and set `SUPABASE_URL`, `SUPABASE_KEY` and `GEMINI_KEY`.
3. Create a signing key once: `keytool -genkeypair -v -keystore tactile.jks -alias tactile -keyalg RSA -keysize 2048 -validity 10000`
4. Build with `./gradlew :app:assembleRelease`.
5. Install with `adb install -r app/build/outputs/apk/release/app-release.apk`, or copy the APK to the phone and allow "Install unknown apps" for your file manager.

Keep the same keystore for every update; Android rejects updates signed with a different key.

## 13. Launcher icon

Reuse the redrawn icon (`windows/assets/icon.svg`): a coral tactile keycap with a white check and the "Running" wave dots.
- **Adaptive icon:** the background is the coral face gradient (`#FF8059` → `#F2441D`). The foreground is the check and the dots, scaled to fit inside the 66 dp safe zone. The raised lip becomes a 3 dp darker band (`#B8300F`) at the bottom of the background layer.
- **Monochrome layer** (Android 13+ themed icons): the check and dots only.
- Convert the SVG with Android Studio → New → Vector Asset (it supports linear gradients).
