using System;
using System.Runtime.CompilerServices;
using Vortice.Direct3D12;
using Vortice.DXGI;

namespace wired.Graphics {
    public abstract class ResourceView {
        protected ResourceView(GraphicsResource resource, CpuDescriptorHandle descriptor) {
            Resource = resource;
            CpuDescriptorHandle = descriptor;
        }

        public GraphicsResource Resource { get; }

        internal CpuDescriptorHandle CpuDescriptorHandle { get; }
    }

    public class ConstantBufferView : ResourceView {
        public ConstantBufferView(ConstantBufferView constantBufferView) : base(constantBufferView.Resource, constantBufferView.CpuDescriptorHandle) {
        }

        public ConstantBufferView(GraphicsResource resource)
            : this(resource, 0, resource.SizeInBytes, resource.GraphicsDevice.ShaderResourceViewAllocator) {
        }

        internal ConstantBufferView(GraphicsResource resource, ulong offset, ulong sizeInBytes, DescriptorAllocator shaderResourceViewAllocator)
            : base(resource, CreateConstantBufferView(resource, offset, sizeInBytes, shaderResourceViewAllocator)) {
        }

        public static ConstantBufferView FromOffset<TBuffer>(GraphicsResource resource, ulong offset, DescriptorAllocator shaderResourceViewAllocator)
            => new ConstantBufferView(resource, offset, (ulong)Unsafe.SizeOf<TBuffer>(), shaderResourceViewAllocator);

        private static CpuDescriptorHandle CreateConstantBufferView(GraphicsResource resource, ulong offset, ulong sizeInBytes, DescriptorAllocator shaderResourceViewAllocator) {
            CpuDescriptorHandle cpuHandle = shaderResourceViewAllocator.Allocate(1);

            int constantBufferSize = ((int)sizeInBytes + 255) & ~255;

            ConstantBufferViewDescription cbvDescription = new ConstantBufferViewDescription {
                BufferLocation = resource.NativeResource.GPUVirtualAddress + offset,
                SizeInBytes = constantBufferSize
            };

            resource.GraphicsDevice.NativeDevice.CreateConstantBufferView(cbvDescription, cpuHandle);

            return cpuHandle;
        }
    }

    public sealed class Texture : GraphicsResource {
        public Texture(GraphicsDevice device, ResourceDescription description, HeapType heapType, string? name = null)
            : base(device, description, heapType) {
            if (description.Dimension < ResourceDimension.Texture1D) {
                throw new ArgumentException($"Cannot create a Texture instance for a '{description.Dimension}' resource.", nameof(description));
            }
            if (!string.IsNullOrWhiteSpace(name)) {
                NativeResource.Name = name;
            }
        }

        internal Texture(GraphicsDevice device, ID3D12Resource resource, string? name = null)
            : base(device, resource) {
            if (!string.IsNullOrWhiteSpace(name)) {
                NativeResource.Name = name;
            }
        }

        //public static Texture Create2D(GraphicsDevice device, int width, int height, Format format, ResourceFlags textureFlags = ResourceFlags.None,
        //                               ushort mipLevels = 1, ushort arraySize = 1, int sampleCount = 1, int sampleQuality = 0, HeapType heapType = HeapType.Default) {
        //    var description = ResourceDescription.Texture2D(format, (uint)width, (uint)height, arraySize, mipLevels, sampleCount, sampleQuality, textureFlags);
        //    return new Texture(device, description, heapType);
        //}
    }

    public class DepthStencilView : ResourceView {
        public DepthStencilView(GraphicsResource resource, DescriptorAllocator dsvAllocator, DepthStencilViewDescription? description = null)
            : base(resource, CreateDepthStencilView(resource, dsvAllocator, description)) {
        }

        public static DepthStencilView FromTexture2D(GraphicsResource resource, DescriptorAllocator dsvAllocator) {
            return new DepthStencilView(resource, dsvAllocator, new DepthStencilViewDescription {
                Format = resource.Description.Format,
                ViewDimension = DepthStencilViewDimension.Texture2D,
                Flags = DepthStencilViewFlags.None,
            });
        }

        private static CpuDescriptorHandle CreateDepthStencilView(GraphicsResource resource, DescriptorAllocator dsvAllocator, DepthStencilViewDescription? description) {
            CpuDescriptorHandle handle = dsvAllocator.Allocate(1);
            resource.GraphicsDevice.NativeDevice.CreateDepthStencilView(resource.NativeResource, description, handle);
            return handle;
        }
    }

    public class RenderTargetView : ResourceView {
        public RenderTargetView(GraphicsResource resource, DescriptorAllocator rtvAllocator)
            : this(resource, rtvAllocator, null) {
        }

        internal RenderTargetView(GraphicsResource resource, DescriptorAllocator rtvAllocator, RenderTargetViewDescription? description)
            : base(resource, CreateRenderTargetView(resource, rtvAllocator, description)) {
            Description = description;
        }

        internal RenderTargetViewDescription? Description { get; }

