using System;
using Vortice.Direct3D12;

namespace D3D12HelloWorld {
    static class MemoryExtensions {
        public static unsafe void CopyTo<T>(this Span<T> source, IntPtr destination) where T : unmanaged {
            source.CopyTo(new Span<T>(destination.ToPointer(), source.Length));
        }

        public unsafe static void CopyTo<T>(this IntPtr source, Span<T> destination) where T : unmanaged {
            new Span<T>(source.ToPointer(), destination.Length).CopyTo(destination);
        }

        public static ulong GetRequiredIntermediateSize(this ID3D12Device device, ID3D12Resource destinationResource, int firstSubresource, int numSubresources) {
            device.GetCopyableFootprints(destinationResource.Description, firstSubresource, numSubresources, 0, null, null, null, out ulong requiredSize);
            return requiredSize;
        }

        public static ulong UpdateSubresource(this ID3D12Device device, ID3D12GraphicsCommandList cmdList, ID3D12Resource destResource, ID3D12Resource intermediateResource,
                                              ulong intermediateOffset, int firstSubresource, ReadOnlySpan<byte> data) {
            int numSubresource = 1;
            var layouts = new PlacedSubresourceFootPrint[numSubresource];
            var numRows = new int[numSubresource];
            var rowSizesInBytes = new ulong[numSubresource];
            device.GetCopyableFootprints(destResource.Description, firstSubresource, numSubresource, intermediateOffset, layouts, numRows, rowSizesInBytes, out ulong requiredSize);

            var intermediateDesc = intermediateResource.Description;
            var destDesc = destResource.Description;

            if (intermediateDesc.Dimension != ResourceDimension.Buffer
                || intermediateDesc.Width < (ulong)data.Length
                || data.Length == 0
                || (destDesc.Dimension == ResourceDimension.Buffer && firstSubresource != 0
                    || numSubresource != 1))
                return 0;

            intermediateResource.SetData(data);

            if (destDesc.Dimension == ResourceDimension.Buffer) {
                cmdList.CopyBufferRegion(destResource, 0, intermediateResource, layouts[0].Offset, (ulong)layouts[0].Footprint.Width);
            }
            else {
                for (int idx = 0; idx < numSubresource; idx++) {
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
        public static ulong UpdateSubResources(this ID3D12Device device, ID3D12GraphicsCommandList cmdList, ID3D12Resource destResource, ID3D12Resource intermediateResource,
                                               ulong intermediateOffset, int firstSubresource, SubresourceInfo[] data) {
            int numSubresource = data.Length;
            var layouts = new PlacedSubresourceFootPrint[numSubresource];
            var numRows = new int[numSubresource];
            var rowSizesInBytes = new ulong[numSubresource];

            device.GetCopyableFootprints(destResource.Description, firstSubresource, numSubresource, intermediateOffset, layouts, numRows, rowSizesInBytes, out var requiredSize);
            ulong result = UpdateSubResources(cmdList, destResource, intermediateResource, firstSubresource, numSubresource, requiredSize, layouts, rowSizesInBytes, data);
            return result;
        }

        private static ulong UpdateSubResources(ID3D12GraphicsCommandList cmdList, ID3D12Resource destResource, ID3D12Resource intermediateResource,
                                                int firstSubresource, int numSubresource, ulong requiredSize, PlacedSubresourceFootPrint[] layouts,
                                                ulong[] rowSizesInBytes, SubresourceInfo[] data) {
            var intermediateDesc = intermediateResource.Description;
            var destDesc = destResource.Description;
            var dataPtr = System.Runtime.InteropServices.Marshal.UnsafeAddrOfPinnedArrayElement(data, 0);

            if (intermediateDesc.Dimension != ResourceDimension.Buffer
                || intermediateDesc.Width < requiredSize + layouts[0].Offset
                || requiredSize == 0
                || (destDesc.Dimension == ResourceDimension.Buffer
                    && firstSubresource != 0
                    || numSubresource != 1)) {
                return 0;
            }

            unsafe {
                if (intermediateResource.Map(0, null).Failure) {
                    return 0;
                }
            }

            for (int idx = 0; idx < numSubresource; idx++) {
                if (rowSizesInBytes[idx] == 0) { return 0; }
                intermediateResource.WriteToSubresource(idx, null, new IntPtr((long)data[idx].Offset), data[idx].RowPitch, data[idx].DepthPitch);
            }

            if (destDesc.Dimension == ResourceDimension.Buffer) {
                cmdList.CopyBufferRegion(destResource, 0, intermediateResource, layouts[0].Offset, (ulong)layouts[0].Footprint.Width);
            }
            else {
                for (int idx = 0; idx < numSubresource; idx++) {
                    var dest = new TextureCopyLocation(destResource, idx + firstSubresource);
                    var src = new TextureCopyLocation(intermediateResource, layouts[idx]);
                    cmdList.CopyTextureRegion(dest, 0, 0, 0, src, null);
                }
            }

            return requiredSize;
        }

    }
}