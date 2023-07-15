using System.Numerics;

namespace D3D12Bundles {
    class SimpleCamera {
        public SimpleCamera() {
            mInitialPosition = Vector3.Zero;
            mPosition = mInitialPosition;
            mYaw = (float)Math.PI;
            mPitch = 0.0f;
            mLookDirection = -Vector3.UnitZ;
            mUpDirection = Vector3.UnitY;
            mMoveSpeed = 20.0f;
            mTurnSpeed = (float)(Math.PI / 2.0);
            mKeysPressed = default;
        }

        public void Init(Vector3 position) {
            mInitialPosition = position;
            Reset();
        }

        public void Update(float elapsedSeconds) {
            // Calculate the move vector in camera space.
            var move = Vector3.Zero;

            if (mKeysPressed.A)
                move.X -= 1.0f;
            if (mKeysPressed.D)
                move.X += 1.0f;
            if (mKeysPressed.W)
                move.Z -= 1.0f;
            if (mKeysPressed.S)
                move.Z += 1.0f;
            if (mKeysPressed.Ctrl)
                move.Y -= 1.0f;
            if (mKeysPressed.Space)
                move.Y += 1.0f;

            if (MathF.Abs(move.X) > 0.1f && MathF.Abs(move.Z) > 0.1f) {
                Vector3 vector = Vector3.Normalize(move);
                move.X = vector.X;
                move.Z = vector.Z;
            }

            float moveInterval = mMoveSpeed * elapsedSeconds;
            float rotateInterval = mTurnSpeed * elapsedSeconds;

            if (mKeysPressed.Left)
                mYaw += rotateInterval;
            if (mKeysPressed.Right)
                mYaw -= rotateInterval;
            if (mKeysPressed.Up)
                mPitch += rotateInterval;
            if (mKeysPressed.Down)
                mPitch -= rotateInterval;

            // Prevent looking too far up or down.
            mPitch = MathF.Min(mPitch, MathF.PI / 4);
            mPitch = MathF.Max(-MathF.PI / 4, mPitch);

            // Move the camera in model space.
            float x = move.X * -MathF.Cos(mYaw) - move.Z * MathF.Sin(mYaw);
            float z = move.X * MathF.Sin(mYaw) - move.Z * MathF.Cos(mYaw);
            mPosition.X += x * moveInterval;
            mPosition.Z += z * moveInterval;
            mPosition.Y += move.Y * moveInterval;

            // Determine the look direction.
            float r = MathF.Cos(mPitch);
            mLookDirection.X = r * MathF.Sin(mYaw);
            mLookDirection.Y = MathF.Sin(mPitch);
            mLookDirection.Z = r * MathF.Cos(mYaw);
        }

        public Matrix4x4 GetViewMatrix() {
            var viewMatrix = CreateLookTo(mPosition, mLookDirection, mUpDirection);
            //return Matrix4x4.Transpose(viewMatrix);
            return viewMatrix;
        }

        public Matrix4x4 GetProjectionMatrix(float fov, float aspectRatio, float nearPlane = 1.0f, float farPlane = 1000.0f) {
            //Checking the source, this appears to be a right-handed matrix... equivalent of DirectX::XMMatrixPerspectiveFovRH
            var projectionMatrix = Matrix4x4.CreatePerspectiveFieldOfView(fov, aspectRatio, nearPlane, farPlane);
            return projectionMatrix;
            //var projectionMatrix = Matrix4x4.CreatePerspectiveFieldOfView(fov, aspectRatio, nearPlane, farPlane);
            //Matrix4x4.Invert(projectionMatrix, out var projectionMatrixRh);
            //return projectionMatrixRh;
        }

        public void SetMoveSpeed(float unitsPerSecond) {
            mMoveSpeed = unitsPerSecond;
        }

        public void SetTurnSpeed(float radiansPerSecond) {
            mTurnSpeed = radiansPerSecond;
        }

        public void OnKeyDown(Keys key) {
            switch (key) {
                case Keys.W:
                    mKeysPressed.W = true;
                    break;
                case Keys.A:
                    mKeysPressed.A = true;
                    break;
                case Keys.S:
                    mKeysPressed.S = true;
                    break;
                case Keys.D:
                    mKeysPressed.D = true;
                    break;
                case Keys.ControlKey | Keys.Control:
                    mKeysPressed.Ctrl = true;
                    break;
                case Keys.Space:
                    mKeysPressed.Space = true;
                    break;
                case Keys.Left:
                    mKeysPressed.Left = true;
                    break;
                case Keys.Right:
                    mKeysPressed.Right = true;
                    break;
                case Keys.Up:
                    mKeysPressed.Up = true;
                    break;
                case Keys.Down:
                    mKeysPressed.Down = true;
                    break;
                case Keys.Escape:
                    Reset();
                    break;
            }
        }

        public void OnKeyUp(Keys key) {
            switch (key) {
                case Keys.W:
                    mKeysPressed.W = false;
                    break;
                case Keys.A:
                    mKeysPressed.A = false;
                    break;
                case Keys.S:
                    mKeysPressed.S = false;
                    break;
                case Keys.D:
                    mKeysPressed.D = false;
                    break;
                case Keys.ControlKey:
                    mKeysPressed.Ctrl = false;
                    break;
                case Keys.Space:
                    mKeysPressed.Space = false;
                    break;
                case Keys.Left:
                    mKeysPressed.Left = false;
                    break;
                case Keys.Right:
                    mKeysPressed.Right = false;
                    break;
                case Keys.Up:
                    mKeysPressed.Up = false;
                    break;
                case Keys.Down:
                    mKeysPressed.Down = false;
                    break;
            }
        }

        void Reset() {
            mPosition = mInitialPosition;
            mYaw = (float)Math.PI;
            mPitch = 0.0f;
            mLookDirection = -Vector3.UnitZ;
        }

        Matrix4x4 CreateLookTo(Vector3 eyePosition, Vector3 eyeDirection, Vector3 upDirection) {
            Vector3 zaxis = Vector3.Normalize(-eyeDirection);
            Vector3 xaxis = Vector3.Normalize(Vector3.Cross(upDirection, zaxis));
            Vector3 yaxis = Vector3.Cross(zaxis, xaxis);

            Matrix4x4 result = Matrix4x4.Identity;

            result.M11 = xaxis.X;
            result.M12 = yaxis.X;
            result.M13 = zaxis.X;

            result.M21 = xaxis.Y;
            result.M22 = yaxis.Y;
            result.M23 = zaxis.Y;

            result.M31 = xaxis.Z;
            result.M32 = yaxis.Z;
            result.M33 = zaxis.Z;

            result.M41 = -Vector3.Dot(xaxis, eyePosition);
            result.M42 = -Vector3.Dot(yaxis, eyePosition);
            result.M43 = -Vector3.Dot(zaxis, eyePosition);

            return result;
        }

        struct KeysPressed {
            public bool W;
            public bool A;
            public bool S;
            public bool D;

            public bool Ctrl;
            public bool Space;

            public bool Left;
            public bool Right;
            public bool Up;
            public bool Down;
        };

        Vector3 mInitialPosition;
        Vector3 mPosition;
        float mYaw;              // Relative to the +z axis.
        float mPitch;            // Relative to the xz plane.
        Vector3 mLookDirection;
        Vector3 mUpDirection;
        float mMoveSpeed;        // Speed at which the camera moves, in units per second.
        float mTurnSpeed;        // Speed at which the camera turns, in radians per second.
        KeysPressed mKeysPressed;
    }
}