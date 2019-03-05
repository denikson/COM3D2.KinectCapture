using MessagePack;

namespace COM3D2.KinectCapture.Shared.Data
{
    [MessagePackObject]
    public class Vec3
    {
        [Key(0)] public float X { get; set; }

        [Key(1)] public float Y { get; set; }

        [Key(2)] public float Z { get; set; }
    }

    [MessagePackObject]
    public class Vec4
    {
        [Key(3)] public float W { get; set; }
        [Key(0)] public float X { get; set; }

        [Key(1)] public float Y { get; set; }

        [Key(2)] public float Z { get; set; }
    }
}