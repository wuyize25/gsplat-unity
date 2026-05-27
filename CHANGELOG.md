# Changelog

All notable changes to this project will be documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [1.3.0] - 2026-05-23

### Added

- Added support for the Niantic [SPZ](https://github.com/nianticlabs/spz) format (`.spz`). The `GsplatImporter` now also handles `.spz` assets, decoding into the existing Spark or Uncompressed pipelines. SPZ versions 1–4 are supported: v1–3 use the gzip container, and v4 uses the NGSP/ZSTD container (decoded via the vendored `ZstdSharp` library under `Runtime/Plugins/ZstdSharp/`). A binary import cache under `Library/GsplatCache/` skips the decode/pack step on subsequent reimports. ([#26](https://github.com/wuyize25/gsplat-unity/pull/26) by [@KeirRice](https://github.com/KeirRice))

- Added rendering support for SH degree 4 (band 4), available only for SPZ v4 files that carry it. Adds `PackSH4` (27 sint4 values → 4 uint32 per splat, matching the SPZ writer's `shRestBits=4` default precision), a new `_PackedSH4Buffer` GPU binding, an `SH_BANDS_4` shader variant, and band-4 evaluation in `EvalSH`. The `GsplatRenderer` SH degree slider's maximum now follows the bound asset's `SHBands` — a degree-3 PLY shows a 0–3 slider; a degree-4 SPZ shows 0–4. PLY import continues to cap at degree 3. ([#26](https://github.com/wuyize25/gsplat-unity/pull/26) by [@KeirRice](https://github.com/KeirRice))

- Added a `SourceCoordinates` option to `GsplatImporter`. Positions, rotation quaternions, and SH coefficients are converted from the source frame (e.g. RUB for 3DGS / SPZ) to Unity (RUF) at import time. ([#26](https://github.com/wuyize25/gsplat-unity/pull/26) by [@KeirRice](https://github.com/KeirRice))

- Added an activatable refresh rate slider, running the sorting every Nth frame and the cutouts computation every Nth sort. Force a sort computation when a camera moves or rotates past a customizable threshold. ([#20](https://github.com/wuyize25/gsplat-unity/pull/20) by [@Arthur-Aillet](https://github.com/Arthur-Aillet))

- `GsplatCutout` component to edit the Gaussian Splattings dynamically. A compute shader prepass is done before rendering that creates the order buffer, ignoring splats contained in cutout shapes and removing them from further calculations. ([#19](https://github.com/wuyize25/gsplat-unity/pull/19) by [@Arthur-Aillet](https://github.com/Arthur-Aillet))

- Multiple materials generated automatically to let the user define a custom render order. Max Render order defined in `GsplatSettings`. ([#19](https://github.com/wuyize25/gsplat-unity/pull/19) by [@Arthur-Aillet](https://github.com/Arthur-Aillet))

- PLY importer and shaders extended to also pack Splats Spherical Harmonics as 2 uint for SH1, 6 uint for SH2 and 10 uint for SH3. The implementation is also heavily inspired by the SparkJS [packing implementation](https://github.com/sparkjsdev/spark/blob/main/src/SplatMesh.ts#L754). ([#14](https://github.com/wuyize25/gsplat-unity/pull/14) by [@Arthur-Aillet](https://github.com/Arthur-Aillet))

### Fixed

- The behavior of the `Reset` button in `Project Settings > Gsplat` was inconsistent with the inspector of `GsplatSettings`, and some properties were not being reset.

## [1.2.1] - 2026-03-26

### Added

- Added a downscale factor for individual splats to the GsplatRenderer. Improves rendering speed while trying to maintain visual fidelity as much as possible. ([#18](https://github.com/wuyize25/gsplat-unity/pull/18) by [@Arthur-Aillet](https://github.com/Arthur-Aillet)).

### Fixed

- Fixed a bug where using an unspecified var type when calling `TryGetGUIDAndLocalFileIdentifier` in `GsplatRenderer` caused an error in Unity 2022/2021. ([#22](https://github.com/wuyize25/gsplat-unity/pull/22) by [@Arthur-Aillet](https://github.com/Arthur-Aillet))

## [1.2.0] - 2026-03-22

### Added

- Per\-asset GPU data buffers are now allocated and cached by `GsplatResourceManager` (reference counted). When multiple instances of the same `GsplatAsset` are present in a scene, they can share the same GraphicsBuffers.

- PLY importer and shaders extended to pack Gaussian Splat data in 4 uint ([#12](https://github.com/wuyize25/gsplat-unity/pull/12) by [@Arthur-Aillet](https://github.com/Arthur-Aillet)). Implementation heavily inspired by the SparkJS packing implementation. An option `Compression` is added to `GsplatImporter` to choose between `Uncompressed` and `Spark` (packed) modes.

- Added a brightness slider in `GsplatRenderer` to allow post-hoc scaling of the Gsplat Asset's brightness. ([#17](https://github.com/wuyize25/gsplat-unity/pull/17) by [@Indivicivet](https://github.com/Indivicivet))

- Supports streaming data from RAM to VRAM ([#6](https://github.com/wuyize25/gsplat-unity/issues/6)). An option `Async Upload` is added to `GsplatRenderer` to enable this feature.

### Changed

- The PLY file will be imported using Spark compression mode by default. Assets imported in the previous versions will be automatically re-imported in the new mode, but `GsplatRenderer` references to the `GsplatAsset` may be lost and need to be manually re-assigned.

### Removed

- Switching `GsplatAsset` instances with the same point count and the same SH bands on `GsplatRenderer` no longer supports the behavior of reusing existing per\-asset GPU data buffers without recreating them.

## [1.1.2] - 2025-11-20

### Fixed

- Fixed the issue where rendering did not work properly on Mac with Unity 6 ([#9](https://github.com/wuyize25/gsplat-unity/issues/9)).

## [1.1.1] - 2025-11-19

### Fixed

- Fixed an error when importing the PLY file generated from Postshot ([#8](https://github.com/wuyize25/gsplat-unity/issues/8)). The `GsplatImporter` now supports PLY files with arbitrary property order, and the PLY file may not contain the unused normal property.

## [1.1.0] - 2025-10-13

### Added

- Supports BiRP, URP and HDRP in Unity 2021 and later versions.

### Fixed

- Fixed a NullReferenceException when opening the project using URP or HDRP.

## [1.0.3] - 2025-09-15

### Fixed

- More space was allocated to the SH buffer than is actually required.

## [1.0.2] - 2025-09-10

### Added

- Added an `SHDegree` option to `GsplatRenderer`, which sets the order of SH coefficients used for rendering.

### Fixed

- Fixed an error in SH calculation.

## [1.0.1] - 2025-09-09

### Changed

- Split out `GsplatRendererImpl` from `GsplatRenderer`.

## [1.0.0] - 2025-09-07

### Added

- This is the first release of Gsplat, as a Package.


[unreleased]: https://github.com/wuyize25/gsplat-unity/compare/v1.3.0...HEAD
[1.3.0]: https://github.com/wuyize25/gsplat-unity/compare/v1.2.1...v1.3.0
[1.2.1]: https://github.com/wuyize25/gsplat-unity/compare/v1.2.0...v1.2.1
[1.2.0]: https://github.com/wuyize25/gsplat-unity/compare/v1.1.2...v1.2.0
[1.1.2]: https://github.com/wuyize25/gsplat-unity/compare/v1.1.1...v1.1.2
[1.1.1]: https://github.com/wuyize25/gsplat-unity/compare/v1.1.0...v1.1.1
[1.1.0]: https://github.com/wuyize25/gsplat-unity/compare/v1.0.3...v1.1.0
[1.0.3]: https://github.com/wuyize25/gsplat-unity/compare/v1.0.2...v1.0.3
[1.0.2]: https://github.com/wuyize25/gsplat-unity/compare/v1.0.1...v1.0.2
[1.0.1]: https://github.com/wuyize25/gsplat-unity/compare/v1.0.0...v1.0.1
[1.0.0]: https://github.com/wuyize25/gsplat-unity/releases/tag/v1.0.0