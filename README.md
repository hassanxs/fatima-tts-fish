# Fatima TTS (Fish)

> Windows desktop client for the [Fish.audio](https://fish.audio) TTS API — generate unlimited-length audio, clone voices, and batch process files with full resume support.

## Why?

Fish.audio's own tools are built around single generations. Fatima TTS (Fish) adds the workflow layer on top:

| Limitation | Fatima TTS (Fish) |
|---|---|
| No client-side chunking for long text | Unlimited text length — automatically splits and merges |
| No batch processing | Generate multiple jobs at once from CSV or TXT files |
| No resume if something goes wrong | Resumes from the exact chunk that failed — no re-billing |
| Manual download per job | Auto-saves audio to your output folder |

## Features

- **Unlimited text length** — automatically chunks long text and merges the audio seamlessly
- **Batch Generate** — process multiple jobs at once from a CSV or TXT file, sequential output naming (`01-Hook.mp3`, `02-Parte 1.mp3`)
- **Resume on failure** — if a job fails mid-way, only the failed chunks are retried — already completed chunks are not re-billed
- **My Jobs** — full history with waveform player, seek, resume interrupted jobs
- **Batch Detail** — dedicated page per batch showing all jobs with play/save/status
- **My Voices + Voice Library** — browse your own saved voices, or search Fish's public Voice Library, right from the Reference ID picker
- **Voice Cloning** — upload audio samples and clone a persistent voice model
- **FFmpeg Integration** — auto-downloaded and managed, lossless batch merge into one file
- **Dashboard** — stats overview, 14-day usage chart, recent jobs
- **Dark / Light theme** — persisted preference
- **Windows notifications** — get notified when long jobs complete in the background

**Not yet included:** Voice Design, SRT/caption export (Fish's TTS API doesn't return timestamps).

## Requirements

- Windows 10 or 11 (x64)
- [Fish.audio API key](https://fish.audio)

## Installation

Download `FatimaTTSFish-v1.0.0-installer.msi` from the [latest release](https://github.com/hassanxs/fatima-tts-fish/releases/latest) and run it.

The installer will:
- Install to `C:\Program Files\Fatima TTS (Fish)\`
- Create a Start Menu shortcut
- Create a Desktop shortcut
- Register in Add/Remove Programs for clean uninstall

FFmpeg is downloaded automatically on first use (only needed for batch merge).

## Getting Started

1. Install and launch Fatima TTS (Fish)
2. Go to **Settings** and paste your [Fish.audio API key](https://fish.audio)
3. Click **Validate** to confirm the key works
4. Go to **Generate Speech** and start generating — pick a voice from **My Voices** or search the **Voice Library**, or paste a Reference ID directly

Your API key is stored securely using Windows DPAPI — never written to disk in plaintext.

## Building from Source

```bash
git clone https://github.com/hassanxs/fatima-tts-fish.git
cd fatima-tts-fish
dotnet restore FatimaTTSFish/FatimaTTSFish.csproj
dotnet run --project FatimaTTSFish/FatimaTTSFish.csproj
```

### Publish self-contained exe

```powershell
dotnet publish FatimaTTSFish/FatimaTTSFish.csproj `
  -c Release -r win-x64 --self-contained true `
  -p:PublishSingleFile=true `
  -o publish/
```

### Build the MSI installer

```powershell
installer/build-installer.ps1 -Version 1.0.0
```

Requires the WiX v5 CLI tool (`dotnet tool install --global wix --version 5.*` and `wix extension add -g WixToolset.UI.wixext/5.0.2`).

## Logs

Application logs are saved to:
```
%AppData%\FatimaTTSFish\logs\fatima_YYYY-MM-DD.log
```

Logs rotate daily and are pruned after 30 days. Open the logs folder from **Settings → Logs → Open Folder**.

## Tech Stack

- **WPF / .NET 8** — Windows Presentation Foundation, C#
- **NAudio** — audio playback and waveform extraction
- **Fish.audio TTS API** — synthesis (streaming), voice listing, voice cloning
- **FFmpeg** — batch audio merging (auto-managed)
- **Windows DPAPI** — secure API key storage

## License

MIT — see [LICENSE](LICENSE)

## Contributing

Pull requests welcome. Please open an issue first to discuss significant changes.
