using System;
using System.Runtime.CompilerServices;
using Vortice.Direct3D12;
using Vortice.DXGI;

namespace D3D12HelloWorld.Rendering {
    public class GraphicsResource : IDisposable {
        ConstantBufferView? defaultConstantBufferView;

        public GraphicsResource(GraphicsDevice device, ResourceDescription description, HeapType heapType) : this(device, CreateResource(device, description, heapType)) {
        }

        internal GraphicsResource(GraphicsDevice device, ID3D12Resource resource) {
            GraphicsDevice = device;
            NativeResource = resource;
        }

        public GraphicsDevice GraphicsDevice { get; }

        public ResourceDescription Description => Unsafe.As<ResourceDescription, ResourceDescription>(ref Unsafe.AsRef(NativeResource.Description));

        public ResourceDimension Dimension => Description.Dimension;

        public HeapType HeapType => NativeResource.HeapProperties.Type;

        public ulong Width => Description.Width;

        public int Height => Description.Height;

        public ushort DepthOrArraySize => Description.DepthOrArraySize;

        public ulong SizeInBytes => Description.Width * (uint)Description.Height * Description.DepthOrArraySize;

        public Format Format => Description.Format;

        public ResourceFlags Flags => Description.Flags;

        public IntPtr MappedResource { get; private set; }

        public ConstantBufferView DefaultConstantBufferView => defaultConstantBufferView ??= new ConstantBufferView(this);

        internal ID3D12Resource NativeResource { get; }

        public void Dispose() {
            NativeResource.Dispose();
        }

        public IntPtr Map(int subresource) {
            unsafe {
                void* mappedResource;
                NativeResource.Map(subresource, default, &mappedResource).CheckError();

                MappedResource = new IntPtr(mappedResource);
            }
            return MappedResource;

        }

        public void Unmap(int subresource) {
            NativeResource.Unmap(subresource);
            MappedResource = IntPtr.Zero;
        }

        #region Creation Methods

        public static GraphicsResource CreateBuffer(GraphicsDevice device, int size, ResourceFlags bufferFlags, HeapType heapType = HeapType.Default) {
            return new GraphicsResource(device, ResourceDescription.Buffer(size, bufferFlags), heapType);
        }

        public static GraphicsResource CreateBuffer<T>(GraphicsDevice device, int elementCount, ResourceFlags bufferFlags, HeapType heapType = HeapType.Default) where T : unmanaged {
            return CreateBuffer(device, elementCount * Unsafe.SizeOf<T>(), bufferFlags, heapType);
        }

        public static unsafe GraphicsResource CreateBuffer<T>(GraphicsDevice device, in T data, ResourceFlags bufferFlags, HeapType heapType = HeapType.Default) where T : unmanaged {
            fixed (T* pointer = &data) {
                return CreateBuffer(device, new Span<T>(pointer, 1), bufferFlags, heapType);
            }
        }

        public static GraphicsResource CreateBuffer<T>(GraphicsDevice device, Span<T> data, ResourceFlags bufferFlags, HeapType heapType = HeapType.Default) where T : unmanaged {
            GraphicsResource buffer = CreateBuffer<T>(device, data.Length, bufferFlags, heapType);
            buffer.SetData(data);

            return buffer;
        }

        #endregion

        private static ID3D12Resource CreateResource(GraphicsDevice device, ResourceDescription description, HeapType heapType) {
            ResourceStates resourceStates = ResourceStates.Common;

            if (heapType == HeapType.Upload) {
                resourceStates = ResourceStates.GenericRead;
            }
            else if (heapType == HeapType.Readback) {
                resourceStates = ResourceStates.CopyDest;
            }

            return device.NativeDevice.CreateCommittedResource(new HeapProperties(heapType), HeapFlags.None, description, resourceStates);
        }

        public T[] GetArray<T>(uint offsetInBytes = 0) where T : unmanaged {
            T[] data = new T[(SizeInBytes / (uint)Unsafe.SizeOf<T>()) - offsetInBytes];
            GetData(data.AsSpan(), offsetInBytes);

            return data;
        }

        public T GetData<T>(uint offsetInBytes = 0) where T : unmanaged {
            T data = new T();
            GetData(ref data, offsetInBytes);

            return data;
        }

        public unsafe void GetData<T>(ref T data, uint offsetInBytes = 0) where T : unmanaged {
            fixed (T* pointer = &data) {
                GetData(new Span<T>(pointer, 1), offsetInBytes);
            }
        }

        public void GetData<T>(Span<T> data, uint offsetInBytes = 0) where T : unmanaged {
            if (Dimension == ResourceDimension.Buffer) {
                if (HeapType == HeapType.Default) {
                    using GraphicsResource readbackBuffer = CreateBuffer<T>(GraphicsDevice, data.Length, ResourceFlags.None, HeapType.Readback);
                    using var copyCommandList = new CommandList(GraphicsDevice, CommandListType.Copy);

                    copyCommandList.CopyBufferRegion(readbackBuffer.NativeResource, 0, NativeResource, offsetInBytes, (uint)data.Length * (uint)Unsafe.SizeOf<T>());
                    copyCommandList.Flush();

                    readbackBuffer.GetData(data);
                }
                else {
                    Map(0);
                    IntPtr source = MappedResource + (int)offsetInBytes;
                    source.CopyTo(data);
                    Unmap(0);
                }
            }
        }

        public unsafe void SetData<T>(in T data, uint offsetInBytes = 0) where T : unmanaged {
            fixed (T* pointer = &data) {
                SetData(new Span<T>(pointer, 1), offsetInBytes);
            }
        }

        public void SetData<T>(Span<T> data, uint offsetInBytes = 0) where T : unmanaged {
            if (Dimension == ResourceDimension.Buffer) {
                if (HeapType == HeapType.Default) {
                    using GraphicsResource uploadBuffer = CreateBuffer(GraphicsDevice, data, ResourceFlags.None, HeapType.Upload);
                    using var copyCommandList = new CommandList(GraphicsDevice, CommandListType.Copy);

                    copyCommandList.CopyBufferRegion(NativeResource, offsetInBytes, uploadBuffer.NativeResource, 0, (uint)data.Length * (uint)Unsafe.SizeOf<T>());
                    copyCommandList.Flush();
                }
                else {
                    Map(0);
                    data.CopyTo(MappedResource + (int)offsetInBytes);
                    Unmap(0);
                }
            }
            else if (Dimension == ResourceDimension.Texture2D) {
                ID3D12Resource uploadResource = GraphicsDevice.NativeDevice.CreateCommittedResource(new HeapProperties(CpuPageProperty.WriteBack, MemoryPool.L0), HeapFlags.None, NativeResource.Description, ResourceStates.CopyDest);
                using var textureUploadBuffer = new Texture(GraphicsDevice, uploadResource);

                textureUploadBuffer.NativeResource.WriteToSubresource(0, data, (int)Width * 4, (int)Width * Height * 4);

                using var copyCommandList = new CommandList(GraphicsDevice, CommandListType.Copy);

                copyCommandList.CopyResource(this, textureUploadBuffer);
                copyCommandList.Flush();
            }
        }
    }
}