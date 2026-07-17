// Copyright (c) 2026
// SPDX-License-Identifier: MIT

using System;
using System.IO;
using WebP;
using UnityEngine;

namespace Gsplat
{
    internal readonly struct SogImage
    {
        public readonly int Width;
        public readonly int Height;
        public readonly Color32[] Pixels;

        public SogImage(int width, int height, Color32[] pixels)
        {
            Width = width;
            Height = height;
            Pixels = pixels;
        }

        public int PixelCount => Width * Height;

        public Color32 this[int index] => Pixel(index % Width, index / Width);

        public Color32 Pixel(int x, int y) => Pixels[x + (Height - 1 - y) * Width];
    }

    public static class SogImageDecoder
    {
        public const string DecoderVersion = "unity-webp-rgba32-rot-wxyz-v1";

        internal static SogImage Decode(byte[] imageBytes, string name)
        {
            if (imageBytes == null || imageBytes.Length == 0)
                throw new InvalidDataException($"SOG: image '{name}' is empty.");

            try
            {
                Texture2DExt.GetWebPDimensions(imageBytes, out int width, out int height);
                var pixelsRgba = Texture2DExt.LoadRGBAFromWebP(imageBytes, ref width, ref height, false, out var error);
                if (error != Error.Success)
                    throw new InvalidDataException($"SOG: failed to decode image '{name}' with libwebp: {error}.");
                if (pixelsRgba == null || pixelsRgba.Length != width * height * 4)
                    throw new InvalidDataException($"SOG: decoded image '{name}' has inconsistent pixel data.");

                var pixels = new Color32[width * height];
                for (int i = 0, j = 0; i < pixels.Length; i++, j += 4)
                {
                    pixels[i] = new Color32(
                        pixelsRgba[j],
                        pixelsRgba[j + 1],
                        pixelsRgba[j + 2],
                        pixelsRgba[j + 3]);
                }

                return new SogImage(width, height, pixels);
            }
            catch (Exception e) when (!(e is InvalidDataException))
            {
                throw new InvalidDataException($"SOG: failed to decode image '{name}': {e.Message}", e);
            }
        }
    }
}
