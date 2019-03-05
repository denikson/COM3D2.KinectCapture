using System.Collections.Generic;
using System.Linq;
using System.Windows.Forms;
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
        KinectJointFilter jointFilter;

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
                jointFilter = new KinectJointFilter();
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

                jointFilter.UpdateFilter(trackedBody);

                var filteredJoints = jointFilter.GetFilteredJoints();

                var joints = new Dictionary<BodyJointType, BodyJoint>();

                for (var index = 0; index < filteredJoints.Length; index++)
                {
                    var joint = filteredJoints[index];
                    joints[(BodyJointType) index] = new BodyJoint
                    {
                        Position = new Vec3 {X = joint.X, Y = joint.Y, Z = joint.Z}
                    };
                }

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