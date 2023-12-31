﻿using DirectX12GameEngine.Shaders;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using System.Windows.Media.Imaging;
using Vortice.Direct3D12;
using wired.Graphics;
using wired.Rendering;
using static wired.Assets.StringExtensions;
using Format = Vortice.DXGI.Format;

namespace wired.Assets {
    class XModelLoader {
        readonly GraphicsDevice mDevice;
        readonly IDictionary<Mesh, Matrix4x4> mFrames;

        XModelLoader(GraphicsDevice device, IDictionary<Mesh, Matrix4x4> frames) {
            mDevice = device ?? throw new ArgumentNullException(nameof(device));
            mFrames = frames ?? throw new ArgumentNullException(nameof(frames));
            //mCreatedMaterials = new Dictionary<Material, DirectX12GameEngine.Rendering.Material>();
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="device"></param>
        /// <param name="modelPath"></param>
        /// <returns></returns>
        public static XModelLoader Create3(GraphicsDevice device, string modelPath) {
            ReadOnlySpan<char> xFile = File.ReadAllText(modelPath).AsSpan();

            var enumerator = new LineSplitEnumerator(xFile);
            if (!enumerator.MoveNext())
                throw new InvalidOperationException("File does not appear to be a DirectX .X file, it is empty.");

            //https://docs.microsoft.com/en-us/windows/win32/direct3d9/reserved-words--header--and-comments
            var firstLine = enumerator.Current.Line;
            if (!firstLine.StartsWith("xof ".AsSpan()))
                throw new InvalidOperationException("File does not appear to be a DirectX .X file.");

            var version = new Version($"{firstLine.Slice(4, 2).ToString()}.{firstLine.Slice(6, 2).ToString()}");
            if (version.Major != 3 || version.Minor != 3)
                throw new NotSupportedException($"DirectX .X files at v3.3 only are supported.  Found v{version}.");

            var formatType = firstLine.Slice(8, 3);
            if (!formatType.Equals("txt".AsSpan(), StringComparison.Ordinal))
                throw new NotSupportedException($"DirectX .X files in txt format only are supported.  Encounted: {formatType.ToString()}.");

            void ParseDataObjects(ReadOnlySpan<char> remainingLines, out IEnumerable<Template> dataObjectLines, out IEnumerable<string> memberLines) {
                var openingBraceSpan = "{".AsSpan();
                var closingBraceSpan = "}".AsSpan();
                char[] trimChars = { ' ', '{' };

                var members = new List<string>();
                var dataObjects = new List<Template>();
                var firstLineOfCurrentData = ReadOnlySpan<char>.Empty;
                var startIndexOfCurrentData = 0;
                var terminatingIndexOfCurrentData = 0;
                int braceCount = 0;
                var lines = new LineSplitEnumerator(remainingLines);
                while (lines.MoveNext()) {
                    var terminatingIndexOfPreviousLine = terminatingIndexOfCurrentData;
                    terminatingIndexOfCurrentData += lines.Current.Line.Length + lines.Current.Separator.Length;

                    var nextLine = lines.Current.Line.Trim();
                    if (!nextLine.IsEmpty) {
                        if (firstLineOfCurrentData == ReadOnlySpan<char>.Empty) {
                            startIndexOfCurrentData = terminatingIndexOfPreviousLine;
                            firstLineOfCurrentData = nextLine;
                        }
                    }

                    if (nextLine.EndsWith(openingBraceSpan)) {
                        if (!firstLineOfCurrentData.IsEmpty && !firstLineOfCurrentData.EndsWith(openingBraceSpan)) {
                            //If member lines thus far are not part of some other data object, then clear them out so that
                            //when encountering the closing brace for this object, we do not attempt to include them as part of it
                            var memberLineRanges = new LineSplitEnumerator(remainingLines.Slice(startIndexOfCurrentData, terminatingIndexOfPreviousLine - startIndexOfCurrentData));
                            while (memberLineRanges.MoveNext()) {
                                var trimmedLine = memberLineRanges.Current.Line.Trim();
                                if (trimmedLine.Length > 0)
                                    members.Add(trimmedLine.ToString());
                            }
                            startIndexOfCurrentData = terminatingIndexOfPreviousLine;
                            firstLineOfCurrentData = nextLine;
                        }
                        braceCount++;
                        continue;
                    }

                    if (nextLine.EndsWith(closingBraceSpan) && !nextLine.StartsWith(openingBraceSpan)) { //e.g. Ignore data references when parsing data objects, treat them as just member lines
                        --braceCount;

                        if (braceCount == 0) {
                            var identifier = firstLineOfCurrentData.Slice(0, firstLineOfCurrentData.IndexOf(' '));
                            var name = firstLineOfCurrentData.Slice(identifier.Length + 1).TrimEnd(trimChars.AsSpan());

                            //Exclude the closing brace we are parsing right now (hence, up to the previous line, not the current one)
                            var memberDefinition = remainingLines.Slice(startIndexOfCurrentData, terminatingIndexOfPreviousLine - startIndexOfCurrentData);
                            //Also exclude the first line already parsed, we can't slice from firstLineOfCurrentData.Length since it has been trimmed, so find it again and slice on it
                            var memberDefinitionLines = new LineSplitEnumerator(memberDefinition);
                            memberDefinitionLines.MoveNext();
                            memberDefinition = memberDefinition.Slice(memberDefinitionLines.Current.Line.Length + memberDefinitionLines.Current.Separator.Length);

                            ParseDataObjects(memberDefinition, out IEnumerable<Template> memberDataObjects, out IEnumerable<string> memberPrimitives);
                            dataObjects.Add(new Template {
                                Identifier = identifier.ToString(),
                                Name = name.ToString(),
                                Members = memberPrimitives,
                                MemberDataObjects = memberDataObjects,
                            });

                            startIndexOfCurrentData = terminatingIndexOfCurrentData;  //Skip over the closing brace
                            firstLineOfCurrentData = ReadOnlySpan<char>.Empty;
                        }
                    }
                };

                dataObjectLines = dataObjects.AsReadOnly();
                if (startIndexOfCurrentData != terminatingIndexOfCurrentData) {
                    var memberLineRanges = new LineSplitEnumerator(remainingLines.Slice(startIndexOfCurrentData, terminatingIndexOfCurrentData - startIndexOfCurrentData));
                    while (memberLineRanges.MoveNext()) {
                        var trimmedLine = memberLineRanges.Current.Line.Trim();
                        if (trimmedLine.Length > 0/* && !trimmedLine.EndsWith(closingBraceSpan)*/)  //Exclude any terminating braces
                            members.Add(trimmedLine.ToString());
                    }
                }
                memberLines = members.AsReadOnly();
            }

            bool TryGetFirst(IEnumerable<Template> dataObjects, string identifier, out Template dataObject) {
                dataObject = dataObjects.FirstOrDefault(d => d.Identifier == identifier);
                return dataObject.Identifier == identifier;
            }

            //Skips all the template definitions at the top to the first data object, which is always a material object among the Mutiny assets
            int indexOfCurrentLine = 0;
            do {
                if (enumerator.Current.Line.StartsWith("Material ".AsSpan()))
                    break;

                indexOfCurrentLine += enumerator.Current.Line.Length + enumerator.Current.Separator.Length;  //On the first execution, this will represent the firstLine already parsed above
            } while (enumerator.MoveNext());

            ParseDataObjects(xFile.Slice(indexOfCurrentLine), out var rootLevelDataObjects, out _);

            var loadedMaterials = new Dictionary<string, Material>();
            foreach (var materialData in rootLevelDataObjects.Where(d => d.Identifier == "Material")) {
                //https://docs.microsoft.com/en-us/windows/win32/direct3d9/material <3D82AB4D-62DA-11CF-AB39-0020AF71E433>
                //Also https://xbdev.net/3dformats/x/x_formats_workings/prt1/prt1.php

                float[] ReadColorRGBA(string memberLine)
                    //r, g, b, a https://docs.microsoft.com/en-us/windows/win32/direct3d9/colorrgba <35FF44E0-6C7C-11cf-8F52-0040333594A3>
                    => memberLine.Split(new[] { ';' }, StringSplitOptions.RemoveEmptyEntries)
                                 .Select(c => float.TryParse(c, out var com) ? com : 0.0f)
                                 .ToArray();

                var nextUnparsedLineIndex = 0;
                var faceColor = ReadColorRGBA(materialData.Members.Skip(nextUnparsedLineIndex++).First());
                var specularColourExponent = float.TryParse(materialData.Members.Skip(nextUnparsedLineIndex++).First().TrimEnd(';'), out var power) ? power : 0.0f;
                var specularColor = ReadColorRGBA(materialData.Members.Skip(nextUnparsedLineIndex++).First());
                var emissiveColor = ReadColorRGBA(materialData.Members.Skip(nextUnparsedLineIndex++).First());

                //This is an open template, but the X files we are loading we know will contain a string member representing the texture, so we'll type our class accordingly
                var textureFilename = materialData.MemberDataObjects.FirstOrDefault(d => d.Identifier == "TextureFilename").Members.First().Trim('"', ';');

                loadedMaterials[materialData.Name] = new Material(new Vector4(faceColor[0], faceColor[1], faceColor[2], faceColor[3]),
                                                                  specularColourExponent,
                                                                  new Vector3(specularColor[0], specularColor[1], specularColor[2]),
                                                                  new Vector3(emissiveColor[0], emissiveColor[1], emissiveColor[2]),
                                                                  textureFilename);
            }

            var frames = new Dictionary<Mesh, Matrix4x4>();
            if (TryGetFirst(rootLevelDataObjects, "Frame", out Template frameData)) {
                Matrix4x4 worldMatrix = Matrix4x4.Identity;

                Matrix4x4 ParseMatrix4x4(string matrix4x4Member) {
                    //https://docs.microsoft.com/en-us/windows/win32/direct3d9/matrix4x4 <F6F23F45-7686-11cf-8F52-0040333594A3>
                    var matrix = matrix4x4Member.TrimEnd(';')
                                                .Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries)
                                                .Select(c => float.TryParse(c, out var com) ? com : 0.0f)
                                                .ToArray();
                    return new Matrix4x4(matrix[0], matrix[1], matrix[2], matrix[3],
                                         matrix[4], matrix[5], matrix[6], matrix[7],
                                         matrix[8], matrix[9], matrix[10], matrix[11],
                                         matrix[12], matrix[13], matrix[14], matrix[15]);
                }

                if (TryGetFirst(frameData.MemberDataObjects, "FrameTransformMatrix", out Template frameTransformMatrixData)) {
                    //https://docs.microsoft.com/en-us/windows/win32/direct3d9/frametransformmatrix <F6F23F41-7686-11cf-8F52-0040333594A3>
                    worldMatrix = ParseMatrix4x4(frameTransformMatrixData.Members.First());
                }

                if (TryGetFirst(frameData.MemberDataObjects, "Frame", out Template childFrameData)) {
                    var primitiveWorldMatrix = worldMatrix;

                    if (TryGetFirst(childFrameData.MemberDataObjects, "FrameTransformMatrix", out Template childFrameTransformMatrixData)) {
                        //https://docs.microsoft.com/en-us/windows/win32/direct3d9/frametransformmatrix <F6F23F41-7686-11cf-8F52-0040333594A3>
                        var primitiveTransform = ParseMatrix4x4(childFrameTransformMatrixData.Members.First());
                        primitiveWorldMatrix = Matrix4x4.Multiply(primitiveWorldMatrix, primitiveTransform);
                    }

                    if (TryGetFirst(childFrameData.MemberDataObjects, "Mesh", out Template meshData)) {
                        int ReadDword(string dWordMember)
                            => int.TryParse(dWordMember.TrimEnd(';'), out var dWordValue)
                             ? dWordValue
                             : default;

                        string ReadArrayString(IEnumerable<string> memberLines, string arrayTerminator, ref int nextUnparsedLineIndex) {
                            bool YetToEncounterArrayTerminator(string l) => !l.EndsWith(arrayTerminator);
                            var arrayLines = memberLines.TakeWhile(YetToEncounterArrayTerminator)
                                                        .Concat(memberLines.SkipWhile(YetToEncounterArrayTerminator).Take(1))
                                                        .ToArray();
                            nextUnparsedLineIndex += arrayLines.Length;
                            return string.Join(string.Empty, arrayLines)
                                         .TrimEnd(';');
                        }

                        void ParseArrayData(IEnumerable<string> memberLines, string arrayTerminator, ref int nextUnparsedLineIndex, out int arrayLength, out string arrayString) {
                            arrayLength = ReadDword(memberLines.Skip(nextUnparsedLineIndex++).First());
                            arrayString = ReadArrayString(memberLines.Skip(nextUnparsedLineIndex), arrayTerminator, ref nextUnparsedLineIndex);
                        }

                        IEnumerable<string[]> ReadArrayOfArrays(string arrayData, int membersPerElement) {
                            var outerArrayElements = new List<string[]>();
                            var innerArrayElements = new List<string>();

                            int charactersParsed = 0;
                            while (charactersParsed < arrayData.Length) {
                                var elementMember = arrayData.Skip(charactersParsed).TakeWhile(c => c != ';').ToArray();
                                charactersParsed += elementMember.Length + 1;
                                innerArrayElements.Add(new string(elementMember));
                                if (innerArrayElements.Count == membersPerElement) {
                                    outerArrayElements.Add(innerArrayElements.ToArray());
                                    innerArrayElements.Clear();

                                    //Assuming membersPerElement is correct and we are on the last element of the array,
                                    //the next character should be the element separator for the outer array elements
                                    if (charactersParsed < arrayData.Length && arrayData.Skip(charactersParsed).First() == ',')
                                        charactersParsed += 1;
                                }
                            }

                            return outerArrayElements.AsReadOnly();
                        }

                        void ParseVectorAndMeshFaceArrays(IEnumerable<string> memberLines, out IEnumerable<IEnumerable<float>> vectors, out (int NumberOfIndices, int[] FaceVertexIndices)[] meshFaces) {
                            var nextUnparsedLineIndex = 0;

                            //An array of Vectors (containing x, y, and z components) <3D82AB5E-62DA-11cf-AB39-0020AF71E433>
                            ParseArrayData(memberLines, ";;", ref nextUnparsedLineIndex, out var numberOfVectors, out var vectorArray);
                            vectors = vectorArray.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries)
                                                 .Select(v => v.Split(new[] { ';' }, StringSplitOptions.RemoveEmptyEntries)
                                                               .Select(c => float.TryParse(c, out var com) ? com : 0.0f));
                            Debug.Assert(vectors.Count() == numberOfVectors);

                            //An array of MeshFaces (containing numberOfIndices, followed by an array of vertex indices) <3D82AB5F-62DA-11cf-AB39-0020AF71E433>
                            ParseArrayData(memberLines, ";;", ref nextUnparsedLineIndex, out var numberOfFaces, out var meshFaceArray);
                            meshFaces = ReadArrayOfArrays(meshFaceArray, 2)
                                       .Select(e => {
                                                   int.TryParse(e[0], out var numberOfIndices);
                                                   var faceVertexIndices = e[1].Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries)
                                                                                   .Select(c => int.TryParse(c, out var com) ? com : 0)
                                                                                   .ToArray();
                                                   Debug.Assert(faceVertexIndices.Length == numberOfIndices);
                                                   return (NumberOfIndices: numberOfIndices,
                                                           FaceVertexIndices: faceVertexIndices);
                                               })
                                       .ToArray();
                            Debug.Assert(meshFaces.Length == numberOfFaces);
                        }


                        ParseVectorAndMeshFaceArrays(meshData.Members, out var vertices, out var faces);
                        var meshNormalsData = meshData.MemberDataObjects.Where(d => d.Identifier == "MeshNormals");
                        MeshNormals? meshNormals = null;
                        if (meshNormalsData.Any()) {
                            ParseVectorAndMeshFaceArrays(meshNormalsData.First().Members,
                                                         out var normals, out var faceNormals);
                            meshNormals = new MeshNormals {
                                Normals = normals.Select(f => new Vector3(f.First(), f.Skip(1).First(), f.Skip(2).Single())).ToArray(),
                                FaceVertexIndices = faceNormals.SelectMany(f => f.FaceVertexIndices).ToArray(),
                            };
                        }
                        //https://docs.microsoft.com/en-us/windows/win32/direct3d9/meshtexturecoords <F6F23F40-7686-11cf-8F52-0040333594A3>
                        var meshTexCoordsNextUnparsedLineIndex = 0;

                        //An array of Coords2d (containing u and v components) <F6F23F44-7686-11cf-8F52-0040333594A3>
                        ParseArrayData(meshData.MemberDataObjects.First(d => d.Identifier == "MeshTextureCoords").Members, ";;",
                                       ref meshTexCoordsNextUnparsedLineIndex, out var numberOfTextureCoords, out var coords2dArray);
                        var textureCoords = coords2dArray.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries)
                                                         .Select(v => v.Split(new[] { ';' }, StringSplitOptions.RemoveEmptyEntries)
                                                                       .Select(c => float.TryParse(c, out var com) ? com : 0.0f));
                        Debug.Assert(textureCoords.Count() == numberOfTextureCoords);


                        var mesh = new Mesh {
                            Vertices = vertices.Select(f => new Vector3(f.First(), f.Skip(1).First(), f.Skip(2).Single())).ToArray(),
                            FaceVertexIndices = faces.SelectMany(f => f.FaceVertexIndices).ToArray(),
                            FaceNormals = meshNormals,
                            TextureCoords = textureCoords.Select(f => new Vector2(f.First(), f.Skip(1).Single())).ToArray(),
                        };


                        //https://docs.microsoft.com/en-us/windows/win32/direct3d9/meshmateriallist <F6F23F42-7686-11CF-8F52-0040333594A3>
                        if (TryGetFirst(meshData.MemberDataObjects, "MeshMaterialList", out Template meshMaterialListData)) {
                            var meshMaterialsNextUnparsedLineIndex = 0;

                            var numberOfMaterials = ReadDword(meshMaterialListData.Members.Skip(meshMaterialsNextUnparsedLineIndex++).First());

                            //An array of Coords2d (containing u and v components) <F6F23F44-7686-11cf-8F52-0040333594A3>
                            ParseArrayData(meshMaterialListData.Members, ";", ref meshMaterialsNextUnparsedLineIndex,
                                           out var numberOfFaceIndexes, out var dWordArray);
                            var faceIndexes = dWordArray.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries)
                                                        .Select(v => v.Split(new[] { ';' }, StringSplitOptions.RemoveEmptyEntries)
                                                                      .Select(c => int.TryParse(c, out var com) ? com : 0));
                            Debug.Assert(faceIndexes.Count() == numberOfFaceIndexes);

                            var materialReferences = meshMaterialListData.Members.Skip(meshMaterialsNextUnparsedLineIndex)
                                                                                 .Take(numberOfMaterials)
                                                                                 .Select(r => r.Trim('{', ' ', '}'))
                                                                                 .ToArray();
                            mesh.Materials = new MeshMaterialList {
                                FaceIndices = faceIndexes.SelectMany(i => i).ToArray(),
                                Materials = materialReferences.Select(r => loadedMaterials[r]).ToArray(),
                            };
                        }

