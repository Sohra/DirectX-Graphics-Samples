using Vortice.DXGI;

namespace wired.Graphics {
    public class PresentationParameters {
        public PresentationParameters() {
        }

        public PresentationParameters(int backBufferWidth, int backBufferHeight)
            : this(backBufferWidth, backBufferHeight, Format.B8G8R8A8_UNorm) {
        }

        public PresentationParameters(int backBufferWidth, int backBufferHeight, Format backBufferFomat) {
            BackBufferWidth = backBufferWidth;
            BackBufferHeight = backBufferHeight;
            BackBufferFormat = backBufferFomat;
        }

        public int BackBufferWidth { get; set; }

        public int BackBufferHeight { get; set; }

        public Format BackBufferFormat { get; set; } = Format.B8G8R8A8_UNorm;

        public Format DepthStencilFormat { get; set; } = Format.D32_Float;

        public PresentParameters PresentParameters { get; set; }

        public bool Stereo { get; set; }

        public int SyncInterval { get; set; } = 1;

        public PresentationParameters Clone() => (PresentationParameters)MemberwiseClone();
    }
}