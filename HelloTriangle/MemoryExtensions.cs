using System;
using Vortice.Direct3D12;

namespace D3D12HelloWorld
{
    static class MemoryExtensions
    {
        public static unsafe void CopyTo<T>(this Span<T> source, IntPtr destination) where T : unmanaged
        {
            source.CopyTo(new Span<T>(destination.ToPointer(), source.Length));
        }

        public static unsafe ShaderBytecode AsShaderBytecode(this Vortice.Dxc.IDxcBlob source)
        {
            return new ShaderBytecode(new IntPtr(source.GetBufferPointer()), source.GetBufferSize());
        }

        public static long GetRequiredIntermediateSize(this ID3D12Device device, ID3D12Resource destinationResource, int firstSubresource, int numSubresources)
        {
            var desc = destinationResource.Description;

            device.GetCopyableFootprints(ref desc, firstSubresource, numSubresources, 0, null, null, null, out long requiredSize);
            return requiredSize;
        }

        public static long UpdateSubresource(this ID3D12Device device, ID3D12GraphicsCommandList cmdList, ID3D12Resource destResource, ID3D12Resource intermediateResource,
                                             int intermediateOffset, int firstSubresource, Span<byte> data)
        {
            int numSubresource = 1;
            var layouts = new PlacedSubresourceFootPrint[numSubresource];
            var numRows = new int[numSubresource];
            var rowSizesInBytes = new long[numSubresource];
            var desc = destResource.Description;
            device.GetCopyableFootprints(ref desc, firstSubresource, numSubresource, intermediateOffset, layouts, numRows, rowSizesInBytes, out var requiredSize);

            var intermediateDesc = intermediateResource.Description;
            var destDesc = destResource.Description;

            if (intermediateDesc.Dimension != ResourceDimension.Buffer
                || intermediateDesc.Width < data.Length
                || data.Length == 0
                || (destDesc.Dimension == ResourceDimension.Buffer && firstSubresource != 0
                    || numSubresource != 1))
                return 0;

            var ptr = intermediateResource.Map(0, new Vortice.Direct3D12.Range(0, 0));  //Do not intend to read this resource from the CPU (whereas null indicates we might read the entire resource)
            if (ptr == IntPtr.Zero)
                return 0;

            for (int idx = 0; idx < numSubresource; idx++)
            {
                if (rowSizesInBytes[idx] == 0)
                    return 0;

                //Throws SharpGenException SharpGen.Runtime.SharpGenException: HRESULT: [0x80070057], Module:[General], ApiCode:[E_INVALIDARG/ Invalid Arguments], Message: The parameter is incorrect.
                //According to https://github.com/Microsoft/DirectX-Graphics-Samples/issues/320 can't write directly to textures without using a custom heap pool
                //But then... what was the C++ code doing?  And this is a buffer dimension, not a texture... so wtf?  Maybe totally unrelated.
                //https://github.com/Microsoft/DirectX-Graphics-Samples/issues/110
                //intermediateResource.WriteToSubresource(idx, data, layouts[idx].Footprint.RowPitch, layouts[idx].Footprint.RowPitch * layouts[idx].Footprint.Height);
                ////https://docs.microsoft.com/en-us/windows/win32/api/d3d12/nf-d3d12-id3d12resource-writetosubresource

                //This works, as does the MemcpySubresource stuff below, but I don't understand why I can't get intermediateResource.WriteToSubresource to work
                data.CopyTo(ptr);
                /* All of this can be replaced by the above one-liner
                //MemcpySubresource https://github.com/microsoft/DirectX-Graphics-Samples/blob/master/Samples/Desktop/D3D12HelloWorld/src/HelloTexture/d3dx12.h
                var destRowPitch = layouts[idx].Footprint.RowPitch;
                var destSlicePitch = layouts[idx].Footprint.RowPitch * numRows[idx];
                var srcRowPitch = layouts[idx].Footprint.RowPitch;
                var srcSlicePitch = layouts[idx].Footprint.RowPitch * layouts[idx].Footprint.Height;
                var numSlices = layouts[idx].Footprint.Depth;
                for (var z = 0; z < numSlices; ++z)
                {
                    IntPtr pDestSlice = ptr + destSlicePitch * z;
                    var srcSlice = data.Slice(srcSlicePitch * z, srcSlicePitch);

                    for (var y = 0; y < numRows[idx]; ++y)
                    {
                        var srcRow = srcSlice.Slice(srcRowPitch * y, (int)rowSizesInBytes[idx]);
                        srcRow.CopyTo(pDestSlice + destRowPitch * y);
                    }
                }*/
            }

            intermediateResource.Unmap(0);

            if (destDesc.Dimension == ResourceDimension.Buffer)
            {
                cmdList.CopyBufferRegion(destResource, 0, intermediateResource, layouts[0].Offset, layouts[0].Footprint.Width);
            }
            else
            {
                for (int idx = 0; idx < numSubresource; idx++)
                {
                    var dest = new TextureCopyLocation(destResource, idx + firstSubresource);
                    var src = new TextureCopyLocation(intermediateResource, layouts[idx]);
                    cmdList.CopyTextureRegion(dest, 0, 0, 0, src, null);
                }
            }

            return requiredSize;

            /*DX12GE Texture.Create2D<T>(GD d, Span<T> data, width, height, format... etc)
              Creates resource with default heap properties, and no resource flags - so not exactly the same as mTexture, or textureUploadHeap...
              Dimension is Texture2D, same as textureDesc
              Then it calls SetData<T>(Span<T>, int offset = 0) which for Texture2D dimensions, creates an "upload resource" with HeapProperties(CpuPageProperty.WriteBack, MemoryPool.L0), no flags, ResourceState CopyDestination
              Then it calls WriteToSubresource on this... creates a copy command list, calls copyCommandList.CopyResource(uploadBuffer, this) being the texture resource, then it calls flush on that commandlist
              The C++ code didn't take this Texture2D-specific pathway, treating it more like a generic buffer...  For compatibility with the HLSL shader, perhaps ought to copy that...
              So the DX12GE equivalent would be GraphicsResource.CreateBuffer<T>(GD d, Span<T> d, resourceFFlags, HeapType = default)
              That would CreateCommittedResource with Dimension Buffer by default, on the default heap.  If we overrode that to UploadHeap then it would set the ResourceState to GenericRead, but otherwise it is 0, Common
              Then it calls SetData and this time because of the Buffer dimension, and if on the default heap (yes) it would create another buffer on the upload heap, just like textureUploadHeap
            //Then it create s a new copy commandlist, calls CopyBufferRegion (as opposed to CopyResource like the Texture case did), then flush...
            So only difference with C++ and the DX12GE buffer version is the ResourceState of the mTexture - they set CopyDestination DX12GE uses Common (0)...
            And then the copy orchestration, C++ sets ResourceBarrier on CopyDestination (corresponding to what they set the initial state to) until PixelShaderResource..
            //It does this on its original command list instead of specifically creating a new copy command list...... later it calls close and ExecuteCommandLists
            //which should accomplish the same as Flush..
            */
        }

