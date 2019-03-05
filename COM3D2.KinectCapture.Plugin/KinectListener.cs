using System;
using System.Collections.Generic;
using COM3D2.KinectCapture.Shared.Contract;
using COM3D2.KinectCapture.Shared.Data;
using MiniIPC.Service;

namespace COM3D2.KinectCapture.Plugin
{
    public class KinectListener : IKinectListener
    {
        const string SERVICE_NAME = "KinectCaptureService";
        StreamServiceSender<IKinectService> kinectServiceSender;
        NamedPipeStream servicePipe;

        public Queue<Dictionary<BodyJointType, BodyJoint>> JointFrameQueue { get; } = new Queue<Dictionary<BodyJointType, BodyJoint>>();
        public IKinectService KinectService { get; private set; }
        object bodyLock = new object();

        public void OnBodyFrameReceived(Dictionary<BodyJointType, BodyJoint> joints)
        {
            JointFrameQueue.Enqueue(joints);
        }

        public Dictionary<BodyJointType, BodyJoint> GetNextBodyFrame()
        {
            return JointFrameQueue.Count == 0 ? null : JointFrameQueue.Dequeue();
        }

        public void OnLogMessageReceived(string message) { Console.WriteLine($"[KinectCaptureService] {message}"); }

        public void Initialize()
        {
            servicePipe = NamedPipeStream.Open(SERVICE_NAME, NamedPipeStream.PipeDirection.InOut);
            kinectServiceSender = new StreamServiceSender<IKinectService>(servicePipe);
            KinectService = kinectServiceSender.Service;
        }
    }
}