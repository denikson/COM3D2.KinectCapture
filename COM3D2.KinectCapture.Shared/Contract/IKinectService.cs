namespace COM3D2.KinectCapture.Shared.Contract
{
    public interface IKinectService
    {
        void InitializeSensor();

        void ListenBoneData();
        void SetListener(string pipeName, string serverName);

        void StopListeningBoneData();
    }
}