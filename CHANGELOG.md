# Changelog

## 1.0.2

- **Voice management** — rename, change visibility (private/unlisted), and delete voices directly from the Clone Voice page's "Your Voices" list (`PATCH`/`DELETE /model/{id}`)
- **In-app auto-update** — a banner (and an About page checker) detects newer GitHub releases, downloads the MSI with a progress bar, and launches the installer for you — no more manual trip to GitHub
- Fix: a manually-typed Job Title on Generate Speech could get silently discarded in favor of an auto-suggested one if you edited the title after pasting body text (stale-flag bug)
- Fix: `UpdateVoiceAsync` crashed on Fish's empty-body success response instead of treating it as a successful rename/visibility change
- README: added a screenshot and a "Download Latest Release" badge

## 1.0.1

- Fix: Generate Speech page's progress/chunk list/playback controls could be clipped off-screen on short or small windows — the center content area now scrolls
- The self-contained portable `.exe` release asset is discontinued — the MSI installer is the only supported way to install going forward

## 1.0.0 — Initial release

New fork of [Fatima TTS](https://github.com/hassanxs/fatima-tts), rebuilt against [Fish.audio](https://fish.audio) instead of Inworld. Independent product — own data folder, own credential storage, own installer identity.

- **Generate Speech** — streaming synthesis against Fish's `s2.1-pro`, `s2-pro`, `s1`, `s2.1-pro-free`, and `drama-3-preview` models, with per-chunk retry and resume
- **Batch Generate** — CSV/TXT batch jobs, sequential file naming, optional FFmpeg merge
- **My Jobs** — job history, waveform player, resume interrupted jobs
- **Dashboard** — usage stats, 14-day byte-billed chart
- **My Voices** — lists your own Fish.audio voice models (`GET /model?self=true`) as a picker for Reference ID
- **Voice Library search** — searches Fish's public catalog (`GET /model?self=false&title=...`)
- **Voice Cloning** — clone a persistent voice model from audio samples (`POST /model`, multipart)
- Billing switched from character count to UTF-8 byte count, matching Fish's actual pricing unit
- No Voice Design, no SRT/caption export (Fish's TTS API returns no timestamp data) — may follow once verified
