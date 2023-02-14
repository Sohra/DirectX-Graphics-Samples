using System.Collections.Generic;

namespace D3D12HelloWorld.Rendering {
    public class CompiledShader {
        public IDictionary<string, byte[]> Shaders { get; } = new Dictionary<string, byte[]>();

    }
}