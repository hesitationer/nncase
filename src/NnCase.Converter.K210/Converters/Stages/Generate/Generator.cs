﻿using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using NnCase.Converter.Converters;
using NnCase.Converter.K210.Converters.Layers;
using NnCase.Converter.K210.Converters.Stages.Inference;
using NnCase.Converter.Model;

namespace NnCase.Converter.K210.Converters.Stages.Generate
{
    public static class Generator
    {
        public static K210BinGenerationContext GenerateBin(Graph graph, Stream stream, int weightsBits, string prefix, InferenceContext inferenceContext)
        {
            var output = inferenceContext.MainMemoryMap[graph.Outputs[0].Input.Connection.From];
            var context = new K210BinGenerationContext
            {
                Prefix = prefix,
                MaxStartAddress = inferenceContext.KPUMemoryAllocator.MaxStart,
                MainMemoryUsage = inferenceContext.MainMemoryAllocator.MaxEnd,
                MainMemoryOutputAddress = output.GetAddress(),
                MainMemoryOutputSize = output.Size,
                Stream = stream,
                WeightsBits = weightsBits
            };

            var converters = (from t in typeof(Generator).Assembly.ExportedTypes
                              let attrs = t.GetCustomAttributes<LayerConverterAttribute>()
                              where attrs.Any()
                              from attr in attrs
                              where attr.LayerType != K210LayerType.Invalid
                              select new
                              {
                                  Key = attr.LayerType,
                                  Value = new { Type = t, Method = t.GetMethod("GenerateBin") }
                              }).ToDictionary(x => x.Key, x => x.Value);

            var layers = inferenceContext.InferenceOrders;
            var bw = new BinaryWriter(stream);

            void GenerateBinLayerBody(K210Layer layer)
            {
                var type = layer.Header.Type;
                if (converters.TryGetValue(type, out var info))
                {
                    if (info.Method != null)
                    {
                        var converter = Activator.CreateInstance(info.Type);
                        info.Method.Invoke(converter, new object[] { bw, layer.Body, context });
                    }
                    else
                    {
                        GenerateBinDefault(bw, layer.Body);
                    }
                }
                else
                {
                    throw new LayerNotSupportedException(type.ToString());
                }

                context.AlignStreamPosition(8);
            }

            uint version = 3;
            uint flags = weightsBits == 8 ? 1u : 0u;
            bw.Write(version);
            bw.Write(flags);
            bw.Write(0);
            bw.Write(layers.Count);
            bw.Write(context.MaxStartAddress);
            bw.Write(context.MainMemoryUsage);
            bw.Write(context.MainMemoryOutputAddress);
            bw.Write(context.MainMemoryOutputSize);

            // Headers
            var fixPosition = bw.BaseStream.Position;
            bw.BaseStream.Position += 4 * 2 * layers.Count;

            for (int i = 0; i < layers.Count; i++)
            {
                var layer = layers[i];
                // BodySize
                var beginPosition = bw.BaseStream.Position;
                GenerateBinLayerBody(layer);
                layer.Header.BodySize = (uint)(bw.BaseStream.Position - beginPosition);
            }

            var newPosition = bw.BaseStream.Position;
            bw.BaseStream.Position = fixPosition;
            for (int i = 0; i < layers.Count; i++)
            {
                var header = layers[i].Header;
                bw.Write((uint)header.Type);
                bw.Write((uint)header.BodySize);
            }

            bw.BaseStream.Position = newPosition;
            return context;
        }

        private static void GenerateBinDefault(BinaryWriter bw, object argument)
        {
            var values = (from p in argument.GetType().GetProperties()
                          orderby p.MetadataToken
                          select p.GetValue(argument)).ToList();

            foreach (var value in values)
            {
                switch (value)
                {
                    case uint v:
                        bw.Write(v);
                        break;
                    case int v:
                        bw.Write(v);
                        break;
                    case K210LayerFlags v:
                        bw.Write((uint)v);
                        break;
                    case K210QuantizationParam v:
                        bw.Write(v.Scale);
                        bw.Write(v.Bias);
                        break;
                    default:
                        throw new InvalidOperationException("Invalid argument member.");
                }
            }
        }
    }
}