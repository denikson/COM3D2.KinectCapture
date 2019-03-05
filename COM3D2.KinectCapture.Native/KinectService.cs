using System.Collections.Generic;
using System.Linq;
using COM3D2.KinectCapture.Shared.Contract;
using COM3D2.KinectCapture.Shared.Data;
using Microsoft.Kinect;
using MiniIPC.Service;

namespace COM3D2.KinectCapture.Native
{
    public class KinectService : IKinectService
    {
        Body[] bodies;
        BodyFrameReader bodyFrameReader;
        IKinectListener listener;
        NamedPipeStream listenerPipe;
        StreamServiceSender<IKinectListener> listenerSender;
        KinectSensor sensor;

        public void InitializeSensor()
        {
            listener.OnLogMessageReceived("Initializing the sensor");
            sensor = KinectSensor.GetDefault();
            sensor.Open();
            listener.OnLogMessageReceived(
                $"Got sensor: {sensor}. Open: {sensor.IsOpen}. Available: {sensor.IsAvailable}.");
        }

        public void ListenBoneData()
        {
            if (sensor == null)
            {
                listener.OnLogMessageReceived("No sensor attached!");
                return;
            }

            if (bodyFrameReader != null)
                bodyFrameReader.IsPaused = false;
            else
            {
                bodyFrameReader = sensor.BodyFrameSource.OpenReader();
                bodyFrameReader.FrameArrived += BodyFrameReaderOnFrameArrived;
            }

            listener.OnLogMessageReceived("Started listening to body frames");
        }

        public void StopListeningBoneData()
        {
            if (bodyFrameReader == null)
            {
                listener.OnLogMessageReceived("No active body frame!");
                return;
            }

            bodyFrameReader.IsPaused = true;
            listener.OnLogMessageReceived("Stopped listening to body frames");
        }

        public void SetListener(string pipeName, string server)
        {
            listenerPipe = NamedPipeStream.Open(pipeName, server, NamedPipeStream.PipeDirection.InOut);
            listenerSender = new StreamServiceSender<IKinectListener>(listenerPipe);
            listener = listenerSender.Service;

            listener.OnLogMessageReceived($"Connected listener from {pipeName}@{server}");
        }

        public void Close() { sensor?.Close(); }

        void BodyFrameReaderOnFrameArrived(object sender, BodyFrameArrivedEventArgs e)
        {
            using (var frame = e.FrameReference.AcquireFrame())
            {
                if (bodies == null)
                    bodies = new Body[frame.BodyCount];

                frame.GetAndRefreshBodyData(bodies);

                var trackedBody = bodies.FirstOrDefault(body => body.IsTracked);
                if (trackedBody == null)
                    return;

                listener.OnLogMessageReceived("Got body");

                var joints = new Dictionary<BodyJointType, BodyJoint>();

                foreach (var joint in trackedBody.Joints.Values)
                    joints[(BodyJointType) joint.JointType] = new BodyJoint
                    {
                        Position = new Vec3 {X = joint.Position.X, Y = joint.Position.Y, Z = joint.Position.Z}
                    };

                foreach (var jointOrientation in trackedBody.JointOrientations.Values)
                    joints[(BodyJointType) jointOrientation.JointType].Orientation = new Vec4
                    {
                        X = jointOrientation.Orientation.X,
                        Y = jointOrientation.Orientation.Y,
                        Z = jointOrientation.Orientation.Z,
                        W = jointOrientation.Orientation.W
                    };

                listener.OnBodyFrameReceived(joints);
            }
        }
    }
}