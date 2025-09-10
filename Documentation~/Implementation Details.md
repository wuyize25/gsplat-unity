## Implementation Details

### Resources Setup

**Material & Mesh**: The `GsplatSettings` singleton creates a set of materials from the `Gsplat.shader`, one for each SH degree (0-3). It also procedurally generates a `Mesh` that consists of multiple quads. The number of quads is defined by `SplatInstanceSize`. Each vertex of these quads has its z-coordinate encoded with an intra-instance index, which is used in the vertex shader to fetch the splat order.

**Gsplat Data**: The `GsplatRendererImpl` class creates several `GraphicsBuffer`s on the GPU to hold the splat data: `PositionBuffer`, `ScaleBuffer`, `RotationBuffer`, `ColorBuffer`, and `SHBuffer`. It also creates an `OrderBuffer` which will later store the sorted indices of the splats. The `GsplatRenderer` class uploads the data from the `GsplatAsset` arrays to these corresponding `GraphicsBuffer`s.

### Rendering Pipeline

The following two passes are performed each frame for every active camera.

#### Sorting Pass

This pass sorts the splats by their depth to the camera. The sorting is performed entirely on the GPU using `Gsplat.compute`. This compute shader leverages a highly optimized radix sort implementation from `DeviceRadixSort.hlsl`.

*   **Integration**: The sorting is initiated by custom render pipeline hooks: `GsplatURPFeature` for URP, `GsplatHDRPPass` for HDRP, or `GsplatSorter.OnPreCullCamera` for BiRP. These hooks call `GsplatSorter.DispatchSort`.
*   **Sorting Steps**:
    1.  **`InitPayload`** (Optional): If the payload buffer (`b_sortPayload`) has not been initialized, fill it with sequential indices (0, 1, 2, ... `SplatCount`-1). 
    2.  **`CalcDistance`**: For each splat, this kernel calculates its view-space depth, and stores them in the `b_sort` buffer which will be used as the sorting key.
    3.  **`DeviceRadixSort`**: The `Upsweep`, `Scan`, and `Downsweep` kernels execute a device-wide radix sort. It sorts the depth values in the `b_sort` buffer. Crucially, it applies the same reordering operations to the `b_sortPayload` buffer.
*   **Result**: After the sort, the `b_sortPayload` buffer (which is the `OrderBuffer` from `GsplatRendererImpl`) contains the original splat indices, now sorted from back-to-front based on their depth to the camera.

#### Render Pass

With the splats sorted, they can now be drawn using `Gsplat.shader`.

*   **Draw Call**: The `GsplatRendererImpl.Render` method issues a single draw call via `Graphics.RenderMeshPrimitives`. It uses GPU instancing to render multiple instances of the procedurally generated quad mesh, and a material is selected based on the desired `SHBands`. All necessary buffers (`OrderBuffer`, `PositionBuffer`, etc.) and parameters (`_MATRIX_M`, `_SplatCount`, etc.) are passed to the shader via a `MaterialPropertyBlock`.
*   **Vertex Shader**: 
    1.  **Index Calculation**: It determines the final splat `order` to render by combining the `instanceID` with the intra-instance index stored in the vertex's z-component.
    2.  **Fetch Sorted ID**: It uses this `order` to look up the actual splat `id` from the `_OrderBuffer`. This `id` corresponds to the correct, depth-sorted splat.
    3.  **Fetch Splat Data**: Using this sorted `id`, it fetches the splat's position, rotation, scale, color, and SH data from their respective buffers.
    4.  **Covariance & Projection**: It calculates the 2D covariance matrix of the Gaussian in screen space. This determines the shape and size of the splat on the screen. It performs frustum and small-splat culling for efficiency.
    5.  **Color Calculation**: The base color is taken from the `_ColorBuffer`. If SHs are used, `EvalSH` is called to calculate the view-dependent color component, which is then added.
    6.  **Vertex Output**: It calculates the final clip-space position of the quad's vertex by offsetting it from the splat's projected center based on the 2D covariance. The final color and UV coordinates (representing the position within the Gaussian ellipse) are passed to the fragment shader.
*   **Fragment Shader**:
    1.  It calculates the squared distance from the pixel to the center of the Gaussian ellipse using the interpolated UVs.
    2.  If the pixel is outside the ellipse (`A > 1.0`), it is discarded.
    3.  The final alpha is calculated using an exponential falloff based on the distance, modulated by the splat's opacity. Pixels with very low alpha are discarded.
    4.  The final color is the vertex color multiplied by the calculated alpha. An optional `Gamma To Linear` conversion can be applied before output.

