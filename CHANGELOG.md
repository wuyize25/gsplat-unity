# Changelog

All notable changes to this project will be documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [Unreleased]

### Added

- PLY importer and shaders extended to pack Gaussian Splat data in 4 uint ([#12]https://github.com/wuyize25/gsplat-unity/pull/12).
Implementation heavily inspired by the SparkJS packing implementation.

- Supports streaming data from RAM to VRAM ([#6](https://github.com/wuyize25/gsplat-unity/issues/6)). An option `Async Upload` is added to `GsplatRenderer` to enable this feature.

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