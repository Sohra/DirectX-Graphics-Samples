using System.Collections.Generic;

namespace wired.Rendering {
    public class CompiledShader {
        public IDictionary<string, byte[]> Shaders { get; } = new Dictionary<string, byte[]>();

    }
}