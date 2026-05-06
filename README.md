# Relay Player

[简体中文](./README.zh-CN.md)

Relay Player is a Windows desktop client for Emby. It handles server login, browsing, search, resume playback, media source selection, and playback progress reporting, while delegating the actual video playback to `mpv.net`.

The goal is simple: keep Emby library access and playback state inside a lightweight desktop client, and let a mature external player handle decoding, subtitles, audio tracks, and playback controls.

## Features

- Save multiple Emby servers and switch between them from the server list.
- Restore the last usable Emby session on startup.
- Browse and search movies, series, seasons, episodes, and folders.
- Open Continue Watching as the primary entry point.
- Select media source, audio track, and subtitle track before playback.
- Launch `mpv.net` with authenticated Emby direct stream URLs.
- Resume from the playback position recorded by Emby.
- Report playback start, progress, and stop events back to Emby through mpv JSON IPC.
- Continue to the next episode in the same `mpv.net` instance when autoplay is enabled.
- Store settings and logs under `%APPDATA%\RelayPlayer`.

## Requirements

- Windows 10 or Windows 11.
- .NET 10 SDK for development.
- `mpv.net` installed. If it is not available in `PATH`, select `mpvnet.exe` in the app.
- An accessible Emby server and a valid user account.

Self-contained release builds do not require the .NET runtime to be installed on the target machine.

## Quick Start

```powershell
dotnet restore .\Player.sln
dotnet build .\Player.sln
dotnet run --project .\src\Player.App\Player.App.csproj
```

The repository includes a `global.json` that targets the .NET 10 SDK. Check your local SDK installation with:

```powershell
dotnet --info
```

First run:

1. Click `Add Server`.
2. Enter the Emby server URL, username, and password.
3. Confirm the `mpv.net` executable path if it is not detected automatically.
4. Pick an item from Continue Watching or search results.
5. Choose the media source, audio track, and subtitle track.
6. Start playback in `mpv.net`.

## Build, Test, and Publish

Run tests:

```powershell
dotnet test .\Player.sln
```

Create a local self-contained Windows x64 build:

```powershell
dotnet publish .\src\Player.App\Player.App.csproj `
  -c Release `
  -r win-x64 `
  --self-contained true `
  -p:PublishSingleFile=true `
  -p:IncludeNativeLibrariesForSelfExtract=true `
  -p:EnableCompressionInSingleFile=true `
  -o .\artifacts\RelayPlayer-win-x64
```

Create a zip package:

```powershell
Compress-Archive -Path .\artifacts\RelayPlayer-win-x64\* -DestinationPath .\artifacts\RelayPlayer-win-x64.zip -Force
```

## GitHub Actions Packaging

The workflow in `.github/workflows/package.yml` runs on push, pull request, manual dispatch, and `v*` tags.

It performs:

- restore
- Release build
- test
- `win-x64` self-contained publish
- zip packaging
- artifact upload
- GitHub Release creation or update for `v*` tags

The uploaded artifact is named `RelayPlayer-win-x64.zip`.

## Project Layout

```text
src/
  Player.App/
    Assets/              app icon and image resources
    Converters/          WPF value converters
    Models/              Emby, settings, and browse-state models
    Services/            Emby API, mpv.net, IPC, playback, and persistence services
    Views/
      Dialogs/           login and password dialogs
      MainWindow/        main window XAML and partial code-behind files
tests/
  Player.App.Tests/      unit tests
.github/workflows/      CI and packaging workflow
```

## Data Locations

- Settings: `%APPDATA%\RelayPlayer\settings.json`
- Logs: `%APPDATA%\RelayPlayer\logs\relay-player.log`

Saved data includes server URLs, usernames, device IDs, Emby tokens, and the `mpv.net` path. Sensitive tokens are protected with Windows user-scoped data protection.

## Known Limits

- Windows and `mpv.net` only.
- External playback requires Emby `api_key` in the stream URL because `mpv.net` cannot reuse the WPF client's HTTP headers.
- Some Emby server settings, transcoding policies, or plugins may affect direct stream playback.

## References

- Emby API: https://dev.emby.media/doc/restapi/
- mpv manual: https://mpv.io/manual/stable/
- mpv.net: https://github.com/mpvnet-player/mpv.net
