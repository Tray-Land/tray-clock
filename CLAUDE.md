@AGENTS.md

## Tray app conventions

This is a tray-only WinUI 3 app built from the `winui-tray-app` skill scaffold on top of `winapp new`. Load that skill before changing app lifetime, the flyout, the tray icon, polling, settings, or secrets.

- Launch with `winapp run` (or `dotnet run`); never register the package by hand. Use `winapp run --clean` to test first-run behavior.
- `App.xaml.cs` owns the tray icon, single instance, and the only exit path (the Exit menu). There is no main window.
- The flyout (`Views/TrayFlyoutWindow`) is hidden on dismiss and closed after a minute hidden. Anything it starts, it must stop in `FlyoutPage.OnHidden` / `Dispose`.
- Network work runs only while visible, through `Services/ForegroundPoller`. The one exception is background work the tray icon itself displays, and that needs a documented reason.
- App API keys go in `OAuth.resw` (gitignored) and are read through `Services/Secrets`. User tokens go in `Services/CredentialStore`. Nothing secret goes in `SettingsService`.
- The repo is MIT licensed (`LICENSE`). Keep `README.md` current (what it does, screenshot, install, privacy, build from source, license); the `winui-tray-app` skill lists the sections.
- Put pure decision logic (parsing, policies, formatting) in classes with no WinRT dependency, so a plain `net10.0` test project can cover it.
- Releases: bump `Identity/@Version`, run `build-msix-for-gh.ps1`, publish the draft without changing its tag, then run `build-msix-for-gh.ps1 -VerifyPublished`. The script is a copy of the `winui-tray-app` skill's; fix it there and recopy, never here. Before the first release, run the skill's audit.
