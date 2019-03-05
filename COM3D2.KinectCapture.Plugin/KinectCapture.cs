using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Threading;
using COM3D2.KinectCapture.Shared.Contract;
using COM3D2.KinectCapture.Shared.Data;
using COM3D2.KinectCapture.Shared.Util;
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

        uint threadID;

        NamedPipeStream captureServicePipe;
        KinectListener listener;
        StreamServiceReceiver<IKinectListener> listenerReceiver;

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

            //Log(
            //    $"Address of {nameof(InitializeConnection)}: {NativeUtil.GetProcAddress(nativeLibrary, nameof(InitializeConnection))}; {nameof(Close)}: {NativeUtil.GetProcAddress(nativeLibrary, nameof(Close))}");

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

        void Log(string msg) { Debug.Log($"[KinectCapture] {msg}"); }

        void OnDestroy()
        {
            Log("Stopping listening to bone data");
            listener.KinectService.StopListeningBoneData();
            running = false;
            ThreadHelper.CancelSynchronousIo(threadID);
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
            threadID = ThreadHelper.GetCurrentThreadId();
            captureServicePipe.WaitForConnection();

            while (running)
            {
                listenerReceiver.ProcessMessage();
                captureServicePipe.Flush();
            }

            captureServicePipe?.Dispose();
        }

        GameObject kinectPosition;
        Dictionary<BodyJointType, GameObject> joints = new Dictionary<BodyJointType, GameObject>();
        bool trackBones = false;

        void LateUpdate()
        {
            if (!trackBones)
                return;

            var jointInfo = listener.GetNextBodyFrame();

            if (jointInfo == null)
                return;

            foreach (var joint in joints)
            {
                var o = joint.Value;
                if (jointInfo.TryGetValue(joint.Key, out var info))
                {
                    o.SetActive(true);
                    o.transform.localPosition = new Vector3(info.Position.X * 1.5f, info.Position.Y * 1.5f, info.Position.Z * 1.5f);
                }
                else
                {
                    o.SetActive(false);
                }
            }
        }

        void Update()
        {
            if (Input.GetKeyDown(KeyCode.Keypad5))
                listener.KinectService.InitializeSensor();
            if (Input.GetKeyDown(KeyCode.Keypad2))
            {

                var maid = FindObjectOfType<Maid>();

                if (maid == null)
                {
                    Log("No maid!");
                    return;
                }

                var pos = maid.transform.position;

                Log($"Found maid at {pos}");

                //kinectPosition = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                kinectPosition = new GameObject();
                kinectPosition.transform.position = pos - Vector3.back * 1.0f + Vector3.up * 1.0f;
                kinectPosition.transform.Rotate(Vector3.up, 180.0f);
                kinectPosition.transform.localScale = 0.5f * Vector3.one;

                foreach (var value in Enum.GetValues(typeof(BodyJointType)))
                {
                    var o = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                    o.transform.localScale = 0.1f * Vector3.one;
                    o.GetComponent<Renderer>().material.color = Color.red;
                    o.SetActive(false);
                    o.transform.SetParent(kinectPosition.transform);
                    joints[(BodyJointType)value] = o;
                }

                listener.KinectService.ListenBoneData();
                trackBones = true;
            }
            if (Input.GetKeyDown(KeyCode.Keypad8))
                listener.KinectService.StopListeningBoneData();

            if (Input.GetKeyDown(KeyCode.I))
                kinectPosition.transform.position += 0.25f * Vector3.forward;
            if (Input.GetKeyDown(KeyCode.K))
                kinectPosition.transform.position -= 0.25f * Vector3.forward;
            if (Input.GetKeyDown(KeyCode.J))
                kinectPosition.transform.position += 0.25f * Vector3.left;
            if (Input.GetKeyDown(KeyCode.L))
                kinectPosition.transform.position -= 0.25f * Vector3.left;
            if (Input.GetKeyDown(KeyCode.O))
                kinectPosition.transform.position += 0.25f * Vector3.up;
            if (Input.GetKeyDown(KeyCode.U))
                kinectPosition.transform.position -= 0.25f * Vector3.up;
        }
    }
}