                        frames[mesh] = primitiveWorldMatrix;
                    }
                }
            }

            return new XModelLoader(device, frames);
        }

        static ID3D12Resource CreateBufferOnDefaultHeap<T>(GraphicsDevice device, CommandQueue copyCommandQueue, Span<T> data, ResourceFlags bufferFlags) where T : unmanaged {
            //Create buffer
            var size = (ulong)(data.Length * Unsafe.SizeOf<T>());
            ID3D12Resource buffer = device.NativeDevice.CreateCommittedResource(HeapProperties.DefaultHeapProperties, HeapFlags.None, ResourceDescription.Buffer(size, bufferFlags), ResourceStates.Common);

            //Set data
            using ID3D12Resource uploadBuffer = CreateBufferOnUploadHeap(device.NativeDevice, data, ResourceFlags.None);
            //Copy from the upload buffer to the buffer
            using var copyCommandList = new CommandList(device, CommandListType.Copy);
            copyCommandList.CopyBufferRegion(buffer, 0, uploadBuffer, 0, size);

            //Using this abstraction for ID3D12CommandQueue (and its abstraction for closed command lists), can force a wait for the queue to complete execution and ensure CPU-GPU synchronisation
            //so that when execution leaves this method, the CPU doesn't clean up resources the GPU still requires resulting in an SEHException
            CompiledCommandList compiledCommandList = copyCommandList.Close();
            copyCommandQueue.ExecuteCommandLists(compiledCommandList);

            return buffer;
        }

        static ID3D12Resource CreateBufferOnUploadHeap<T>(ID3D12Device device, Span<T> data, ResourceFlags bufferFlags) where T : unmanaged {
            //Create buffer
            int size = data.Length * Unsafe.SizeOf<T>();
            ID3D12Resource buffer = device.CreateCommittedResource(HeapProperties.UploadHeapProperties, HeapFlags.None, ResourceDescription.Buffer(size, bufferFlags), ResourceStates.GenericRead);

            //Set data
            buffer.SetData<T>(data);
            return buffer;
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="texturesFolder"></param>
        /// <param name="useTextureCoordinates"></param>
        /// <returns></returns>
        public async Task<IEnumerable<(ID3D12Resource IndexBuffer, ID3D12Resource VertexBuffer, IEnumerable<ShaderResourceView> ShaderResourceViews, Model Model)>> GetFlatShadedMeshesAsync(string texturesFolder, bool useTextureCoordinates) {
            var meshes = new List<(ID3D12Resource IndexBuffer, ID3D12Resource VertexBuffer, IEnumerable<ShaderResourceView> ShaderResourceViews, Model Model)>();

            foreach (var mesh in mFrames.Keys) {
                ushort[] faceVertexIndices = mesh.FaceVertexIndices.Select(Convert.ToUInt16).ToArray();
                var areIndices32Bit = System.Runtime.InteropServices.Marshal.SizeOf(faceVertexIndices.GetType().GetElementType()!) == sizeof(int);
                ID3D12Resource indexBuffer = CreateBufferOnDefaultHeap(mDevice, mDevice.CopyCommandQueue, GetIndexBuffer(faceVertexIndices), ResourceFlags.None);
                var indexBufferView = new IndexBufferView(indexBuffer.GPUVirtualAddress, (int)indexBuffer.Description.Width, areIndices32Bit);

                var worldMatrix = mFrames[mesh] * Matrix4x4.CreateScale(0.1f);  //For D3D12Bundles, and HelloTexture (with further scaling and translation compensation in its vertex shader)
                var triangleVertices = mesh.Vertices.Select((f, i) => new FlatShadedVertex(Vector3.Transform(f, worldMatrix), mesh.TextureCoords[i]));
                byte[] vertexData = triangleVertices.SelectMany(v => BitConverter.GetBytes(v.Position.X)
                                                                                 .Concat(BitConverter.GetBytes(v.Position.Y))
                                                                                 .Concat(BitConverter.GetBytes(v.Position.Z))
                                                                                 .Concat(BitConverter.GetBytes(v.TexCoord.X))
                                                                                 .Concat(BitConverter.GetBytes(v.TexCoord.Y)))
                                                    .ToArray();

                // Note: using upload heaps to transfer static data like vert buffers is not 
                // recommended. Every time the GPU needs it, the upload heap will be marshalled 
                // over. Please read up on Default Heap usage. An upload heap is used here for 
                // code simplicity and because there are very few verts to actually transfer.
                var vertexBuffer = CreateBufferOnUploadHeap(mDevice.NativeDevice, new Span<byte>(vertexData), ResourceFlags.None);
                //The above overload, because its Dimension is Buffer but its HeapType is Upload instead of Default, call Map(0), data.CopyTo, then Unmap(0)

                var vertexBufferView = new VertexBufferView(vertexBuffer.GPUVirtualAddress, (int)vertexBuffer.Description.Width, Unsafe.SizeOf<FlatShadedVertex>());

                IShader? shader = null;
                if (mesh.Materials.HasValue) {
                    var firstMaterialDefinition = mesh.Materials.Value.Materials.First();
                    var textureFile = new FileInfo(Path.Combine(texturesFolder, firstMaterialDefinition.TextureFilename));
                    if (textureFile.Exists) {
                        using FileStream stream = File.OpenRead(textureFile.FullName);
                        Texture2dBuilder textureBuilder = textureFile.Extension switch {
                            ".dds" => new DdsTexture2dBuilder(mDevice),
                            ".jpg" => new JpgTexture2dBuilder(mDevice),
                            _ => throw new NotSupportedException($"Cannot load {textureFile.Extension} images.  Only DDS or JPG supported for use as textures."),
                        };

                        Texture diffuseTexture = textureBuilder.Create2dTexture(stream);

                        shader = new TextureShader(diffuseTexture, false);
                    }
                }
                else {
                    shader = new ColourShader();
                }

                if (shader == null) {
                    throw new InvalidOperationException($"No shader was selected for compilation! This indicates the model has a material, however the referenced texture could not be found.");
                }
                var context = new ShaderGeneratorContext(mDevice);
                context.Visit(shader);

                //Describe and create the graphics pipeline state object (PSO)
                PipelineState pipelineState = await context.CreateGraphicsPipelineStateAsync(FlatShadedVertex.InputElements);

                var primitiveWorldMatrix = mFrames[mesh];
                var material = new MaterialPass {
                    PassIndex = 0,
                    PipelineState = pipelineState,
                    ShaderResourceViewDescriptorSet = context.CreateShaderResourceViewDescriptorSet(),
                    SamplerDescriptorSet = context.CreateSamplerDescriptorSet(),
                };

                var meshDraw = new MeshDraw {
                    IndexBufferView = indexBufferView,
                    VertexBufferViews = new[] { vertexBufferView },
                };
                var model = new Model();
                model.Materials.Add(material);
                model.Meshes.Add(new wired.Rendering.Mesh(meshDraw) { MaterialIndex = 0, WorldMatrix = primitiveWorldMatrix });
                meshes.Add((indexBuffer, vertexBuffer, context.ShaderResourceViews, model));
            }

            return meshes.AsReadOnly();
        }

        abstract class Texture2dBuilder {
            readonly GraphicsDevice mDevice;

            protected Texture2dBuilder(GraphicsDevice device) {
                mDevice = device ?? throw new ArgumentNullException(nameof(device));
            }

            public Texture Create2dTexture(FileStream stream) {
                (Format format, uint width, uint height, Memory<byte> data) = ReadImageStream(stream);
                var textureDesc = ResourceDescription.Texture2D(format, width, height, 1, 1, 1, 0, ResourceFlags.None);

                var texture = new Texture(mDevice, textureDesc, HeapType.Default);
                //I can't for the life of me figure out why this doesn't work:
                //texture.SetData(data.Span);

                //But the below does:
                var uploadBufferSize = texture.SizeInBytes * 4;

                var textureUploadBuffer = GraphicsResource.CreateBuffer(mDevice, (int)uploadBufferSize, ResourceFlags.None, HeapType.Upload);
                textureUploadBuffer.NativeResource.Name = nameof(textureUploadBuffer);
                mDevice.CommandList.UpdateSubresource(texture, textureUploadBuffer.NativeResource, 0, 0, data.Span);

                return texture;
            }

            protected abstract (Format Format, uint Width, uint Height, Memory<byte> Data) ReadImageStream(Stream stream);
        }

        class DdsTexture2dBuilder : Texture2dBuilder {
            public DdsTexture2dBuilder(GraphicsDevice device) : base(device) {
            }

            protected override (Format Format, uint Width, uint Height, Memory<byte> Data) ReadImageStream(Stream stream) {
                var pfimConfig = new Pfim.PfimConfig();
                Pfim.Dds image = Pfim.Dds.Create(stream, pfimConfig);

                var format = image.Format == Pfim.ImageFormat.Rgba32
                           ? Format.R8G8B8A8_UNorm
                           : Format.D24_UNorm_S8_UInt;  //Used for a depth stencil, for a 24bit image consider Format.R8G8B8_UNorm

                return (format, (uint)image.Width, (uint)image.Height, image.Data);
            }
        }

        class JpgTexture2dBuilder : Texture2dBuilder {
            public JpgTexture2dBuilder(GraphicsDevice device) : base(device) {
            }

            protected override (Format Format, uint Width, uint Height, Memory<byte> Data) ReadImageStream(Stream stream) {
                var decoder = BitmapDecoder.Create(stream, BitmapCreateOptions.None, BitmapCacheOption.OnLoad);
                var firstFrame = decoder.Frames.First();

                var format = firstFrame.Format.BitsPerPixel == 32
                           ? Format.R8G8B8A8_UNorm
                           : Format.D24_UNorm_S8_UInt; //Used for a depth stencil, for a 24bit image consider Format.R8G8B8_UNorm
                var pixelSizeInBytes = firstFrame.Format.BitsPerPixel / 8;
                var stride = firstFrame.PixelWidth * pixelSizeInBytes;
                var pixels = new byte[firstFrame.PixelHeight * stride];
                firstFrame.CopyPixels(pixels, stride, 0);

                return (format, (uint)firstFrame.PixelWidth, (uint)firstFrame.PixelHeight, pixels);
            }
        }


        static Vector3[] GenerateVertexNormals(Mesh mesh) {
            //Create VertexBuffer for the normal data
            if (!mesh.FaceNormals.HasValue)
                throw new InvalidOperationException("No Normal data was found in the model");

            var vertexNormals = new Vector3[mesh.Vertices.Length];
            int i = 0;
            var meshFaces = (from s in mesh.FaceVertexIndices
                             let num = i++
                             group s by num / 3 into g
                             select g.ToList())
                            .Select((f, fi) => new { VertexIndices = f, Index = fi })
                            .ToArray();
            i = 0;
            var normalFaces = (from s in mesh.FaceNormals.Value.FaceVertexIndices
                               let num = i++
                               group s by num / 3 into g
                               select g.ToArray())
                              .ToArray();

            for (int meshVertexIndex = 0; meshVertexIndex < vertexNormals.Length; ++meshVertexIndex) {
                var associatedNormals = meshFaces.Where(f => f.VertexIndices.Contains(meshVertexIndex))
                                                 .Select(f => new { //Select the participating Face indices, and which vertex of the face (0, 1, or 2) it participates in
                                                             FaceIndex = f.Index,
                                                             FaceVertexIndex = f.VertexIndices.IndexOf(meshVertexIndex),
                                                         })
                                                 .Select(f => { //Lookup the associated normal face to get the normal associated with that face vertex
                                                             var normalIndex = normalFaces[f.FaceIndex].Skip(f.FaceVertexIndex).First();
                                                             return mesh.FaceNormals.Value.Normals[normalIndex];
                                                         });
                //Sum all the associated face normals together
                var vertexNormal = Vector3.Zero;
                foreach (var normal in associatedNormals) {
                    vertexNormal += normal;
                }
                //Normalise this sum, to determine the vertex normal
                vertexNormals[meshVertexIndex] = Vector3.Normalize(vertexNormal);
            }

            return vertexNormals;
        }

        static Span<byte> GetIndexBuffer(ushort[] faceVertexIndices)
            => new Span<byte>(faceVertexIndices.SelectMany(BitConverter.GetBytes).ToArray());

        static Span<byte> GetVertexBuffer(Vector2[] vertices)
            => new Span<byte>(vertices.SelectMany(v => BitConverter.GetBytes(v.X)
                                                                   .Concat(BitConverter.GetBytes(v.Y)))
                                      .ToArray());

        static Span<byte> GetVertexBuffer(Vector3[] vertices)
            => new Span<byte>(vertices.SelectMany(v => BitConverter.GetBytes(v.X)
                                                                   .Concat(BitConverter.GetBytes(v.Y))
                                                                   .Concat(BitConverter.GetBytes(v.Z)))
                                      .ToArray());

        #region Types
        [DebuggerDisplay("{Identifier} {Name}")]
        struct Template {
            public string Identifier;
            public string Name;
            public IEnumerable<string> Members;
            public IEnumerable<Template> MemberDataObjects;
        }

        /// <summary>
        /// 
        /// </summary>
        /// <remarks>https://docs.microsoft.com/en-us/windows/win32/direct3d9/material
        /// 3D82AB4D-62DA-11CF-AB39-0020AF71E433
        /// Made a class rather than a struct so that it can be referenced from MeshMaterialList data objcets</remarks>
        class Material {
            /// <summary>
            /// Face colour
            /// </summary>
            /// <remarks>https://docs.microsoft.com/en-us/windows/win32/direct3d9/colorrgba</remarks>
            public Vector4 FaceColour { get; }
            /// <summary>
            /// Material specular colour exponent
            /// </summary>
            public float Power { get; }
            /// <summary>
            /// Material specular colour
            /// </summary>
            /// <remarks>https://docs.microsoft.com/en-us/windows/win32/direct3d9/colorrgb</remarks>
            public Vector3 SpecularColour { get; }
            /// <summary>
            /// Material emissive colour
            /// </summary>
            /// <remarks>https://docs.microsoft.com/en-us/windows/win32/direct3d9/colorrgb</remarks>
            public Vector3 EmissiveColour { get; }
            /// <summary>
            /// This is actually an "open template" and has no restriction on additional data that may be included, but we'll restrict it to containing the texture filename, as that's all we use in the Mutiny model assets
            /// </summary>
            /// <remarks>https://docs.microsoft.com/en-us/windows/win32/direct3d9/texturefilename A42790E1-7810-11cf-8F52-0040333594A3</remarks>
            public string TextureFilename { get; }

            /// <summary>
            ///
            /// </summary>
            /// <param name="faceColour"></param>
            /// <param name="power"></param>
            /// <param name="specularColour"></param>
            /// <param name="emissiveColour"></param>
            /// <param name="textureFilename"></param>
            public Material(Vector4 faceColour, float power, Vector3 specularColour, Vector3 emissiveColour, string textureFilename) {
                FaceColour = faceColour;
                Power = power;
                SpecularColour = specularColour;
                EmissiveColour = emissiveColour;
                TextureFilename = textureFilename;
            }
        }

        /// <summary>
        /// https://docs.microsoft.com/en-us/windows/win32/direct3d9/meshnormals
        /// </summary>
        /// <remarks>F6F23F43-7686-11cf-8F52-0040333594A3</remarks>
        struct MeshNormals {
            /// <summary>
            /// 
            /// </summary>
            public Vector3[] Normals;
            /// <summary>
            /// Specifies which normals should be applied to a given face, the length should be equal to the number of faces in a mesh
            /// </summary>
            public int[] FaceVertexIndices;
        }

        /// <summary>
        /// Specifies which material applies to which faces
        /// </summary>
        /// <remarks>https://docs.microsoft.com/en-us/windows/win32/direct3d9/meshmateriallist
        /// F6F23F42-7686-11CF-8F52-0040333594A3</remarks>
        struct MeshMaterialList {
            /// <summary>
            /// Length should match the number of faces in the mesh, each value indicates the index of the Materials array to apply
            /// </summary>
            public int[] FaceIndices;
            /// <summary>
            /// References a material template (because typed as struct instead of 
            /// </summary>
            public Material[] Materials;
        }

        /// <summary>
        /// https://docs.microsoft.com/en-us/windows/win32/direct3d9/mesh
        /// </summary>
        /// <remarks>3D82AB44-62DA-11CF-AB39-0020AF71E433</remarks>
        struct Mesh {
            public Vector3[] Vertices;
            public int[] FaceVertexIndices;
            public MeshNormals? FaceNormals;
            public MeshMaterialList? Materials;
            public Vector2[] TextureCoords;
        }

        #endregion
    }

    public readonly struct FlatShadedVertex {
        /// <summary>
        /// Defines the vertex input layout.
        /// NOTE: The HLSL Semantic names here must match the ShaderTypeAttribute.TypeName associated with the ShaderSemanticAttribute associated with the 
        ///       compiled Vertex Shader's Input parameters - PositionSemanticAttribute and TextureCoordinateSemantic in this case per the VSInput struct
        /// </summary>
        public static readonly InputElementDescription[] InputElements = new[] {
            new InputElementDescription("Position", 0, Format.R32G32B32_Float, InputElementDescription.AppendAligned, 0, InputClassification.PerVertexData, 0),
            new InputElementDescription("TexCoord", 0, Format.R32G32_Float, InputElementDescription.AppendAligned, 0, InputClassification.PerVertexData, 0),
        };

        public FlatShadedVertex(in Vector3 position, in Vector2 texCoord) {
            Position = position;
            TexCoord = texCoord;
        }

        public readonly Vector3 Position;
        public readonly Vector2 TexCoord;
    }

    /// <summary>
    /// Based on https://github.com/microsoft/DirectX-Graphics-Samples/blob/master/Samples/UWP/D3D12HelloWorld/src/HelloTriangle/shaders.hlsl
    /// </summary>
    class ColourShader : IShader {
        [Shader("vertex")]
        [ShaderMethod]
        public PSCVInput VSMain([PositionSemantic] Vector4 position, [ColorSemantic] Vector4 colour/*, [SystemInstanceIdSemantic] uint instanceId*/) {
            PSCVInput result;
            result.Position = position;
            result.Colour = colour;
            return result;
        }

        [Shader("pixel")]
        [ShaderMethod]
        [return: SystemTargetSemantic]
        public Vector4 PSMain(PSCVInput input) {
            return input.Colour;
        }

        public void Accept(ShaderGeneratorContext context) {
            //Do nothing
        }
    }

    /// <summary>
    /// Based on https://github.com/microsoft/DirectX-Graphics-Samples/blob/master/Samples/UWP/D3D12HelloWorld/src/HelloTexture/shaders.hlsl
    /// </summary>
    class TextureShader : IShader {
#pragma warning disable CS8618 // Non-nullable field must contain a non-null value when exiting constructor. Consider declaring as nullable.
        public TextureShader(Texture? texture, bool convertToLinear = false) {
            Texture = texture;
            ConvertToLinear = convertToLinear;
        }

        [ConstantBufferView(0)]
        public Matrix4x4 WorldViewProj { get; set; }

        [IgnoreShaderMember]
        public Texture? Texture { get; set; }

        [IgnoreShaderMember]
        public bool ConvertToLinear { get; set; }

        //The type of this property specifies the appropriate attribute, no need to add another
        //[ShaderMember]
        public SamplerState Sampler { get; set; }

        //The type of this property specifies the appropriate attribute, no need to add another
        //[ShaderMember]
        public Texture2D ColorTexture { get; set; }
#pragma warning restore CS8618 // Non-nullable field must contain a non-null value when exiting constructor. Consider declaring as nullable.

        [Shader("vertex")]
        [ShaderMethod]
        public PSTVInput VSMain([PositionSemantic] Vector3 position, [TextureCoordinateSemantic] Vector2 uv) {
            return new PSTVInput {
                Position = Vector4.Transform(new Vector4(position, 1.0f), WorldViewProj),
                UV = uv,
            };
        }

        [Shader("pixel")]
        [ShaderMethod]
        [return: SystemTargetSemantic]
        public Vector4 PSMain(PSTVInput input) {
            return ColorTexture.Sample(Sampler, input.UV);
        }

        public void Accept(ShaderGeneratorContext context) {
            if (Texture is null)
                throw new InvalidOperationException($"Cannot use this shader without first constructing with or assigning a {nameof(Texture)}");

            context.RootParameters.Add(new RootParameter1(new RootDescriptorTable1(new DescriptorRange1(DescriptorRangeType.ConstantBufferView, 1, context.ConstantBufferViewRegisterCount++, 0, -1, DescriptorRangeFlags.DataStatic)), ShaderVisibility.All));

            ColorTexture = new Texture2D(ShaderResourceView.FromTexture2D(Texture, ConvertToLinear ? ToSrgb(Texture.Format) : Texture.Format));
            context.ShaderResourceViews.Add(ColorTexture);
        }

        static Format ToSrgb(Format format) => format switch
        {
            Format.R8G8B8A8_UNorm => Format.R8G8B8A8_UNorm_SRgb,
            Format.B8G8R8A8_UNorm => Format.B8G8R8A8_UNorm_SRgb,
            Format.B8G8R8X8_UNorm => Format.B8G8R8X8_UNorm_SRgb,
            _ => format
        };
    }

    public struct PSCVInput {
        [SystemPositionSemantic]
        public Vector4 Position;
        [ColorSemantic(0)]
        public Vector4 Colour;
        //[SystemInstanceIdSemantic]
        //public uint InstanceId;
    }

    public struct PSTVInput {
        [SystemPositionSemantic]
        public Vector4 Position;
        [TextureCoordinateSemantic(0)]
        public Vector2 UV;
    }

    public static class StringExtensions {
        public ref struct LineSplitEnumerator {
            private ReadOnlySpan<char> mStr;

            public LineSplitEnumerator(ReadOnlySpan<char> str) {
                mStr = str;
                Current = default;
            }

            // Needed to be compatible with the foreach operator
            public LineSplitEnumerator GetEnumerator() => this;

            public bool MoveNext() {
                var span = mStr;
                if (span.Length == 0) // Reach the end of the string
                    return false;

                var index = span.IndexOfAny('\r', '\n');
                if (index == -1) // The string is composed of only one line
                {
                    mStr = ReadOnlySpan<char>.Empty; // The remaining string is an empty string
                    Current = new LineSplitEntry(span, ReadOnlySpan<char>.Empty);
                    return true;
                }

                if (index < span.Length - 1 && span[index] == '\r') {
                    // Try to consume the '\n' associated to the '\r'
                    var next = span[index + 1];
                    if (next == '\n') {
                        Current = new LineSplitEntry(span.Slice(0, index), span.Slice(index, 2));
                        mStr = span.Slice(index + 2);
                        return true;
                    }
                }

                Current = new LineSplitEntry(span.Slice(0, index), span.Slice(index, 1));
                mStr = span.Slice(index + 1);
                return true;
            }

            public LineSplitEntry Current { get; private set; }
        }

        public readonly ref struct LineSplitEntry {
            public LineSplitEntry(ReadOnlySpan<char> line, ReadOnlySpan<char> separator) {
                Line = line;
                Separator = separator;
            }

            public ReadOnlySpan<char> Line { get; }
            public ReadOnlySpan<char> Separator { get; }

            // This method allow to deconstruct the type, so you can write any of the following code
            // foreach (var entry in str.SplitLines()) { _ = entry.Line; }
            // foreach (var (line, endOfLine) in str.SplitLines()) { _ = line; }
            // https://docs.microsoft.com/en-us/dotnet/csharp/deconstruct#deconstructing-user-defined-types
            public void Deconstruct(out ReadOnlySpan<char> line, out ReadOnlySpan<char> separator) {
                line = Line;
                separator = Separator;
            }

            // This method allow to implicitly cast the type into a ReadOnlySpan<char>, so you can write the following code
            // foreach (ReadOnlySpan<char> entry in str.SplitLines())
            public static implicit operator ReadOnlySpan<char>(LineSplitEntry entry) => entry.Line;
        }
    }
}