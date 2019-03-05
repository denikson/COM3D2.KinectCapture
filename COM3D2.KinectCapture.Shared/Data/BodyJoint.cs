using MessagePack;

namespace COM3D2.KinectCapture.Shared.Data
{
    [MessagePackObject]
    public class BodyJoint
    {
        [Key(1)] public Vec4 Orientation { get; set; }

        [Key(0)] public Vec3 Position { get; set; }
    }
}