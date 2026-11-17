# Tactile Issues for Android (native)

A Kotlin + Jetpack Compose app with the same look and the same Supabase data as the web and Windows apps.

- **Shows your list immediately.** It reads one small local file and draws the list first; syncing starts after the first frame.
- **Works offline.** Every edit is saved on the phone right away and queued. Queued edits upload when you're back online, even if you close the app first (a WorkManager job finishes the upload).
- **Phone-friendly controls:**
  - the composer sits at the bottom, within thumb reach
  - swipe a row right to mark it **Done**, or left to **Delete** (with Undo)
  - long-press a row and drag to reorder it
  - pull down to sync
  - long-press a project chip to rename, recolor or delete it
- **Share into it from any app.** Share text, a link or a screenshot to **"Add to Issues"**. Long-press the launcher icon for **New task**.

The UI uses the same design values as the web app: colors, the 7 project themes, Plus Jakarta Sans and JetBrains Mono, the springs (converted from motion's stiffness/damping), the Running-dots wave, and the marimba sounds.

## Build

You need JDK 17 and the Android SDK (platform 36, build-tools 35). `local.properties` (git-ignored) holds `sdk.dir` and the keys; see `local.properties.example`.

```bash
gradlew.bat assembleRelease
```

The APK is written to `app\build\outputs\apk\release\app-release.apk`. It's signed with `keystore/tactile-release.jks`, whose password is in `keystore.properties`.

**Back up `keystore/` and `keystore.properties`.** Android only accepts an update if it's signed with the same key as the installed app.

## Install on your phone

- **USB:** enable *Developer options → USB debugging*, then run:
  ```bash
  %LOCALAPPDATA%\Android\Sdk\platform-tools\adb.exe install -r app\build\outputs\apk\release\app-release.apk
  ```
- **Without a cable:** copy the APK to the phone (Drive, Telegram, USB file transfer), open it, and allow "Install unknown apps" for that app when asked.

## Tests

```bash
python ..\windows\tools\mock_supabase.py 8767
```

```bash
gradlew.bat testDebugUnitTest
```

Start the mock backend first, then run the tests. They cover the ported logic (status cycle, reorder, prompt mode, comments, the row format shared with the web app) and an end-to-end sync round-trip: create, edit, delete, cascade delete, "a pull never overwrites an in-flight edit", and the offline queue surviving a restart. They run against the mock, never your real database.

## Differences from ANDROID_PLAN.md

- **Storage:** a single JSON file plus the outbox, the same design proven on Windows, instead of Room. The data is about 40 KB, so this is simpler and loads faster.
- **One app module** instead of four.
- **Not yet built:** the Quick Settings tile, the home-screen widget, a Baseline Profile generated on a device, and pasting images from the clipboard. You can attach images with the picker or share them in from another app.
