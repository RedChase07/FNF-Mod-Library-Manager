# FNF Mod Library (WinUI)
# I'm going to say this now, this is 100% AI vibe coded.

[What Co-Pilot Said]

A Windows desktop rewrite of the FNF Mod Library, built with C# and WinUI 3.

## Features

- Scan a selected folder and its subfolders for `.exe` mods.
- Browse mods in a virtualized library with search, sorting, favorites, and tag filters.
- Add tags, ignore executables, and manage ignored entries.
- Show a neighboring `icon.ico` or `icon.png` first, then the icon embedded in the executable.
- Keep play counts, favorites, tags, and library settings between launches.
- Choose one to six columns, or customize the cards, borders, accent, text, and button fill/text/border colors individually.
- Keep all mod cards the same size within each card-artwork setting.
- Keep the current library scroll position while playing, favoriting, ignoring, or editing tags on a mod.
- Launch a mod from its own folder after confirmation.

## Requirements

- Windows 10 version 2004 (build 19041) or later, x64.
- The .NET 9 SDK to build or run from source.

## Run from source

Open `FnFModLibrary.WinUI.csproj` in Visual Studio and run it, or use:

```powershell
dotnet run --project .\FnFModLibrary.WinUI.csproj
```

The project is configured for unpackaged WinUI deployment. No Developer Mode or MSIX installation is required.

## Create a standalone release

Run `publish.bat`, or publish manually:

```powershell
dotnet publish .\FnFModLibrary.WinUI.csproj -c Release -r win-x64 --self-contained true -p:WindowsAppSDKSelfContained=true -p:PublishTrimmed=false -o .\publish\win-x64
```

Copy the **entire** `publish\win-x64` folder to the other PC and run `FnFModLibrary.WinUI.exe`. It is self-contained and does not require the .NET runtime to be installed. The app still requires a supported Windows version.

## Data and migration

The WinUI app stores its library data separately at `%APPDATA%\FNF Mod Library WinUI\library.json`.

If the app cannot start, it displays an error and writes details to `%TEMP%\FNF Mod Library WinUI startup error.txt`.
