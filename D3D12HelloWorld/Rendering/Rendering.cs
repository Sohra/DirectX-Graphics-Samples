using DirectX12GameEngine.Shaders;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using System.Windows.Media.Media3D;
using Vortice.Direct3D12;
using Vortice.DXGI;

namespace D3D12HelloWorld.Rendering {
    [ShaderType("Texture2D")]
    [ShaderResourceView]
    public class Texture2D<T> : ShaderResourceView where T : unmanaged {
        public Texture2D(ShaderResourceView shaderResourceView) : base(shaderResourceView) {
        }

        public T Sample(Sampler sampler, Vector2 textureCoordinate) => throw new NotImplementedException("This is intentionally not implemented.");
    }

    [ShaderType("Texture2D")]
    [ShaderResourceView]
    public class Texture2D : Texture2D<Vector4> {
        public Texture2D(ShaderResourceView shaderResourceView) : base(shaderResourceView) {
        }
    }

    public sealed class DescriptorAllocator : IDisposable {
        private const int DescriptorsPerHeap = 4096;

        readonly object mAllocatorLock = new object();

        readonly DescriptorHeapDescription mDescription;

        public int CurrentDescriptorCount { get; private set; }
        //public int DescriptorCapacity { get; private set; }
        public int DescriptorHandleIncrementSize { get; }
        //public DescriptorHeapFlags Flags => mDescription.Flags;
        internal ID3D12DescriptorHeap DescriptorHeap { get; }

        public DescriptorAllocator(ID3D12Device device, DescriptorHeapType descriptorHeapType, int descriptorCount = DescriptorsPerHeap, DescriptorHeapFlags descriptorHeapFlags = DescriptorHeapFlags.None) {
            if (descriptorCount < 1 || descriptorCount > DescriptorsPerHeap) {
                throw new ArgumentOutOfRangeException(nameof(descriptorCount), $"Descriptor count must be between 1 and {DescriptorsPerHeap}.");
            }

            //Type = descriptorHeapType;
            //Flags = descriptorHeapFlags;

            DescriptorHandleIncrementSize = device.GetDescriptorHandleIncrementSize(descriptorHeapType);
            mDescription = new DescriptorHeapDescription(descriptorHeapType, descriptorCount, descriptorHeapFlags);
            DescriptorHeap = device.CreateDescriptorHeap(mDescription);
            //DescriptorCapacity = descriptorCount;
        }

        public CpuDescriptorHandle Allocate(int count) {
            lock (mAllocatorLock) {
                if (count < 1 || count > mDescription.DescriptorCount) {
                    throw new ArgumentOutOfRangeException(nameof(count), "Count must be between 1 and the total descriptor count.");
                }

                if (CurrentDescriptorCount + count > mDescription.DescriptorCount) {
                    Reset();
                }

                CpuDescriptorHandle result = DescriptorHeap.GetCPUDescriptorHandleForHeapStart().Offset(CurrentDescriptorCount, DescriptorHandleIncrementSize);
                CurrentDescriptorCount += count;
                return result;
            }
        }

        public GpuDescriptorHandle GetGpuDescriptorHandle(CpuDescriptorHandle descriptor) {
            if (!mDescription.Flags.HasFlag(DescriptorHeapFlags.ShaderVisible)) {
                throw new InvalidOperationException();
            }

            var offset = (int)(descriptor - DescriptorHeap.GetCPUDescriptorHandleForHeapStart());
            return new GpuDescriptorHandle(DescriptorHeap.GetGPUDescriptorHandleForHeapStart(), offset);
        }

        public void Dispose() {
            DescriptorHeap.Dispose();
        }

        public void Reset() {
            CurrentDescriptorCount = 0;
        }
    }

    public sealed class DescriptorSet {
        readonly ID3D12Device mDevice;
        public DescriptorHeapType DescriptorHeapType { get; }
        public int CurrentDescriptorCount { get; private set; }
        public int DescriptorCapacity { get; private set; }
        public DescriptorAllocator DescriptorAllocator { get; }
        public CpuDescriptorHandle StartCpuDescriptorHandle { get; }

        public DescriptorSet(GraphicsDevice device, IEnumerable<Sampler> samplers)
            : this(device, samplers.Count(), DescriptorHeapType.Sampler) {
            AddSamplers(samplers);
        }

        public DescriptorSet(GraphicsDevice device, int descriptorCount, DescriptorHeapType descriptorHeapType = DescriptorHeapType.ConstantBufferViewShaderResourceViewUnorderedAccessView) {
            if (descriptorCount < 1) {
                throw new ArgumentOutOfRangeException("descriptorCount");
            }

            mDevice = device.NativeDevice;
            DescriptorHeapType = descriptorHeapType;
            DescriptorCapacity = descriptorCount;
            DescriptorAllocator = GetDescriptorAllocator(device);
            StartCpuDescriptorHandle = DescriptorAllocator.Allocate(DescriptorCapacity);
        }

        public void AddResourceViews(IEnumerable<ResourceView> resources) {
            AddDescriptors(resources.Select((ResourceView r) => r.CpuDescriptorHandle));
        }

        public void AddResourceViews(params ResourceView[] resources) {
            AddResourceViews(resources.AsEnumerable());
        }

        public void AddSamplers(IEnumerable<Sampler> samplers) {
            AddDescriptors(samplers.Select((Sampler r) => r.CpuDescriptorHandle));
        }

