# Tactile Issues for Windows (native)

A native WPF rewrite of the Tactile Issue Tracker PWA. Same look, same Supabase data, so it stays in sync with the web app (and later the Android app).

- **No runtime to install.** Built against .NET Framework 4.8, which ships with Windows 10/11. The exe is about 150 KB.
- **Opens in about 0.6–0.9 s** with 78 items (measured on this machine, to the first painted frame). The UI comes from a local cache, so startup never waits on the network.
- **Works offline.** Edits are saved locally right away and queued. They upload when you're back online and aren't lost if the app closes first.

## Build & run

```bash
build.cmd
```

```bash
bin\Tactile.exe
```

`install.cmd` copies the app to `%LOCALAPPDATA%\Programs\TactileIssues` and adds a **Tactile Issues** Start menu shortcut (no admin rights needed). Pin it to the taskbar from there.

The build uses the C# compiler that comes with Windows (`%WINDIR%\Microsoft.NET\Framework64\v4.0.30319\csc.exe`). You don't need Visual Studio, an SDK or Node.

## Configuration

`config.json` sits next to the exe (copied in by `build.cmd`; it's in `.gitignore` because it holds keys). See `config.example.json`.

| key | purpose |
|---|---|
| `supabaseUrl`, `supabaseKey` | same project and publishable key as the web app |
| `supabaseBucket` | storage bucket for pasted images (falls back to inline data URLs, like the web app) |
| `geminiKey`, `geminiModels` | title extraction for long (≥100 character) prompts; the `GEMINI_API_KEY` env var also works |
| `syncEnabled` | `false` = local-only |

Local data is stored in `%LOCALAPPDATA%\TactileTracker\` (`data.json`, image cache, `app.log`).

## What's the same as the web app

The project carousel (color dot cycles the color, double-click renames), Running/Task/Idea/Restore status pill, Done, inline title editing (clear the title to delete), the comments drawer with editing, image paste/attach/remove and a lightbox, the Completed drawer, the Idea toggle, prompt mode with Gemini titles, drag-to-reorder using the grip, per-project color tints and the marimba UI sounds.

## Extras

| shortcut | action |
|---|---|
| `Ctrl+Tab` / `Ctrl+Shift+Tab` (or `Ctrl+PgDn/PgUp`) | next / previous project |
| `Ctrl+N` or `Ctrl+L` | jump to the composer |
| `Ctrl+V` anywhere | paste text or images into the composer |
| `F5` | sync now |
| `Ctrl+M` | mute / unmute sounds |
| `Esc` | close the image preview / leave a field |

- You can drag image files onto the window to attach them.
- Deleting a project that still has items now asks you to confirm first.
- **If sync fails, a card appears in the bottom-right corner.** It shows:
  - why sync failed: no internet, server unreachable (for example, a VPN is needed), server not responding, server error, or the key was rejected
  - when you last synced, and how many changes are waiting
  - a **Retry now** button

  Close the card and a small amber "Not synced" chip stays; click the chip to retry. When sync works again, a short "Back online · synced" message appears. The app also retries as soon as your network changes, for example when a VPN connects.
- A small "Syncing…" chip appears only when a sync is slow.
- Each edit uploads only the item that changed. Remote changes are pulled when the window gets focus (throttled to every 20 s), every 3 min, or when you press F5. A pull never overwrites an edit that is still uploading.

## Fonts

`windows\fonts\` bundles the web app's fonts as static TTFs: Plus Jakarta Sans (Regular/Medium/SemiBold/Bold, from tokotype/PlusJakartaSans) and JetBrains Mono (Regular/Medium/SemiBold, from JetBrains/JetBrainsMono). Both are under the SIL Open Font License; the license files are included. `build.cmd` copies them to `bin\fonts\`, and the app loads them from there without installing them system-wide. Characters these fonts don't have, such as Cyrillic, fall back to Segoe UI. WPF can't use variable-font files, so keep the static ones.

## Tests

`bin\Tactile.exe --selftest --data-dir <dir> --config <cfg>` runs every edit path end-to-end. Point it at the mock backend in `tools/mock_supabase.py`, never at the real database. The last run passed all 27 checks.
