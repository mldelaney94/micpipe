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

Built in Cursor, prompt by prompt. Rough log of what was asked:

1. **Initial prompt** — Plan and implement a Windows-native app that pipes clip audio to microphone-out (for proximity chat / games), with mic pass-through, volume control, a toolbar with logo, separate tool windows (not one dashboard), URL + file clip import with scrubbing, and F-row hotkeys.  
   Follow-ups in planning: use a Virtual Cable **dependency**; windows launch as separate menus; use **.NET 10** (not 8); ship FFmpeg/yt-dlp **opaque/internal**; use **WinUI 3**. Had to steer stack to .NET 10 + WinUI 3, but otherwise it did the rest. Then: implement the plan; compile to an exe; upload to GitHub; add a README.

2. **It didn’t load** — “Try running it yourself, does it launch?” Published exe crashed (`Cannot locate resource from 'ms-appx:///Views/MainWindow.xaml'`). Fix: ensure `MicPipe.pri` is copied into the publish output. One prompt away from working.

3. **Give it a logo** — Generate a logo and put it on the toolbar; relaunch.

4. **Document the build story** — Add a step-by-step “how this was created” section to the README (this section). Note in the initial-prompt blurb that .NET 10 / WinUI 3 had to be specified.

5. **Why Virtual Cable?** — Explanation: Windows won’t let a normal app become the microphone without a driver.

6. **UI overlaps the X** — Devices/Clips overlapped the window close buttons. Fix custom title bar / caption spacing; move actions off the caption strip. Also: no Quit button (use the corner X / tray).

7. **Devices / main sizing** — Devices (then main) should show the full button row on startup, be scrollable, and not require resizing.

8. **Import / YouTube** — Where is URL import? (Import clip.) Make Import bigger; treat bare numbers as **seconds** (e.g. `0`–`6`), not only `hh:mm:ss`; fix fetch (point the downloader at private FFmpeg).

9. **Button hover flash** — Hover was flashing through theme colours. Flat style: one colour, optional short fade.

10. **Scrubber / PTT / clips UX** — Bigger scrubber; Play selection should be **audible** locally; auto hold a push-to-talk key while clips play; clips right-click rename / edit back in scrubber. Then: a **PTT** button next to Clips that sets/shows the binding. Enter saves in rename. PTT can bind **mouse buttons** (e.g. Mouse4).

11. **Playback scrub animation** — While playing, scroll/scrub a colour along the waveform (and progress on main / in the Clips list) so you know it’s working.

12. **Per-clip global hotkeys** — In Clips, each row gets a Bind control: “select any key”, then that key globally plays the clip to mic-out **and** holds PTT. Bind dialog must close on success (including F1) and the button must show the bound key instead of “Bind”.

13. **This prompt** — Write all of the above prompts into the README.
