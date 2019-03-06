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
using Random = UnityEngine.Random;

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

        Dictionary<Transform, GameObject> boneDict = new Dictionary<Transform, GameObject>();

        readonly string[] boneNames =
        {
            //"Bip01",
            "Bip01 Head", // => Head (doesn't work?)
            "Bip01 Neck", // Neck
            "Bip01 Pelvis", // => SpineBase?
            "Bip01 Spine", // => SpineBase?
            "Bip01 Spine0a", // None
            "Bip01 Spine1", // SpineMid
            "Bip01 Spine1a", // None / SpineMid
            //"Mouth",
            //"Mune_R",
            //"Nipple_R",
            "Bip01 R Clavicle", // None / ShoulderRight
            "Bip01 R UpperArm", // ShoulderLeft
            "Bip01 R Forearm", // ElbowRight
            "Bip01 R Hand", // HandRight
            //"Mune_L",
            //"Mune_L_sub",
            //"Nipple_L",
            "Bip01 L Clavicle", // None / ShoulderLeft
            "Bip01 L UpperArm", // ShoulderRight
            "Bip01 L Forearm", // ElbowLeft
            "Bip01 L Hand", // HandLeft
            //"Hip_R", 
            "Bip01 R Thigh", // => HipRight
            "Bip01 R Calf", // => KneeRight
            "Bip01 R Foot", // => AnkleRight / FootRight
            //"Hip_L",  
            "Bip01 L Thigh", // => HipLeft
            "Bip01 L Calf", // => KneeLeft
            "Bip01 L Foot" // => AnkleLeft / FootLeft
        };

        readonly List<KeyValuePair<string, GameObject>> boneObjects = new List<KeyValuePair<string, GameObject>>();

        NamedPipeStream captureServicePipe;

        int index;
        readonly Dictionary<BodyJointType, GameObject> joints = new Dictionary<BodyJointType, GameObject>();

        GameObject kinectPosition;
        KinectListener listener;
        StreamServiceReceiver<IKinectListener> listenerReceiver;
        TBody maidBody;
        readonly List<GameObject> maidTransforms = new List<GameObject>();

        IntPtr nativeLibrary;
        Color prevColor;
        int prevIndex = -1;
        Quaternion prevRotation = Quaternion.identity;
        GameObject prevSphere;
        Thread runnerThread;
        bool running = true;
        float t;

        uint threadID;
        bool trackBones;

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


        void DrawBones()
        {
            boneObjects.Clear();
            maidTransforms.Clear();

            var maid = FindObjectOfType<Maid>();
            if (maid == null)
                return;

            maidBody = maid.body0;


            foreach (var boneName in boneNames)
            {
                var bone = maidBody.GetBone(boneName);
                if (bone == null)
                {
                    Error($"Bone {boneName} does not exist!");
                    continue;
                }

                var c = Random.ColorHSV();

                var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
                go.GetComponent<Renderer>().material = new Material(Shader.Find("Transparent/Diffuse")) {color = c};
                go.transform.position = bone.position - 2.0f * Vector3.left;
                go.transform.localRotation = Quaternion.identity;
                go.transform.localScale = 0.05f * Vector3.one;
                go.SetActive(true);

                maidTransforms.Add(bone.gameObject);
                boneObjects.Add(new KeyValuePair<string, GameObject>(boneName, go));
            }
        }

        void Error(string msg) { Debug.LogError($"[KinectCapture] {msg}"); }

        Transform GetParent(Transform root, Transform child)
        {
            if (root == child)
                return null;

            for (var i = 0; i < root.childCount; i++)
            {
                var rootChild = root.GetChild(i);
                if (child == rootChild)
                    return root;
                var result = GetParent(root.GetChild(i), child);
                if (result != null)
                    return result;
            }

            return null;
        }

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
                    o.transform.localPosition =
                        new Vector3(info.Position.X * 1.5f, info.Position.Y * 1.5f, info.Position.Z * 1.5f);
                    o.transform.localRotation =
                        new Quaternion(info.Orientation.X, info.Orientation.Y, info.Orientation.Z, info.Orientation.W);
                    Console.WriteLine($"Rotation quaternion: {o.transform.localRotation}");
                }
                else
                    o.SetActive(false);
            }
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

        void Update()
        {
            // Add delegate to OnLateUpdateEnd in maid body to override all transforms
            if (maidBody != null)
                maidBody.OnLateUpdateEnd =
                    (Action) Delegate.Combine(maidBody.OnLateUpdateEnd, new Action(UpdateMotion));

            // Track the bone
            for (var i = 0; i < boneObjects.Count; i++)
            {
                var sphere = boneObjects[i];
                var bone = maidTransforms[i];
                sphere.Value.transform.position = bone.transform.position - 2.0f * Vector3.left;
                sphere.Value.transform.rotation = bone.transform.rotation;
            }

            // Highlight one of the bones
            if (Input.GetKeyDown(KeyCode.Keypad9))
            {
                var sphere = boneObjects[index];
                if (prevSphere != null)
                {
                    maidTransforms[prevIndex].transform.rotation = prevRotation;
                    prevSphere.transform.localScale = Vector3.one * 0.05f;
                    prevSphere.GetComponent<Renderer>().material.color = prevColor;
                }

                Log($"Highlighting {sphere.Key}");

                prevSphere = sphere.Value;
                sphere.Value.transform.localScale = Vector3.one * 0.08f;
                prevColor = prevSphere.GetComponent<Renderer>().material.color;
                prevSphere.GetComponent<Renderer>().material.color = new Color(1f, 1f, 1f, 0.5f);
                prevRotation = maidTransforms[index].transform.rotation;
                prevIndex = index;
                index = (index + 1) % boneObjects.Count;
            }

            // Create custom objects of the bones
            if (Input.GetKeyDown(KeyCode.Keypad7))
                DrawBones();
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
                    joints[(BodyJointType) value] = o;
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

        void UpdateMotion()
        {
            if (prevSphere != null)
            {
                t += 100.0f * Time.deltaTime;

                if (t > 360f)
                    t = 0f;

                maidTransforms[prevIndex].transform.rotation = Quaternion.Euler(t, t, t);
            }
        }
    }
}