# Tactile Issues

Native, fast-starting versions of the Tactile Issue Tracker note taker. They use the same look and the same Supabase data as the original web app.

| Folder | What | Build |
|---|---|---|
| [`windows/`](windows/) | WPF app on the built-in .NET Framework 4.8. One ~180 KB exe with no installer runtime. | `windows\build.cmd` |
| [`android/`](android/) | Kotlin + Jetpack Compose app. Works offline first, has swipe actions, and accepts shared content. | `android\gradlew.bat assembleRelease` |

[`ANDROID_PLAN.md`](ANDROID_PLAN.md) is the original design plan for the Android app.

## Keys

No keys are stored in this repo:

- **Windows:** reads them from `windows/config.json`. Copy `config.example.json` to create it.
- **Android:** reads them from `android/local.properties` at build time. Copy `local.properties.example` to create it.

Both files are git-ignored.

## Downloads

The APK on the [Releases](../../releases) page is built **without a Gemini key**, so long prompts are saved as plain text instead of being turned into tasks. To get AI prompt mode, build the app yourself with your own key in `local.properties`.