        public static RenderTargetView FromTexture2D(GraphicsResource resource, DescriptorAllocator renderTargetViewAllocator) {
            return new RenderTargetView(resource, renderTargetViewAllocator, new RenderTargetViewDescription {
                ViewDimension = RenderTargetViewDimension.Texture2D,
                Format = resource.Description.Format,
            });
        }

        private static CpuDescriptorHandle CreateRenderTargetView(GraphicsResource resource, DescriptorAllocator rtvAllocator, RenderTargetViewDescription? description) {
            CpuDescriptorHandle handle = rtvAllocator.Allocate(1);
            resource.GraphicsDevice.NativeDevice.CreateRenderTargetView(resource.NativeResource, description, handle);
            return handle;
        }
    }

    internal class D3DXUtilities {
        public const int ComponentMappingMask = 0x7;

        public const int ComponentMappingShift = 3;

        public const int ComponentMappingAlwaysSetBitAvoidingZeromemMistakes = 1 << (ComponentMappingShift * 4);

        public static int ComponentMapping(int src0, int src1, int src2, int src3) {
            return ((src0) & ComponentMappingMask)
                | (((src1) & ComponentMappingMask) << ComponentMappingShift)
                | (((src2) & ComponentMappingMask) << (ComponentMappingShift * 2))
                | (((src3) & ComponentMappingMask) << (ComponentMappingShift * 3))
                | ComponentMappingAlwaysSetBitAvoidingZeromemMistakes;
        }

        public static int DefaultComponentMapping() {
            return ComponentMapping(0, 1, 2, 3);
        }

        public static int ComponentMapping(int ComponentToExtract, int Mapping) {
            return (Mapping >> (ComponentMappingShift * ComponentToExtract)) & ComponentMappingMask;
        }
    }


    public class ShaderResourceView : ResourceView {
        public ShaderResourceView(GraphicsResource resource) : this(resource, null) {
        }

        public ShaderResourceView(ShaderResourceView shaderResourceView) : base(shaderResourceView.Resource, shaderResourceView.CpuDescriptorHandle) {
            Description = shaderResourceView.Description;
        }

        internal ShaderResourceView(GraphicsResource resource, ShaderResourceViewDescription? description)
            : base(resource, CreateShaderResourceView(resource, description)) {
            Description = description;
        }

        internal ShaderResourceViewDescription? Description { get; }

        public static ShaderResourceView FromBuffer<T>(GraphicsResource resource, ulong firstElement = 0, int elementCount = 0) where T : unmanaged {
            return FromBuffer(resource, firstElement, elementCount == 0 ? (int)resource.Width / Unsafe.SizeOf<T>() : elementCount, Unsafe.SizeOf<T>());
        }

        public static ShaderResourceView FromBuffer(GraphicsResource resource, ulong firstElement, int elementCount, int structureByteStride) {
            return new ShaderResourceView(resource, new ShaderResourceViewDescription {
                Shader4ComponentMapping = D3DXUtilities.DefaultComponentMapping(),
                ViewDimension = ShaderResourceViewDimension.Buffer,
                Buffer = {
                    FirstElement = firstElement,
                    NumElements = elementCount,
                    StructureByteStride = structureByteStride
                }
            });
        }

        public static ShaderResourceView FromTexture2D(GraphicsResource resource, Format format) {
            return new ShaderResourceView(resource, new ShaderResourceViewDescription {
                Shader4ComponentMapping = D3DXUtilities.DefaultComponentMapping(),
                ViewDimension = ShaderResourceViewDimension.Texture2D,
                Format = format,
                Texture2D =
                {
                    MipLevels = resource.Description.MipLevels
                }
            });
        }

        static CpuDescriptorHandle CreateShaderResourceView(GraphicsResource resource, ShaderResourceViewDescription? description) {
            CpuDescriptorHandle cpuHandle = resource.GraphicsDevice.ShaderResourceViewAllocator.Allocate(1);
            resource.GraphicsDevice.NativeDevice.CreateShaderResourceView(resource.NativeResource, description, cpuHandle);

            return cpuHandle;
        }
    }

    public class UnorderedAccessView : ResourceView {
        internal UnorderedAccessViewDescription? Description { get; }

        public UnorderedAccessView(GraphicsResource resource)
            : this(resource, null) {
        }

        public UnorderedAccessView(UnorderedAccessView unorderedAccessView)
            : base(unorderedAccessView.Resource, unorderedAccessView.CpuDescriptorHandle) {
            Description = unorderedAccessView.Description;
        }

        internal UnorderedAccessView(GraphicsResource resource, UnorderedAccessViewDescription? description)
            : base(resource, CreateUnorderedAccessView(resource, description)) {
            Description = description;
        }

        static CpuDescriptorHandle CreateUnorderedAccessView(GraphicsResource resource, UnorderedAccessViewDescription? description) {
            CpuDescriptorHandle cpuHandle = resource.GraphicsDevice.ShaderResourceViewAllocator.Allocate(1);
            resource.GraphicsDevice.NativeDevice.CreateUnorderedAccessView(resource.NativeResource, null, description, cpuHandle);
            return cpuHandle;
        }
    }

}