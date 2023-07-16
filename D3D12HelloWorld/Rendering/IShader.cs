namespace wired.Rendering {
    public interface IShader {
        void Accept(ShaderGeneratorContext context);
    }
}