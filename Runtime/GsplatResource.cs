using System.Runtime.InteropServices;
using UnityEngine;

namespace Gsplat
{
    public abstract class GsplatResource
    {
        public bool Uploaded;
        public uint UploadedCount;
        public abstract void Dispose();
    }

    public class GsplatResourceUncompressed : GsplatResource
    {
        public GraphicsBuffer PositionBuffer { get; private set; }
        public GraphicsBuffer ScaleBuffer { get; private set; }
        public GraphicsBuffer RotationBuffer { get; private set; }
        public GraphicsBuffer ColorBuffer { get; private set; }
        public GraphicsBuffer SHBuffer { get; private set; }

        public GsplatResourceUncompressed(uint splatCount, byte shBands)
        {
            if (splatCount == 0)
                return;
            PositionBuffer = new GraphicsBuffer(GraphicsBuffer.Target.Structured, (int)splatCount,
                Marshal.SizeOf(typeof(Vector3)));
            ScaleBuffer = new GraphicsBuffer(GraphicsBuffer.Target.Structured, (int)splatCount,
                Marshal.SizeOf(typeof(Vector3)));
            RotationBuffer = new GraphicsBuffer(GraphicsBuffer.Target.Structured, (int)splatCount,
                Marshal.SizeOf(typeof(Vector4)));
            ColorBuffer = new GraphicsBuffer(GraphicsBuffer.Target.Structured, (int)splatCount,
                Marshal.SizeOf(typeof(Vector4)));
            if (shBands > 0)
                SHBuffer = new GraphicsBuffer(GraphicsBuffer.Target.Structured,
                    GsplatUtils.SHBandsToCoefficientCount(shBands) * (int)splatCount, Marshal.SizeOf(typeof(Vector3)));
        }

        public override void Dispose()
        {
            PositionBuffer?.Dispose();
            PositionBuffer = null;
            ScaleBuffer?.Dispose();
            ScaleBuffer = null;
            RotationBuffer?.Dispose();
            RotationBuffer = null;
            ColorBuffer?.Dispose();
            ColorBuffer = null;
            SHBuffer?.Dispose();
            SHBuffer = null;
        }
    }

    public class GsplatResourceSpark : GsplatResource
    {
        public GraphicsBuffer PackedSplatsBuffer { get; private set; }
        public GraphicsBuffer SHBuffer { get; private set; }

        public GsplatResourceSpark(uint splatCount, byte shBands) : base()
        {
            if (splatCount == 0)
                return;
            PackedSplatsBuffer = new GraphicsBuffer(GraphicsBuffer.Target.Structured, (int)splatCount,
                Marshal.SizeOf(typeof(uint)) * 4);
            if (shBands > 0)
                SHBuffer = new GraphicsBuffer(GraphicsBuffer.Target.Structured,
                    GsplatUtils.SHBandsToCoefficientCount(shBands) * (int)splatCount, Marshal.SizeOf(typeof(Vector3)));
        }

        public override void Dispose()
        {
            PackedSplatsBuffer?.Dispose();
            PackedSplatsBuffer = null;
            SHBuffer?.Dispose();
            SHBuffer = null;
        }
    }
}
