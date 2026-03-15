## Implementation Details

### Resources Setup

**Material & Mesh**: The `GsplatSettings` singleton owns global rendering resources. It:

- Maintains a `GsplatMaterial` array indexed by `CompressionMode` (e.g. `Uncompressed`, `Spark`), where each `GsplatMaterial` contains `Materials` for SH bands \(0-3\) and a `CalcDepthShader`.
- Procedurally generates a `Mesh` that consists of multiple quads. The number of quads is defined by `SplatInstanceSize`. Each vertex of these quads has its z-coordinate encoded with an intra-instance index, which is used in the vertex shader to fetch the splat order.

**Gsplat Data**: This package supports importing PLY file in two modes via `GsplatAsset` implementations:

- **Uncompressed**: `GsplatAssetUncompressed` stores per\-splat arrays (`Positions`, `Colors`, `Scales`, `Rotations`, optional `SHs`) and uploads them to dedicated GPU `GraphicsBuffer`s.
- **Spark (Packed)**: `GsplatAssetSpark` packs each splat into a fixed 16\-byte layout (`uint4` per splat in `PackedSplats`) plus an optional `SHs` array. Packing includes float16 position, log\-encoded scale, RGBA8, and octahedral axis\+angle quaternion encoding, which is inspired by [spark.js](https://github.com/sparkjsdev/spark/blob/main/src/shaders/splatDefines.glsl#L237C6-L237C25).

**GPU Resources & Lifetime**:

- `GsplatRendererImpl` creates a per\-renderer `OrderBuffer` (which will later store the sorted indices of the splats) and an `ISorterResource` (sorting support buffers and key buffer).
- Per\-asset GPU data buffers are allocated and cached by `GsplatResourceManager` (reference counted), so multiple renderers can share the same uploaded asset.
- Upload can be synchronous (`UploadData`) or asynchronous batched (`UploadDataAsync`), controlled by `GsplatRenderer.AsyncUpload`. The renderer can optionally draw before upload completes (`RenderBeforeUploadComplete`).

### Rendering Pipeline

The following two passes are performed each frame for every active camera.

#### Sorting Pass

This pass sorts the splats by their depth to the camera. The sorting is performed entirely on the GPU using `Gsplat.compute`. This compute shader leverages a highly optimized radix sort implementation from `DeviceRadixSort.hlsl`.

*   **Integration**: The sorting is initiated by custom render pipeline hooks: `GsplatURPFeature` for URP, `GsplatHDRPPass` for HDRP, or `GsplatSorter.OnPreCullCamera` for BiRP. These hooks call `GsplatSorter.DispatchSort`.
*   **Sorting Steps**:
    1.  **InitPayload** (Optional): If the payload buffer (`b_sortPayload`) has not been initialized, fill it with sequential indices (0, 1, 2, ... `SplatCount`-1).
    2.  **CalcDepth**: `IGsplat.ComputeDepth` runs an asset\-specific compute kernel (`CalcDepth` or `CalcDepthSpark`) to calculates view-space depth of each splat, and stores them into `SorterResource.InputKeys` which will be used as the sorting key.
    3.  **DeviceRadixSort**: The `Upsweep`, `Scan`, and `Downsweep` kernels execute a device-wide radix sort. It sorts the depth values in the `b_sort` buffer. Crucially, it applies the same reordering operations to the `b_sortPayload` buffer.
*   **Result**: After the sort, the `b_sortPayload` buffer (which is the `OrderBuffer` from `GsplatRendererImpl`) contains the original splat indices, now sorted from back-to-front based on their depth to the camera.

#### Render Pass

With the splats sorted, they can now be drawn using `Gsplat.shader`.

*   **Draw Call**: The `GsplatRendererImpl.Render` method issues a single draw call via `Graphics.RenderMeshPrimitives`. It uses GPU instancing to render multiple instances of the procedurally generated quad mesh, and a material from `GsplatAsset.Material` is selected based on the desired `SHBands`. All necessary buffers and parameters (`_MATRIX_M`, `_SplatCount`, etc.) are passed to the shader via a `MaterialPropertyBlock`.
*   **Vertex Shader**:
    1.  **Index Calculation**: It determines the final splat `order` to render by combining the `instanceID` with the intra-instance index stored in the vertex's z-component.
    2.  **Fetch Sorted ID**: It uses this `order` to look up the actual splat `id` from the `_OrderBuffer`. This `id` corresponds to the correct, depth-sorted splat.
    3.  **Fetch Splat Data**: Using this sorted `id`, it fetches (extracts) the splat's position, rotation, scale, color, and SH data from their respective buffers.
    4.  **Covariance & Projection**: It calculates the 2D covariance matrix of the Gaussian in screen space. This determines the shape and size of the splat on the screen. It performs frustum and small-splat culling for efficiency.
    5.  **SH Calculation** (Optional): If SHs are used, `EvalSH` is called to calculate the view-dependent color component, which is then added to the base color.
    6.  **Vertex Output**: It calculates the final clip-space position of the quad's vertex by offsetting it from the splat's projected center based on the 2D covariance. The final color and UV coordinates (representing the position within the Gaussian ellipse) are passed to the fragment shader.
*   **Fragment Shader**:
    1.  It calculates the squared distance from the pixel to the center of the Gaussian ellipse using the interpolated UVs.
    2.  If the pixel is outside the ellipse (`A > 1.0`), it is discarded.
    3.  The final alpha is calculated using an exponential falloff based on the distance, modulated by the splat's opacity. Pixels with very low alpha are discarded.
    4.  The final color is the vertex color multiplied by the calculated alpha. An optional `Gamma To Linear` conversion can be applied before output.

