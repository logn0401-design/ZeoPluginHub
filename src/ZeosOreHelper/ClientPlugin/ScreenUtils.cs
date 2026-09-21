using Sandbox.ModAPI;
using VRageMath;

namespace ZeosOreHelper
{
    internal static class ScreenUtils
    {
        public static Vector2D WorldToScreen(Vector3D worldPosition, out bool offScreen)
        {
            var camera = MyAPIGateway.Session==null?null:MyAPIGateway.Session.Camera;
            if(camera==null){offScreen=true;return new Vector2D(double.NaN,double.NaN);}
            var screen = camera.WorldToScreen(ref worldPosition);

            var toTarget = worldPosition - camera.Position;
            if (toTarget.LengthSquared() > 0.000001)
                toTarget.Normalize();

            var behind = Vector3D.Dot(camera.WorldMatrix.Forward, toTarget) < 0.0;
            offScreen =
                screen.X > 1.0 || screen.X < -1.0 ||
                screen.Y > 1.0 || screen.Y < -1.0 ||
                behind;

            if (behind)
                screen *= -1.0;

            return new Vector2D(screen.X, screen.Y);
        }

        public static bool IsOutsideHud(Vector2D screenPos)
        {
            const double padding = 0.08;
            var camera = MyAPIGateway.Session.Camera;
            if(camera==null||camera.ViewportSize.X<=0)return true;
            var heightRatio = camera.ViewportSize.Y / camera.ViewportSize.X;
            var scaled = screenPos * new Vector2D(1.0, heightRatio);
            var hudHeight = heightRatio - padding;
            var hudWidth = 1.0 - padding;
            return
                scaled.X < -hudWidth || scaled.X > hudWidth ||
                scaled.Y < -hudHeight || scaled.Y > hudHeight;
        }

        public static Vector2D ClampOffscreen(Vector2D screenPos)
        {
            var camera = MyAPIGateway.Session.Camera;
            var heightRatio = camera.ViewportSize.Y / camera.ViewportSize.X;
            var scaled = screenPos * new Vector2D(1.0, heightRatio);
            if (scaled.LengthSquared() < 0.000001)
                scaled = new Vector2D(0.0, 1.0);
            scaled.Normalize();
            scaled *= 0.76;
            return new Vector2D(scaled.X, scaled.Y);
        }
    }
}
