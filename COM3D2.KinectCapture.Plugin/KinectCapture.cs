using System;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Threading;
using COM3D2.KinectCapture.Shared.Contract;
using MiniIPC.Service;
using UnityEngine;
using UnityInjector;
using UnityInjector.Attributes;

namespace COM3D2.KinectCapture.Plugin
{
    static class NativeUtil
    {
        [DllImport("kernel32.dll")]
        public static extern bool FreeLibrary(IntPtr module);

        [DllImport("kernel32.dll")]
        public static extern IntPtr GetProcAddress(IntPtr module, string function);

        [DllImport("kernel32.dll")]
        public static extern IntPtr LoadLibrary(string path);
    }

    [PluginName("Kinect Capture")]
    [PluginVersion("1.0.0.0")]
    public class KinectCapture : PluginBase
    {
        const string LISTENER_NAME = "KinectCaptureListener";

        const string NATIVE_DLL_NAME = "COM3D2.KinectCapture.Native";

        NamedPipeStream captureServicePipe;
        KinectListener listener;
        StreamServiceReceiver<IKinectListener> listenerReceiver;
        readonly ManualResetEvent mre = new ManualResetEvent(false);

        IntPtr nativeLibrary;
        Thread runnerThread;
        bool running = true;

        KinectCapture() { BinariesFolder = Path.Combine(DataPath, "KinectCaptureBin"); }

        string BinariesFolder { get; }
        Action Close { get; set; }

        Action InitializeConnection { get; set; }

        void Awake()
        {
            DontDestroyOnLoad(this);

            Log($"Native binaries path: {BinariesFolder}");

            if (!Directory.Exists(BinariesFolder))
            {
                Error(
                    "Native binaries path does not exist (and thus has no binaries)! Creating empty folder and aborting...");
                Directory.CreateDirectory(BinariesFolder);
                Destroy(this);
                return;
            }

            var nativeDllPath = Path.Combine(BinariesFolder, $"{NATIVE_DLL_NAME}.dll");

            if (!File.Exists(nativeDllPath))
            {
                Error($"{NATIVE_DLL_NAME}.dll does not exist in the native binaries path! Aborting...");
                Destroy(this);
                return;
            }

            AppDomain.CurrentDomain.AssemblyResolve += ResolveNatives;

            nativeLibrary = NativeUtil.LoadLibrary(nativeDllPath);

            InitializeConnection = (Action) Marshal.GetDelegateForFunctionPointer(
                NativeUtil.GetProcAddress(nativeLibrary, nameof(InitializeConnection)), typeof(Action));
            Close = (Action) Marshal.GetDelegateForFunctionPointer(
                NativeUtil.GetProcAddress(nativeLibrary, nameof(Close)), typeof(Action));

            Log(
                $"Address of {nameof(InitializeConnection)}: {NativeUtil.GetProcAddress(nativeLibrary, nameof(InitializeConnection))}; {nameof(Close)}: {NativeUtil.GetProcAddress(nativeLibrary, nameof(Close))}");

            InitializeService();
        }

        void Error(string msg) { Debug.LogError($"[KinectCapture] {msg}"); }

        void InitializeService()
        {
            Log("Initializing services");

            InitializeConnection();

            Log("Done!");

            listener = new KinectListener();
            captureServicePipe = NamedPipeStream.Create(LISTENER_NAME, NamedPipeStream.PipeDirection.InOut);
            listenerReceiver = new StreamServiceReceiver<IKinectListener>(listener, captureServicePipe);
            runnerThread = new Thread(StartPipeHandler);
            runnerThread.Start();

            listener.Initialize();

            Log("Sending connection request");
            listener.KinectService.SetListener(LISTENER_NAME, ".");
            Log("Connected!");
        }

        void Log(string msg) { Console.WriteLine($"[KinectCapture] {msg}"); }

        void OnDestroy()
        {
            Log("Stopping listening to bone data");
            listener.KinectService.StopListeningBoneData();
            running = false;
            Log("Closing the listener pipe");
            listenerReceiver.Dispose();
            Log("Closing the service pipe");
            Close();
            Log("Freeing the library");
            NativeUtil.FreeLibrary(nativeLibrary);
        }

        Assembly ResolveNatives(object sender, ResolveEventArgs args)
        {
            var name = new AssemblyName(args.Name).Name;
            var dllPath = Path.Combine(BinariesFolder, $"{name}.dll");
            return File.Exists(dllPath) ? Assembly.LoadFile(dllPath) : null;
        }

        void StartPipeHandler()
        {
            mre.Set();
            captureServicePipe.WaitForConnection();

            while (running)
            {
                listenerReceiver.ProcessMessage();
                captureServicePipe.Flush();
            }

            captureServicePipe?.Dispose();
        }

        void Update()
        {
            if (Input.GetKeyDown(KeyCode.Keypad5))
                listener.KinectService.InitializeSensor();
            if (Input.GetKeyDown(KeyCode.Keypad2))
                listener.KinectService.ListenBoneData();
            if (Input.GetKeyDown(KeyCode.Keypad8))
                listener.KinectService.StopListeningBoneData();
        }
    }
}