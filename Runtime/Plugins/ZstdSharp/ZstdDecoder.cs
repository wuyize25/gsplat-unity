// Copyright (c) 2025 Niantic Spatial
// SPDX-License-Identifier: MIT

using System;
using ZstdSharp;

namespace Gsplat.Internal
{
    // Thin wrapper around ZstdSharp so the rest of the Gsplat package can decompress
    // without referencing the third-party DLL directly. This file lives in the
    // Gsplat.ZstdSharp assembly (which owns the precompiled reference); callers only
    // need to reference Gsplat.ZstdSharp by name.
    //
    // Hold one instance for the duration of a multi-stream load and reuse it across
    // streams — ZstdSharp's Decompressor allocates an internal context per construction
    // and is designed to be reused.
    public sealed class ZstdDecoderSession : IDisposable
    {
        readonly Decompressor _dec = new();

        // Decompresses a single zstd frame into the supplied destination buffer.
        // Returns the number of bytes written.
        public int Decompress(byte[] compressed, byte[] destination)
            => _dec.Unwrap(compressed, destination);

        public void Dispose() => _dec.Dispose();
    }
}
