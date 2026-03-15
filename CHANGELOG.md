# Changelog

All notable changes to this project will be documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [Unreleased]

### Added

- Per\-asset GPU data buffers now allocated and cached by `GsplatResourceManager` (reference counted). When multiple instances of the same `GsplatAsset` are present in a scene, they can share the same GraphicsBuffers.

- PLY importer and shaders extended to pack Gaussian Splat data in 4 uint ([#12](https://github.com/wuyize25/gsplat-unity/pull/12) by [@Arthur-Aillet](https://github.com/Arthur-Aillet)).
Implementation heavily inspired by the SparkJS packing implementation. An option `Compression Mode` is added to `GsplatImporter` to choose between `Uncompressed` and `Spark` (packed) modes. 

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


[unreleased]: https://github.com/wuyize25/gsplat-unity/compare/v1.1.2...HEAD
[1.1.2]: https://github.com/wuyize25/gsplat-unity/compare/v1.1.1...v1.1.2
[1.1.1]: https://github.com/wuyize25/gsplat-unity/compare/v1.1.0...v1.1.1
[1.1.0]: https://github.com/wuyize25/gsplat-unity/compare/v1.0.3...v1.1.0
[1.0.3]: https://github.com/wuyize25/gsplat-unity/compare/v1.0.2...v1.0.3
[1.0.2]: https://github.com/wuyize25/gsplat-unity/compare/v1.0.1...v1.0.2
[1.0.1]: https://github.com/wuyize25/gsplat-unity/compare/v1.0.0...v1.0.1
[1.0.0]: https://github.com/wuyize25/gsplat-unity/releases/tag/v1.0.0