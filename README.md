# DwarfCorp (FNA fork)

![](DwarfCorpContent/Logos/gamelogo.png)

[DwarfCorp](http://www.dwarfcorp.com/) is a single-player colony/strategy game
originally developed by Completely Fair Games. This fork replaces the dead
XNA 4.0 toolchain with [FNA](https://github.com/FNA-XNA/FNA) at runtime and
the [MonoGame Content Builder](https://docs.monogame.net/articles/getting_started/tools/mgcb.html)
for building assets, so the game is playable on modern Windows without
installing XNA Game Studio or the MXA hacks.

## What's in this repo

| Path | Purpose |
|---|---|
| [DwarfCorp/](DwarfCorp/) | Game source (`DwarfCorpFNA.csproj`) |
| [DwarfCorpContent/](DwarfCorpContent/) | Source assets (PNG, FX, WAV, JSON) + `Content.mgcb` |
| [ContentMGCB/](ContentMGCB/) | Content-pipeline helpers: `FxcEffectProcessor`, converter scripts, compiled `.xnb` output |
| [FNA/](FNA/) | FNA submodule (pinned at `f92a34c`, pre-FNA3D, pre-SDL3) |
| [FNA_libs/win32/](FNA_libs/win32/) | Native DLLs (SDL2, MojoShader, FAudio) copied into the build output |
| [LibNoise/](LibNoise/), [YarnSpinner/](YarnSpinner/), [PSD/](PSD/) | Direct dependencies of `DwarfCorpFNA` |

## Prerequisites

- **Windows 10/11** (FNA native libs in this fork are Windows-only).
- **.NET Framework 4.8 Developer Pack** — [download](https://dotnet.microsoft.com/download/dotnet-framework/net48).
- **.NET 8 SDK** (or newer) — needed for `dotnet mgcb`.
- **Windows 10 SDK** with `fxc.exe` — needed once to compile shaders; the copy
  of `fxc.exe` is already bundled at [ContentMGCB/lib/](ContentMGCB/lib/).
- **JetBrains Rider** or Visual Studio 2022 with MSBuild tools. Rider is what
  this fork was validated against.
- **Python 3** — used by one post-build script that patches 2 SpriteFont `.xnb`
  for .NET Framework compatibility.

## Building from scratch

```bash
git clone <this repo>
cd dwarfcorp
git submodule update --init --recursive

# Build FNA runtime (one time)
cd FNA
dotnet build FNA.csproj -c Release
cd ..

# Build content
dotnet tool restore
cd DwarfCorpContent
dotnet mgcb /@:Content.mgcb
cd ..

# Build and run the game (Rider ▶ on DwarfCorpFNA, or:)
dotnet build DwarfCorp/DwarfCorpFNA.csproj -c Debug
```

The `DwarfCorpFNA.csproj` post-build copies `FNA_libs/win32/*`, the compiled
`ContentMGCB/bin/Content/*`, and the loose runtime data files
(JSON/PSD/XACT banks) into `DwarfCorp/bin/FNA/Debug/`, then runs
[ContentMGCB/patch_xnb_types.py](ContentMGCB/patch_xnb_types.py) to fix
SpriteFont assembly names.

## What changed from upstream

- `DwarfCorpXNA`, `DCMono`, `FontBuilder`, `ManaLampMod`, `MaybeNullSpeedTest`,
  `Todo`, `TodoViewer` are deleted. The only maintained build is `DwarfCorpFNA`.
- `.NET Framework 4.8` everywhere (upstream was 4.5, whose developer pack is no
  longer distributed).
- Content pipeline is MGCB + [FxcEffectProcessor](ContentMGCB/src/FxcEffectProcessor.cs)
  (emits `fx_2_0` bytecode for FNA's MojoShader).
- `sphereLowPoly.fbx` was regenerated as binary FBX; the original ASCII-format
  FBX 2010 could not be read by Blender or Assimp.
- `#if XNA_BUILD` / `#if GEMMONO` / `#if GEMXNA` branches stripped everywhere.

## Licensing

Released under a modified MIT agreement. Source code is free to use, modify,
and redistribute. **Game content** — textures, 3D models, sound effects, music
— remains proprietary to Completely Fair Games Ltd and MUST NOT be
redistributed. If you want to play the game, [buy it on Steam](http://store.steampowered.com/app/252390/DwarfCorp/)
or [itch.io](https://completelyfairgames.itch.io/dwarfcorp); the content
folder in your install pairs with this source.

Raw text files, JSON, and XML configuration are considered source code.

See [LICENSE.txt](LICENSE.txt) for the full text.
