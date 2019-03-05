using System;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Threading;
using COM3D2.KinectCapture.Shared.Contract;
using MiniIPC.Service;

namespace COM3D2.KinectCapture.Native
{
    public static class Exports
    {
        const string SERVICE_NAME = "KinectCaptureService";
        static KinectService service;
        static StreamServiceReceiver<IKinectService> serviceReceiver;
        static NamedPipeStream servicePipe;
        static bool running = true;
        static Thread thread;
        static string currentPath;

        [DllExport(CallingConvention.StdCall)]
        public static void Close()
        {
            servicePipe = null;
            running = false;
            serviceReceiver.Dispose();
            service.Close();
        }

        [DllExport(CallingConvention.StdCall)]
        public static void InitializeConnection()
        {
            currentPath = Path.GetDirectoryName(typeof(Exports).Assembly.Location);
            AppDomain.CurrentDomain.AssemblyResolve += ResolveLibraries;

            service = new KinectService();
            servicePipe = NamedPipeStream.Create(SERVICE_NAME, NamedPipeStream.PipeDirection.InOut);
            serviceReceiver = new StreamServiceReceiver<IKinectService>(service, servicePipe);
            thread = new Thread(StartPipeHandler);
            thread.Start();
        }

        static Assembly ResolveLibraries(object sender, ResolveEventArgs args)
        {
            var name = new AssemblyName(args.Name).Name;
            var dllPath = Path.Combine(currentPath, $"{name}.dll");

            return File.Exists(dllPath) ? Assembly.UnsafeLoadFrom(dllPath) : null;
        }

        static void StartPipeHandler()
        {
            servicePipe.WaitForConnection();

            while (running)
            {
                serviceReceiver.ProcessMessage();
                servicePipe.Flush();
            }

            servicePipe?.Dispose();
        }
    }
}