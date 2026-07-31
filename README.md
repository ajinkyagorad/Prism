# PRISM

*Quest 3 · mixed reality · fully procedural*

A mixed-reality piece built without a single imported asset — every mesh, material and motion is generated in code at runtime.

Built and shipped through the [ClaudeAI-UnityMCP-APK pipeline](https://github.com/ajinkyagorad/ClaudeAI-UnityMCP-APK-pipeline) — a headless Unity build server driven by Claude Code, with APKs served to the headset over the browser.

## What is here

| | |
|---|---|
| Unity | 6000.5.5f1 |
| Product | PRISM |
| Package | `com.triton.prism` |
| Scenes | Prism |
| Source in this repo | 3.5 MB across 559 files |
| Payload left out | 2.2 MB — see [ASSETS.md](ASSETS.md) |

## AI

Written with Claude Code, which suits a procedural project: the whole artefact is source, so there is nothing to hand-place.

## Notes

- No textures, no models, nothing downloaded — by design. If you find yourself adding an imported asset, you are in the wrong repo.

## Build

```bash
# 1. open once in the editor so Unity restores Packages/ and regenerates Library/
Unity -projectPath .

# 2. or build headless (Android / Quest)
Unity -batchmode -projectPath . \
      -executeMethod ServerBuild.BuildAndroid -logFile build.log
```

Requires **Unity 6000.5.5f1** with the Android module. First open takes a few minutes
while Unity rebuilds `Library/` from what is in this repo — that directory is
regenerated state and is deliberately not committed.

**Do not build with `-nographics`.** It drops the Vulkan `xr-*` entries from
`boot.config`, and the APK then shows a black screen instead of passthrough while
reporting a successful build.

## Related

- [ToolBench XR](https://github.com/ajinkyagorad/ToolBenchXR) — Quest 3 · mixed reality
- [Holo Idea Tests](https://github.com/ajinkyagorad/holo-idea-tests) — Quest 3 · mixed reality · sketches
- [Planetary Vitals](https://github.com/ajinkyagorad/PlanetaryVitals) — Quest 3 · mixed reality · real data
- [MRI Holo Viewer](https://github.com/ajinkyagorad/MRI-Holo-Viewer) — Quest 3 · passthrough · medical imaging
- [AI-XR](https://github.com/ajinkyagorad/AI-XR) — Quest 3 · passthrough · on-device inference
- [PlanetGolf](https://github.com/ajinkyagorad/PlanetGolf) — Quest 3 · mixed reality · orbital mechanics
- [CosmoSynth](https://github.com/ajinkyagorad/CosmoSynth) — Quest 3 · simulation · validated against NASA

---

Source only: `Library/`, `Temp/`, `obj/` and build output are regenerated and are not committed.
