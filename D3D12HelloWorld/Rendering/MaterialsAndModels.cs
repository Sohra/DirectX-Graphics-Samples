using System.Collections.Generic;
using System.Numerics;
using Vortice.Direct3D12;
using wired.Graphics;

namespace wired.Rendering {
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