# MicPipe

Windows utility that pipes audio clips into your microphone path for proximity chat and voice apps — while still letting you speak normally.

MicPipe mixes your **real microphone** with **clip playback** and sends the result to a **virtual cable**. Games and Discord hear that mix as the mic.

## Requirements

- Windows 10/11 (x64)
- [.NET 10 SDK](https://dotnet.microsoft.com/download) (for building)
- [VB-Audio Virtual Cable](https://vb-audio.com/Cable/) (required at runtime)

FFmpeg and yt-dlp are **bundled privately** when you build/publish. End users do not install them.

## Quick start (published build)

1. Install [VB-Audio Virtual Cable](https://vb-audio.com/Cable/).
2. Run `publish.ps1`, then launch:

   `artifacts\publish\win-x64\MicPipe.exe`

3. In MicPipe → **Devices**:
   - Microphone → your real mic
   - Virtual cable output → **CABLE Input**
4. In Discord / your game:
   - Input device → **CABLE Output**

Keep the whole `win-x64` folder together when you move the app.

## Develop

```powershell
# Restore private media tools (first time / CI)
.\tools\fetch-tools.ps1

dotnet build MicPipe.sln -c Debug -p:Platform=x64
dotnet run --project src\MicPipe\MicPipe.csproj -c Debug -p:Platform=x64
```

Publish a self-contained folder:

```powershell
.\publish.ps1
```

## Features

- Mic pass-through + separate clip output volume
- Import from local audio files with a waveform scrubber
- Import from URL (e.g. YouTube) with a time range, then trim
- Clip library
- F1–F12 hotkeys (separate window; hidden until you open it)
- System tray (closing the main window hides; **Quit** exits)

## Windows

MicPipe uses separate windows on purpose — not one dashboard:

| Window | Purpose |
|--------|---------|
| Main | Volumes, Live status, open tools |
| Devices | Mic / cable / optional monitor setup |
| Import | File or URL source |
| Trim | Scrubber range select → save |
| Clips | Library play / rename / delete |
| Hotkeys | Bind F-keys to clips |

## Notes

- Clips and settings live under `%LocalAppData%\MicPipe\`
- Diagnostics log: `%LocalAppData%\MicPipe\micpipe.log`
- UI never mentions FFmpeg or yt-dlp; import failures are product errors only

## License

Add a license if/when you publish the repo formally.

## How this was made

Built in Cursor, prompt by prompt:

1. **Initial prompt** — Plan and implement a Windows-native app that pipes clip audio to microphone-out (for proximity chat / games), with mic pass-through, volume control, separate tool windows (not one dashboard), URL + file clip import with scrubbing, and F-row hotkeys. Stack settled on .NET 10 + WinUI 3, VB-Audio Virtual Cable as the external dependency, FFmpeg/yt-dlp private and opaque. Repo pushed to GitHub; README added.

2. **It didn’t load** — Published `MicPipe.exe` crashed on launch (`Cannot locate resource from 'ms-appx:///Views/MainWindow.xaml'`). One follow-up prompt: diagnose, fix publish so `MicPipe.pri` ships next to the exe, relaunch. It stayed up.

3. **Give it a logo** — Generate a charcoal/amber MicPipe mark, put it on the main toolbar (and window/tray icons), republish, relaunch.
