﻿// <auto-generated>
// Do not edit this file yourself!
//
// This code was generated by Stride Shader Mixin Code Generator.
// To generate it yourself, please install Stride.VisualStudio.Package .vsix
// and re-save the associated .sdfx.
// </auto-generated>

using System;
using Stride.Core;
using Stride.Rendering;
using Stride.Graphics;
using Stride.Shaders;
using Stride.Core.Mathematics;
using Buffer = Stride.Graphics.Buffer;

namespace Stride.Rendering
{
    public static partial class Noise_TextureFXKeys
    {
        public static readonly ValueParameterKey<Vector2> Scale = ParameterKeys.NewValue<Vector2>(new Vector2(4,4));
        public static readonly ValueParameterKey<Vector2> Offset = ParameterKeys.NewValue<Vector2>();
        public static readonly ValueParameterKey<float> Z = ParameterKeys.NewValue<float>();
        public static readonly ValueParameterKey<uint> Type = ParameterKeys.NewValue<uint>();
    }
}