        /// <summary>
        /// https://github.com/Mayhem50/SharpMiniEngine/blob/b955ec87786278af6bc6281ea84b5ce968a3ab21/Assemblies/Core/DirectX12.cs
        /// </summary>
        public static long UpdateSubResources(this ID3D12Device device, ID3D12GraphicsCommandList cmdList, ID3D12Resource destResource, ID3D12Resource intermediateResource,
                                              long intermediateOffset, int firstSubresource, SubresourceInfo[] datas)
        {
            int numSubresource = datas.Length;
            var layouts = new PlacedSubresourceFootPrint[numSubresource];
            var numRows = new int[numSubresource];
            var rowSizesInBytes = new long[numSubresource];

            var desc = destResource.Description;
            device.GetCopyableFootprints(ref desc, firstSubresource, numSubresource, intermediateOffset, layouts, numRows, rowSizesInBytes, out var requiredSize);
            long result = UpdateSubResources(cmdList, destResource, intermediateResource, firstSubresource, numSubresource, requiredSize, layouts, rowSizesInBytes, datas);
            return result;
        }

        private static long UpdateSubResources(ID3D12GraphicsCommandList cmdList, ID3D12Resource destResource, ID3D12Resource intermediateResource,
                                               int firstSubresource, int numSubresource, long requiredSize, PlacedSubresourceFootPrint[] layouts,
                                               long[] rowSizesInBytes, SubresourceInfo[] data)
        {
            var intermediateDesc = intermediateResource.Description;
            var destDesc = destResource.Description;
            var dataPtr = System.Runtime.InteropServices.Marshal.UnsafeAddrOfPinnedArrayElement(data, 0);

            if (intermediateDesc.Dimension != ResourceDimension.Buffer
                || intermediateDesc.Width < requiredSize + layouts[0].Offset
                || requiredSize == 0
                || (destDesc.Dimension == ResourceDimension.Buffer
                    && firstSubresource != 0
                    || numSubresource != 1))
            {
                return 0;
            }

            var ptr = intermediateResource.Map(0, null);
            if (ptr == IntPtr.Zero)
            {
                return 0;
            }

            for (int idx = 0; idx < numSubresource; idx++)
            {
                if (rowSizesInBytes[idx] == 0) { return 0; }
                intermediateResource.WriteToSubresource(idx, null, new IntPtr(data[idx].Offset), data[idx].RowPitch, data[idx].DepthPitch);
            }

            if (destDesc.Dimension == ResourceDimension.Buffer)
            {
                cmdList.CopyBufferRegion(destResource, 0, intermediateResource, layouts[0].Offset, layouts[0].Footprint.Width);
            }
            else
            {
                for (int idx = 0; idx < numSubresource; idx++)
                {
                    var dest = new TextureCopyLocation(destResource, idx + firstSubresource);
                    var src = new TextureCopyLocation(intermediateResource, layouts[idx]);
                    cmdList.CopyTextureRegion(dest, 0, 0, 0, src, null);
                }
            }

            return requiredSize;
        }

    }
}