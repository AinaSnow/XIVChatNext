# XIVChat Next branding

The user selected **C1** on 2026-09-10 for both the desktop client and the Dalamud plugin.
The blue-and-cream messenger bird on lavender represents remote messages and reliable delivery.

![Selected C1](logo-c1.png)

## Sources and exports

| File | Purpose | Size |
| --- | --- | --- |
| `logo-c1.png` | Selected master; byte-identical to candidate C1 | 1254 × 1254 |
| `../../XIVChat Desktop/Resources/logo-c1.png` | Main-window brand image | 256 × 256 |
| `../../XIVChat Desktop/Resources/logo-c1.ico` | Executable and window icons | 16, 20, 24, 32, 40, 48, 64, 96, 128, 256 |
| `../../XIVChatPlugin/Resources/icon.png` | Dalamud plugin icon | 512 × 512 |

The artwork, background and crop are retained. Application exports only encode the selected
image at the dimensions required by their consumers. The master is not resampled or repainted.
Run `./assets/branding/Export-LogoResources.ps1` in PowerShell on Windows to reproduce them.

The desktop embeds the ICO in its executable and uses it for all app windows. Its PNG is shown
beside the main menu. Resources are copied into build and publish output.

The plugin project copies its PNG to `images/icon.png` beside `XIVChatNext.dll`, the path
Dalamud uses for development-plugin icons. Release packaging also emits
`XIVChatNext/images/icon.png` alongside the release ZIP and manifest. A public release must
host that image and set the repository manifest's `IconUrl` to the published image URL.
This local branding change does not publish a release or change the public repository catalog.

The six original candidates, exact prompts, requested colors, provider information and checksums
remain in [the candidate record](candidates/2026-09-10/README.md). The previous plugin icon is
preserved in `legacy/plugin-icon.png`; the original desktop ICO and root SVGs remain available.

## Verification

- Desktop Debug build passed with the three existing Windows App SDK packaging warnings.
- Plugin Debug and Release builds passed with the seven existing warnings.
- Master SHA-256 matches C1; desktop and plugin output files match their source exports.
- All ten embedded ICO frames decode at the declared sizes.
- The running desktop main window, header image and configuration window display C1.
- The plugin release sidecar `images/icon.png` matches the development-build icon.

## Integration references

- [Windows App SDK: AppWindow.SetIcon](https://learn.microsoft.com/en-us/windows/windows-app-sdk/api/winrt/microsoft.ui.windowing.appwindow.seticon?view=windows-app-sdk-1.8)
- [DalamudPackager image handling](https://github.com/goatcorp/DalamudPackager/blob/master/DalamudPackager/DalamudPackager.cs)
