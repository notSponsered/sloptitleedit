# Sloptitle Edit

Subtitle Edit but vibecoded to fit my needs. It's a fork of [Subtitle Edit](https://github.com/SubtitleEdit/subtitleedit) 5.0.0-rc3 that fixes/adds things like:

- **A dynamic UI for customization and layouts:** split, tab, drag and float any panel (subtitles, text, video, waveform, styles, script info, notes, auto-replace, project, GitHub). Arrangements are saved as workspaces (Edit, Timing, Typeset, Project, or your own), and the old 12 layouts are still there as presets.
- **Project features to standardize styling across episodes:** a project keeps a series together (each episode's video and subtitle file, shared styles, naming tokens). Episode titles can be looked up on TVmaze or AniList, and files can be renamed with Sonarr-style naming templates.
- **Connecting GitHub repos for subtitles:** a GitHub panel to commit changes, create pull requests, and check out or merge them (uses the GitHub CLI). Projects can be imported from or connected to a repo.
- **A better color picker:** spectrum, RGBA, hex and ASS codes, recent colors, and an eyedropper that picks from anywhere on screen, the video included (Windows only).
- **A better way of adding shapes:** draw ASSA shapes right on the video with a pen tool (curves, grid, zoom), and edit existing drawings in place.
- **More controls in the waveform:** snap, a razor tool to cut lines, overlapping lines stacked in lanes, line colors, and a quick settings menu.
- **Motion tracking (WIP):** track a point in the video and make selected ASSA lines follow it (position, plus optional scale and rotation).
- **Other UI changes that work for me:** an ASSA tools bar, drag-to-reorder in the subtitle list, padding shown in the text box, fade in/out to the video position, a Blender theme, and buttons moved or added for certain things.
- Some other random QoL stuff I'm forgetting about.

This build is meant for my personal needs. I doubt this fork will get any attention, however, if it does, please do not support me. I do not code. Everything is vibe coded or taken from the main branch.

### Go support the main dev
- [Main Branch](https://github.com/SubtitleEdit/subtitleedit/releases)
- [GitHub Sponsors](https://github.com/sponsors/niksedk)
- [Donate via PayPal](https://www.paypal.com/donate/?hosted_button_id=4XEHVLANCQBCU)

---

## 🌐 Documentation & FAQ
Sloptitle Edit works like Subtitle Edit, so its documentation applies:
http://subtitleedit.github.io/subtitleedit/

---

## 🛠️ Building
There are no ready-made builds of Sloptitle Edit. To build it yourself, install the [.NET 10 SDK](https://dotnet.microsoft.com/download), then run this in the repository folder:

```bash
dotnet run --project src/ui/UI.csproj -c Release
```

To make a Windows installer, run `dotnet publish src/ui/UI.csproj -c Release`, then compile `installer/WindowsInno/Subtitle_Edit_Installer.iss` with Inno Setup 6.7.1 or newer.

> ⚙️ Note: installed in Program Files, Sloptitle Edit uses the same settings folder as Subtitle Edit 5 (`%AppData%\Subtitle Edit`), so it picks up your existing settings. Run from any other folder, it keeps its settings next to the program.

---

## 💻 System Requirements

### Windows
- Minimum: Windows 10 version 22H2 (build 19045) or newer, fully updated. Older Windows 10 builds (2004/20H2/21H1/21H2) are end-of-life and may fail to start with a .NET runtime error (`0x80131506`).

### macOS and Linux
Built mainly for Windows. It should build and run on macOS 12+ and Linux like Subtitle Edit does, but that isn't tested, and the screen eyedropper is Windows only. On Linux, video needs mpv and ffmpeg:

#### Debian/Ubuntu
```bash
sudo apt update && sudo apt install -y mpv libmpv-dev ffmpeg
```

#### Arch
```bash
sudo pacman -S mpv ffmpeg
```

#### Fedora
```bash
sudo dnf install mpv-libs ffmpeg
```

#### openSUSE
```bash
sudo zypper install libmpv1 ffmpeg
```

### Optional tools
- **ffmpeg:** motion tracking and "Import from video" in the project editor use it (set it up via Video → Open video).
- **[GitHub CLI](https://cli.github.com/) and git:** needed for the GitHub panel.

---

## 🔒 Privacy

**Sloptitle Edit** is an offline, open-source application.
It does **not** collect, store, transmit, or analyze the content of your subtitle files, media files, or any associated metadata — not for analytics, not for model training, and not for any other secondary purpose, now or in the future.

All core features, including editing, converting, video playback, and **local auto-backup**, run entirely on your device.

If you choose to use optional online services (such as translation, speech-to-text, text-to-speech, OCR, dictionary/lookups, episode title lookups on TVmaze or AniList, or GitHub through the GitHub CLI), only the minimal data required to perform that specific request is sent directly to the selected provider. Any such data transfer is governed by the provider’s own privacy policy, and Sloptitle Edit does not retain or forward this data in any way.

Sloptitle Edit aims to give you full control over your files — your data stays yours.

---

## 📄 License
MIT, like Subtitle Edit. See [LICENSE](LICENSE).
