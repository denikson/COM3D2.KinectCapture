using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
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
                    o.transform.localRotation = new Quaternion(info.Orientation.X, info.Orientation.Y, info.Orientation.Z, info.Orientation.W);
                    Console.WriteLine($"Rotation quaternion: {o.transform.localRotation}");
                }
                else
                {
                    o.SetActive(false);
                }
            }
        }

        Transform GetParent(Transform root, Transform child)
        {
            if (root == child)
                return null;

            for (int i = 0; i < root.childCount; i++)
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

        Dictionary<Transform, GameObject> boneDict = new Dictionary<Transform, GameObject>();

        string[] boneNames =
        {
            //"Bip01",
            "Bip01 Head", // => Head (doesn't work?)
            "Bip01 Neck", // Neck
            "Bip01 Pelvis", // => SpineBase?
            "Bip01 Spine", // => SpineBase?
            "Bip01 Spine0a", // None
            "Bip01 Spine1", // SpineMid
            "Bip01 Spine1a", // None / SpineMid
            "Mouth",
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
            "Hip_R", // => HipRight
            "Bip01 R Thigh",
            "Bip01 R Calf", // => KneeRight
            "Bip01 R Foot", // => AnkleRight / FootRight
            "Hip_L",  // => HipLeft
            "Bip01 L Thigh",
            "Bip01 L Calf", // => KneeLeft
            "Bip01 L Foot", // => AnkleLeft / FootLeft
        };

        List<KeyValuePair<string, GameObject>> boneObjects = new List<KeyValuePair<string, GameObject>>();
        List<GameObject> maidTransforms = new List<GameObject>();
        TBody maidBody;


        void DrawBones()
        {

            //if (drawBones)
            //{
            //    drawBones = false;
            //    bones.Clear();
            //    colors.Clear();
            //    Debug.Log($"Stopped drawing bones");
            //    return;
            //}

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
                go.GetComponent<Renderer>().material = new Material(Shader.Find("Transparent/Diffuse"))
                {
                    color = c
                };
                go.transform.position = bone.position - 2.0f * Vector3.left;
                go.transform.localRotation = Quaternion.identity;
                go.transform.localScale = 0.05f * Vector3.one;
                go.SetActive(true);

                maidTransforms.Add(bone.gameObject);
                boneObjects.Add(new KeyValuePair<string, GameObject>(boneName, go));
            }

            //var meshRenderers = maid.gameObject.GetComponentsInChildren<SkinnedMeshRenderer>();

            //var bonesSet = new HashSet<Transform>();

            //var mat = new Material(Shader.Find("Transparent/Diffuse"))
            //{
            //    color = new Color(0.4f, 0.4f, 1f, 0.8f)
            //};

            //using (var fs = new StreamWriter(File.Create("bones_colors.txt")))
            //{
            //    fs.AutoFlush = true;
            //    foreach (var skinnedMeshRenderer in meshRenderers)
            //        foreach (var bone in skinnedMeshRenderer.bones.Where(b => b != null))
            //        {

            //            fs.WriteLine($"{bone.name}"); /*=> ({ (int)Math.Round(c.r * 255)}, { (int)Math.Round(c.g * 255)}, { (int)Math.Round(c.b * 255)})*/
            //            //var go = new GameObject();
            //            //go.transform.SetParent(bone, false);
            //            //var gizmo = go.AddComponent<GizmoRender>();
            //            //gizmo.Visible = true;
            //            //gizmo.eRotate = true;
            //            //gizmo.offsetScale = 0.25f;
            //            //gizmo.eAxis = true;
            //            //gizmo.lineRSelectedThick = 0.25f;
            //            var go = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            //            go.GetComponent<Renderer>().material = new Material(Shader.Find("Transparent/Diffuse"))
            //            {
            //                color = c
            //            };
            //            //rend.material = mat;
            //            //go.layer = 8;
            //            //go.transform.SetParent(bone);
            //            go.transform.position = bone.position - 2.0f * Vector3.left;
            //            //go.transform.localPosition = Vector3.right * 2.0f;
            //            go.transform.localRotation = Quaternion.identity;
            //            go.transform.localScale = 0.1f * Vector3.one;
            //            go.SetActive(true);

            //            //boneDict.Add(bone, go);
            //            //spheres.Add(go.transform);
            //        }
            //}

            //foreach (var uiCamera in NGUITools.FindActive<UICamera>())
            //{
            //    uiCamera.GetComponent<Camera>().enabled = true;
            //}
            //bonesSet.Add(bone);

            //foreach (var bone in bonesSet.ToList())
            //{
            //    var parent = GetParent(maid.body0.m_trBones, bone);
            //    while (parent != null && parent != maid.body0.m_trBones)
            //    {
            //        if (bonesSet.Contains(parent))
            //        {
            //            bonesSet.Remove(bone);
            //            break;
            //        }
            //        parent = GetParent(maid.body0.m_trBones, parent);
            //    }
            //}

            //bones.AddRange(bonesSet);
            //foreach (var bone in bones)
            //    colors.Add(Random.ColorHSV());

            //Debug.Log($"Got {bonesSet.Count} separate bones");

            //HashSet<Transform> spheres = new HashSet<Transform>();

            //foreach (var bone in bonesSet)
            //{
            //    MakeSphere(bone, spheres);
            //}

            //Debug.Log($"Drawing {bones.Count} bones");

            //drawBones = !drawBones;
        }

        void UpdateMotion()
        {
            if (prevSphere != null)
            {
                t += 100.0f * Time.deltaTime;

                if (t > 360f)
                    t = 0f;

                maidTransforms[prevIndex].transform.rotation = Quaternion.Euler(t, t, t);

                //var rot = bone.transform.rotation.eulerAngles;
                //Log($"Old rot: {rot}");

                //rot.x += 100f * Time.deltaTime;
                //rot.y += 100f * Time.deltaTime;
                //rot.z += 100f * Time.deltaTime;

                //Log($"Temp rot: {rot}");

                //bone.transform.rotation = Quaternion.Euler(rot);

                //Log($"New rot: {maidTransforms[prevIndex].transform.rotation.eulerAngles}");
            }
        }

        int index = 0;
        int prevIndex = -1;
        Color prevColor = new Color();
        GameObject prevSphere = null;
        Quaternion prevRotation = Quaternion.identity;
        float t = 0f;


        void Update()
        {
            //foreach (var keyValuePair in boneDict)
            //{
            //    keyValuePair.Value.transform.position = keyValuePair.Key.position;
            //}

            //if (maidBody != null)
            //{
            //    maidBody.boLockHeadAndEye = true;
            //}

            if (maidBody != null)
            {
                maidBody.OnLateUpdateEnd = (Action)Delegate.Combine(maidBody.OnLateUpdateEnd, new Action(UpdateMotion));
            }

            for (var i = 0; i < boneObjects.Count; i++)
            {
                var sphere = boneObjects[i];
                var bone = maidTransforms[i];
                sphere.Value.transform.position = bone.transform.position - 2.0f * Vector3.left;
                sphere.Value.transform.rotation = bone.transform.rotation;
            }

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

            if(Input.GetKeyDown(KeyCode.Keypad7))
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