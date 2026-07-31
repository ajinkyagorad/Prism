<h1 align="center">PRISM</h1>
<p align="center"><em>Nothing imported. Every mesh born in code.</em></p>
<p align="center"><img alt="Unity" src="https://img.shields.io/badge/Unity-6000.5.5f1-2E8B57?style=flat-square"> <img alt="platform" src="https://img.shields.io/badge/platform-Quest%203-444?style=flat-square"> <img alt="built with" src="https://img.shields.io/badge/built%20with-Claude%20Code-D97757?style=flat-square"> <img alt="source only" src="https://img.shields.io/badge/source%20only-no%20payload-555?style=flat-square"></p>

<p align="center">
  <img src="docs/01-atrium-horizon.png" width="860">
</p>

A mixed-reality piece built without a single imported asset — every mesh, material and motion is generated at runtime.

Built and shipped through the [ClaudeAI-UnityMCP-APK pipeline](https://github.com/ajinkyagorad/ClaudeAI-UnityMCP-APK-pipeline) — a headless Unity build server driven by Claude Code, serving APKs straight to the headset.

## Scenes

|   |   |
|:--:|:--:|
| <img src="docs/02-constellation.png" width="100%"><br><sub>Constellation</sub> | <img src="docs/03-constellation-wide.png" width="100%"><br><sub>Constellation wide</sub> |
| <img src="docs/04-orbital.png" width="100%"><br><sub>Orbital</sub> | <img src="docs/05-living-cell.png" width="100%"><br><sub>Living cell</sub> |
| <img src="docs/06-quantum-garden.png" width="100%"><br><sub>Quantum garden</sub> | <img src="docs/07-machine-cathedral.png" width="100%"><br><sub>Machine cathedral</sub> |
| <img src="docs/08-planet-guardian.png" width="100%"><br><sub>Planet guardian</sub> | <img src="docs/09-evolution-engine.png" width="100%"><br><sub>Evolution engine</sub> |
| <img src="docs/10-algorithm-city.png" width="100%"><br><sub>Algorithm city</sub> |   |

## What is here

| | |
|---|---|
| Unity | `6000.5.5f1` |
| Product | PRISM |
| Package | `com.triton.prism` |
| Scenes | Prism |

## AI

Written with Claude Code, which suits a procedural project: the whole artefact is source, so there is nothing to hand-place.

## Notes

- No textures, no models, nothing downloaded — by design. If you are adding an imported asset, you are in the wrong repository.

## Build

```bash
# open once so Unity restores Packages/ and regenerates Library/
Unity -projectPath .

# or headless, for Quest
Unity -batchmode -projectPath . \
      -executeMethod ServerBuild.BuildAndroid -logFile build.log
```

Needs **Unity 6000.5.5f1** with the Android module. The first open takes a few minutes while `Library/` is rebuilt from what is committed here.

**Do not build with `-nographics`** — the APK comes out black instead of passthrough and the build still reports success.

## Assets

This repository is **source only**. Model weights, volumes, imagery and package samples are left out and listed in [ASSETS.md](ASSETS.md), with the command that fetches each one back.

## Related

- [AI-XR](https://github.com/ajinkyagorad/AI-XR) — passthrough · on-device inference
- [CosmoSynth](https://github.com/ajinkyagorad/CosmoSynth) — simulation · checked against NASA
- [MRI Holo Viewer](https://github.com/ajinkyagorad/MRI-Holo-Viewer) — passthrough · medical imaging
- [PlanetGolf](https://github.com/ajinkyagorad/PlanetGolf) — mixed reality · orbital mechanics
- [Planetary Vitals](https://github.com/ajinkyagorad/PlanetaryVitals) — mixed reality · real measurements
- [Holo Idea Tests](https://github.com/ajinkyagorad/holo-idea-tests) — mixed reality · 26 sketches
- [ToolBench XR](https://github.com/ajinkyagorad/toolbench-xr) — mixed reality · tool bench

---

<sub>Captures are real renders from the headset build, composited onto passthrough black so they read the same in GitHub's light and dark themes. `Library/`, `Temp/`, `obj/` and build output are regenerated and not committed.</sub>
