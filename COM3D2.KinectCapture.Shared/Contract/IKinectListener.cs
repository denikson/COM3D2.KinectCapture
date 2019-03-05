using System.Collections.Generic;
using COM3D2.KinectCapture.Shared.Data;

namespace COM3D2.KinectCapture.Shared.Contract
{
    public interface IKinectListener
    {
        void OnBodyFrameReceived(Dictionary<BodyJointType, BodyJoint> joints);

        void OnLogMessageReceived(string message);
    }
}