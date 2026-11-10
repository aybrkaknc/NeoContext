<p align="center">
  <img src="src/NeoContext.App/Assets/Square150x150Logo.png" width="96" alt="NeoContext logo" />
</p>

<h1 align="center">NeoContext</h1>

<p align="center">
  A modern, safe manager for the Windows right-click menu.<br/>
  Built with C#, .NET 10 and WinUI 3.
</p>

---

NeoContext shows everything that Explorer adds to your right-click menus and lets you switch each
entry on or off, rename it, or remove it. It is a ground-up rewrite of the ideas behind
[ContextMenuManager](https://github.com/BluePointLilac/ContextMenuManager) by BluePointLilac,
redesigned around safety, clear structure and an English-first, fully localizable interface.

## Features

| Page | What you can manage | How NeoContext changes it |
| --- | --- | --- |
| Files, Folders, Folder background, Desktop, Drives, Files and folders | Static commands (`shell` verbs) and COM shell extensions (`shellex` handlers) | Verbs: the documented `LegacyDisable` value. Handlers: moved to `-ContextMenuHandlers` |
| New menu | File types in Explorer's **New** submenu | `ShellNew` key renamed to `-ShellNew` |
| Win+X menu | Shortcuts in the Win+X / Start-button menu | Hidden attribute; the Win+X hash is rewritten after a rename |
| Store apps | Context menu verbs declared by installed MSIX / Microsoft Store apps | Per-user `Shell Extensions\Blocked` list |

Every page has search, and each entry can be opened in Registry Editor or Explorer.

### Safety first

- **No silent permission changes.** Reading never touches permissions. If Windows protects a key
  (for example one owned by TrustedInstaller), NeoContext asks before taking ownership, saves the
  previous owner and permissions, and can restore them from *Settings → Permissions*.
- **Backups before deletes.** A registry entry is exported to a `.reg` file before it is deleted;
  if the export fails, nothing is deleted. Win+X shortcuts go to the Recycle Bin.
- **Essential entries are protected.** Entries Explorer needs (such as *Open*) cannot be disabled
  or deleted unless you turn protection off.
- **Per-user first.** Per-user and machine-wide entries are read separately and labelled, so most
  changes work without administrator rights. When elevation is needed, NeoContext offers to restart
  as administrator instead of always running elevated.
- **Verified updates.** NeoContext only notifies you about new releases. If you choose to download
  one, the file must match the SHA-256 digest published on GitHub, and installer packages must
  carry a valid Authenticode signature; otherwise the download is discarded.

## Requirements

- Windows 10 version 1809 or later, or Windows 11 (x64 or ARM64)
- For building: the [.NET 10 SDK](https://dotnet.microsoft.com/download). Visual Studio is not required.

## Install

Download the latest build from [Releases](https://github.com/aybrkaknc/NeoContext/releases):

- **`NeoContext-<version>-win-x64.zip`** – portable. Extract and run `NeoContext.exe`. Create an empty
  `data` folder next to the executable to keep settings and backups there instead of `%LOCALAPPDATA%`.
- **`NeoContext-<version>-win-x64.msix`** – installable package (when a signed package is published).

A winget manifest template lives in [`packaging/winget`](packaging/winget).

## Build from source

```powershell
git clone https://github.com/aybrkaknc/NeoContext.git
cd NeoContext
dotnet test                                   # unit and integration tests
dotnet build src/NeoContext.App -p:Platform=x64 -p:WindowsPackageType=None
src/NeoContext.App/bin/x64/Debug/net10.0-windows10.0.19041.0/win-x64/NeoContext.exe
```

Release packages are produced by [`build/Publish.ps1`](build/Publish.ps1), which the release workflow runs.

> Build into the default output folder. Passing `-o <dir>` to `dotnet build` changes the paths of the
> compiled XAML inside the resource index, and the app then fails to start with "XAML parsing failed".

## Architecture

```
src/
  NeoContext.Core       Domain model, rules and services. No Windows APIs; fully unit-testable.
  NeoContext.Registry   Registry paths and access, verb / shell extension / ShellNew providers,
                        key ownership (take and restore), .reg export, Shell Extensions\Blocked.
  NeoContext.Shell      .lnk and property-store interop, Win+X hash and provider, desktop.ini.
  NeoContext.Uwp        Reads AppxManifest.xml of installed packages for their context menu verbs.
  NeoContext.Updates    GitHub release lookup, version comparison, verified downloads.
  NeoContext.App        WinUI 3 views and view models (MVVM), composition root (Services/AppHost.cs).
tests/
  NeoContext.Tests      xUnit tests; registry tests run against a throw-away key under HKCU.
```

Each kind of menu entry is an `IEntryProvider`. `ContextMenuService` loads all providers, applies the
protection and naming rules, and routes every change to the right provider. Providers never show UI
and never throw for expected failures: they return a `Result` that the UI turns into a message with
the right next step (restart as administrator, take ownership, restart Explorer).

### What changed compared to ContextMenuManager

| Area | ContextMenuManager | NeoContext |
| --- | --- | --- |
| Protected keys | Took ownership on every read, recursively, never restored | Only after a failed write, with consent, one key, restorable |
| Updates | Downloaded and ran an .exe without checks | Notify only; SHA-256 + Authenticode verification before keeping a download |
| Errors | Empty `catch` blocks, ignored HRESULTs | Typed `Result` values, logged, shown with a suggested fix |
| Structure | WinForms controls containing registry code | Separate domain, providers and MVVM UI |
| Enabling verbs | Wiped `CommandFlags` | Removes only `LegacyDisable` / `ProgrammaticAccessOnly` |
| Win+X hash | Order-dependent path replacement, unchecked HRESULTs | Longest-prefix replacement, checked; tested against Windows' own shortcuts |
| Store apps | Prefix match on package names | Exact match by reading each package's manifest |
| Classes root | Wrote through merged `HKEY_CLASSES_ROOT` | Reads HKCU and HKLM separately and labels the scope |

## Known limitations

- Windows 11 shows registry-based entries under **Show more options**; NeoContext manages them there.
  Entries in the new Windows 11 menu come from packaged apps and are on the **Store apps** page.
- New-menu (ShellNew), Win+X and Store-app changes appear after Explorer restarts; NeoContext offers to restart it.
- New-menu items cannot be renamed, because that would rename the file type everywhere in Windows.
- Renaming a shell extension is not supported; its name comes from the COM class.

## Localization

User interface strings live in `src/NeoContext.App/Strings/<language>/Resources.resw`. English (`en-US`)
is the default and Turkish (`tr-TR`) is included. To add a language, copy the `en-US` folder, translate
the values, and add the language to `SettingsViewModel.Languages`.

## License

NeoContext is free software, licensed under the [GNU General Public License v3.0](LICENSE).

It is based on the ideas and Windows research of
[ContextMenuManager](https://github.com/BluePointLilac/ContextMenuManager) © BluePointLilac, also GPL-3.0.
The Win+X hash algorithm follows Rafael Rivera's [hashlnk](https://github.com/riverar/hashlnk).
