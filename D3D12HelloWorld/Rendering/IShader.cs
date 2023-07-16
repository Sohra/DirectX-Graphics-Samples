namespace D3D12HelloWorld.Rendering {
    public interface IShader {
        void Accept(ShaderGeneratorContext context);
    }
}