        public void AddSamplers(params Sampler[] samplers) {
            AddSamplers(samplers);
        }

        private void AddDescriptors(IEnumerable<CpuDescriptorHandle> descriptors) {
            if (descriptors.Any()) {
                CpuDescriptorHandle[] array = descriptors.ToArray();
                if (CurrentDescriptorCount + array.Length > DescriptorCapacity) {
                    throw new InvalidOperationException();
                }

                int[] array2 = new int[array.Length];
                for (int i = 0; i < array2.Length; i++) {
                    array2[i] = 1;
                }

                var value = new CpuDescriptorHandle(StartCpuDescriptorHandle, CurrentDescriptorCount, DescriptorAllocator.DescriptorHandleIncrementSize);
                mDevice.CopyDescriptors(1, new CpuDescriptorHandle[1] { value, }, new int[1] { array.Length }, array.Length, array, array2, DescriptorHeapType);
                CurrentDescriptorCount += array.Length;
            }
        }

        DescriptorAllocator GetDescriptorAllocator(GraphicsDevice device) {
            DescriptorHeapType descriptorHeapType = DescriptorHeapType;

            DescriptorAllocator result = descriptorHeapType switch {
                DescriptorHeapType.ConstantBufferViewShaderResourceViewUnorderedAccessView => device.ShaderResourceViewAllocator,
                DescriptorHeapType.Sampler => device.SamplerAllocator,
                _ => throw new NotSupportedException("This descriptor heap type is not supported."),
            };

            return result;
        }
    }

    public abstract class ResourceView {
        protected ResourceView(GraphicsResource resource, CpuDescriptorHandle descriptor) {
            Resource = resource;
            CpuDescriptorHandle = descriptor;
        }

        public GraphicsResource Resource { get; }

        internal CpuDescriptorHandle CpuDescriptorHandle { get; }
    }

    public class ConstantBufferView : ResourceView {
        public ConstantBufferView(GraphicsResource resource) : base(resource, CreateConstantBufferView(resource)) {
        }

        public ConstantBufferView(ConstantBufferView constantBufferView) : base(constantBufferView.Resource, constantBufferView.CpuDescriptorHandle) {
        }

        private static CpuDescriptorHandle CreateConstantBufferView(GraphicsResource resource) {
            CpuDescriptorHandle cpuHandle = resource.GraphicsDevice.ShaderResourceViewAllocator.Allocate(1);

            int constantBufferSize = ((int)resource.SizeInBytes + 255) & ~255;

            ConstantBufferViewDescription cbvDescription = new ConstantBufferViewDescription {
                BufferLocation = resource.NativeResource.GPUVirtualAddress,
                SizeInBytes = constantBufferSize
            };

            resource.GraphicsDevice.NativeDevice.CreateConstantBufferView(cbvDescription, cpuHandle);

            return cpuHandle;
        }
    }

    public sealed class Texture : GraphicsResource {
        public Texture(GraphicsDevice device, ResourceDescription description, HeapType heapType)
            : base(device, description, heapType) {
            if (description.Dimension < ResourceDimension.Texture1D) {
                throw new ArgumentException();
            }
        }

        internal Texture(GraphicsDevice device, ID3D12Resource resource)
            : base(device, resource) {
        }
    }

    public class DepthStencilView : ResourceView {
        public DepthStencilView(GraphicsResource resource)
            : base(resource, CreateDepthStencilView(resource)) {
        }

        private static CpuDescriptorHandle CreateDepthStencilView(GraphicsResource resource) {
            CpuDescriptorHandle handle = resource.GraphicsDevice.DepthStencilViewAllocator.Allocate(1);
            resource.GraphicsDevice.NativeDevice.CreateDepthStencilView(resource.NativeResource, null, handle);
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

    public class MaterialPass {
        public int PassIndex { get; set; }

        public PipelineState? PipelineState { get; set; }

        public DescriptorSet? ShaderResourceViewDescriptorSet { get; set; }

        public DescriptorSet? SamplerDescriptorSet { get; set; }
    }

    //public class Material {
    //    public MaterialDescriptor? Descriptor { get; set; }

    //    public IList<MaterialPass> Passes { get; } = new List<MaterialPass>();

    //    public static Task<Material> CreateAsync(GraphicsDevice device, MaterialDescriptor descriptor, IContentManager contentManager) {
    //        Material material = new Material { Descriptor = descriptor };

    //        MaterialGeneratorContext context = new MaterialGeneratorContext(device, material, contentManager);
    //        return MaterialGenerator.GenerateAsync(descriptor, context);
    //    }
    //}

    public sealed class MeshDraw {
        public IndexBufferView? IndexBufferView { get; set; }

        public VertexBufferView[]? VertexBufferViews { get; set; }
    }

    public sealed class Mesh {
        public Mesh(MeshDraw meshDraw) {
            MeshDraw = meshDraw;
        }

        public int MaterialIndex { get; set; }

        public MeshDraw MeshDraw { get; set; }

        public Matrix4x4 WorldMatrix { get; set; } = Matrix4x4.Identity;
    }

    public sealed class Model {
        //public IList<Material> Materials { get; } = new List<Material>();
        public IList<MaterialPass> Materials { get; } = new List<MaterialPass>();

        public IList<Mesh> Meshes { get; } = new List<Mesh>();
    }
}