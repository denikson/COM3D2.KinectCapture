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

        Dictionary<BodyJointType, BodyJointType> immediateChildrenDict = new Dictionary<BodyJointType, BodyJointType>
        {
            [BodyJointType.SpineBase] = BodyJointType.SpineMid,
            [BodyJointType.SpineMid] = BodyJointType.SpineShoulder,
            [BodyJointType.SpineShoulder] = BodyJointType.Neck,
            [BodyJointType.Neck] = BodyJointType.Head,
            //[BodyJointType.Head] = None,


            [BodyJointType.ShoulderRight] = BodyJointType.ElbowRight,
            [BodyJointType.ElbowRight] = BodyJointType.WristRight,
            [BodyJointType.WristRight] = BodyJointType.HandRight,
            [BodyJointType.HandRight] = BodyJointType.HandTipRight,
            //[BodyJointType.ThumbRight] = BodyJointType.WristRight,
            //[BodyJointType.HandTipRight] = BodyJointType.HandRight,


            [BodyJointType.ShoulderLeft] = BodyJointType.ElbowLeft,
            [BodyJointType.ElbowLeft] = BodyJointType.WristLeft,
            [BodyJointType.WristLeft] = BodyJointType.HandLeft,
            [BodyJointType.HandLeft] = BodyJointType.HandTipLeft,


            [BodyJointType.HipRight] = BodyJointType.KneeRight,
            [BodyJointType.KneeRight] = BodyJointType.AnkleRight,
            [BodyJointType.AnkleRight] = BodyJointType.FootRight,
            //[BodyJointType.FootRight] = BodyJointType.AnkleRight,

            [BodyJointType.HipLeft] = BodyJointType.KneeLeft,
            [BodyJointType.KneeLeft] = BodyJointType.AnkleLeft,
            [BodyJointType.AnkleLeft] = BodyJointType.FootLeft,
        };

        Dictionary<string, BodyJointType> maidBodyToKinectJointsDict = new Dictionary<string, BodyJointType>
        {
            ["Bip01 L Thigh"] = BodyJointType.HipLeft,
            ["Bip01 L Calf"] = BodyJointType.KneeLeft,
            ["Bip01 L Foot"] = BodyJointType.AnkleLeft,
            ["Bip01 R Thigh"] = BodyJointType.HipRight,
            ["Bip01 R Calf"] = BodyJointType.KneeRight,
            ["Bip01 R Foot"] = BodyJointType.AnkleRight,
            //["Bip01 Pelvis"] = BodyJointType.SpineBase,
            ["Bip01 Spine"] = BodyJointType.SpineBase,
            ["Bip01 Spine1"] = BodyJointType.SpineMid,
            //["Bip01 Spine1a"] = BodyJointType.SpineMid,
            //["Bip01 R Clavicle"] = BodyJointType.ShoulderRight,
            ["Bip01 L UpperArm"] = BodyJointType.ShoulderLeft,
            ["Bip01 L Forearm"] = BodyJointType.ElbowLeft,
            ["Bip01 L Hand"] = BodyJointType.HandLeft,
            ["Bip01 Neck"] = BodyJointType.Neck,
            ["Bip01 Head"] = BodyJointType.Head,
            ["Bip01 R UpperArm"] = BodyJointType.ShoulderRight,
            ["Bip01 R Forearm"] = BodyJointType.ElbowRight,
            ["Bip01 R Hand"] = BodyJointType.HandRight,
            //["Bip01 L Clavicle"] = BodyJointType.ShoulderLeft,
        };

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

                Log($"Creating node {boneName}");
                var go = CreateBoneNode(boneName); 
                go.SetActive(true);
                /*GameObject.CreatePrimitive(PrimitiveType.Cube);*/
                //go.GetComponent<Renderer>().material = new Material(Shader.Find("Transparent/Diffuse")) {color = c};
                //go.transform.position = bone.position - 2.0f * Vector3.left;
                //go.transform.localRotation = Quaternion.identity;
                //go.transform.localScale = 0.05f * Vector3.one;
                //go.SetActive(true);

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

        //void LateUpdate()
        //{
        //    if (!trackBones)
        //        return;

        //    var jointInfo = listener.GetNextBodyFrame();

        //    if (jointInfo == null)
        //        return;

        //    foreach (var joint in joints)
        //    {
        //        var o = joint.Value;
        //        if (jointInfo.TryGetValue(joint.Key, out var info))
        //        {
        //            o.SetActive(true);
        //            o.transform.localPosition =
        //                new Vector3(info.Position.X * 1.5f, info.Position.Y * 1.5f, info.Position.Z * 1.5f);
        //            o.transform.localRotation =
        //                new Quaternion(info.Orientation.X, info.Orientation.Y, info.Orientation.Z, info.Orientation.W);
        //            Console.WriteLine($"Rotation quaternion: {o.transform.localRotation}");
        //        }
        //        else
        //            o.SetActive(false);
        //    }
        //}

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

        Dictionary<BodyJointType, Transform> bodyJoints = new Dictionary<BodyJointType, Transform>();
        Dictionary<BodyJointType, Transform> maidJoints = new Dictionary<BodyJointType, Transform>();
        Transform bipJoint = null;

        Dictionary<BodyJointType, Transform> bodyJointGizmos = new Dictionary<BodyJointType, Transform>();

        void InitKinectObject(TBody maidBody, Vector3 startingPosition)
        {
            kinectPosition = new GameObject();
            kinectPosition.transform.position = startingPosition;

            var kinectCube = GameObject.CreatePrimitive(PrimitiveType.Cube);
            kinectCube.transform.SetParent(kinectPosition.transform);
            kinectCube.transform.localPosition = Vector3.zero;
            kinectCube.transform.localScale = 0.1f * Vector3.one;

            foreach (var value in Enum.GetValues(typeof(BodyJointType)))
            {
                var o = new GameObject(); /*GameObject.CreatePrimitive(PrimitiveType.Sphere);*//*new GameObject($"KinectJoint_{value}");*/
                o.transform.SetParent(kinectPosition.transform);
                //o.transform.localScale = 0.01f * Vector3.one;
                o.transform.localPosition = 2f * Vector3.forward;
                var boneDraw = CreateBoneNode($"kinect_{value}");
                boneDraw.transform.SetParent(kinectPosition.transform);
                //boneDraw.transform.localPosition += Vector3.forward * 2f;
                bodyJoints[(BodyJointType) value] = o.transform;
                bodyJointGizmos[(BodyJointType) value] = boneDraw.transform;
            }

            var gizmo = kinectPosition.AddComponent<GizmoRender>();
            gizmo.eAxis = true;
            gizmo.eRotate = true;
            gizmo.offsetScale = 0.5f;
            gizmo.Visible = true;

            foreach (var jointType in maidBodyToKinectJointsDict)
                maidJoints.Add(jointType.Value, maidBody.GetBone(jointType.Key));
            bipJoint = maidBody.GetBone("Bip01");

            foreach (var keyValuePair in maidJoints)
                Log($"{keyValuePair.Value.name}: Local pos: {keyValuePair.Value.localPosition}; Local rot: {keyValuePair.Value.localRotation}");
        }

        float rotationX = 0f;
        float rotationY = 0f;
        float rotationZ = 0f;

        void UpdateMotion()
        {
            if (!trackBones)
                return;

            var jointInfo = listener.GetNextBodyFrame();

            if (jointInfo != null)
            {
                foreach (var bodyJoint in bodyJoints)
                {
                    var jointData = jointInfo[bodyJoint.Key];
                    var jointTransform = bodyJoint.Value;

                    jointTransform.localPosition = new Vector3(jointData.Position.X * 0.9f, jointData.Position.Y * 0.9f, jointData.Position.Z * 0.9f);
                    jointTransform.localRotation = new Quaternion(jointData.Orientation.X, jointData.Orientation.Y, jointData.Orientation.Z, jointData.Orientation.W);
                    //jointTransform.Rotate(rotationX, rotationY, rotationZ, Space.Self);
                }

                foreach (var bodyJoint in bodyJoints)
                {
                    var jointTransform = bodyJoint.Value;

                    if (!immediateChildrenDict.TryGetValue(bodyJoint.Key, out var child))
                        continue;

                    var rotFix = Quaternion.LookRotation(bodyJoints[child].localPosition - jointTransform.localPosition, Vector3.up);
                    jointTransform.localRotation = rotFix * jointTransform.localRotation;

                    //jointTransform.Rotate(rotationX, rotationY, rotationZ, Space.Self);
                }

                foreach (var bodyJoint in bodyJoints)
                {
                    var jointTransform = bodyJoint.Value;
                    var gizmo = bodyJointGizmos[bodyJoint.Key];

                    gizmo.position = jointTransform.position + Vector3.left * 2f;
                    gizmo.rotation = jointTransform.rotation;
                    //jointTransform.Rotate(rotationX, rotationY, rotationZ, Space.Self);
                }
            }

            bipJoint.rotation = bodyJoints[BodyJointType.SpineBase].rotation;
            bipJoint.position = bodyJoints[BodyJointType.SpineBase].position;
            foreach (var maidJoint in maidJoints)
            {
                var jointTransform = bodyJoints[maidJoint.Key];
                var maidJointTransform = maidJoint.Value;

                maidJointTransform.rotation = jointTransform.rotation;
                maidJointTransform.position = jointTransform.position;
            }


            //foreach (var joint in joints)
            //{
            //    var o = joint.Value;
            //    if (jointInfo.TryGetValue(joint.Key, out var info))
            //    {
            //        o.SetActive(true);
            //        o.transform.localPosition =
            //            new Vector3(info.Position.X * 1.5f, info.Position.Y * 1.5f, info.Position.Z * 1.5f);
            //        o.transform.localRotation =
            //            new Quaternion(info.Orientation.X, info.Orientation.Y, info.Orientation.Z, info.Orientation.W);
            //        Console.WriteLine($"Rotation quaternion: {o.transform.localRotation}");
            //    }
            //    else
            //        o.SetActive(false);
            //}

            //if (prevSphere != null)
            //{
            //    t += 100.0f * Time.deltaTime;

            //    if (t > 360f)
            //        t = 0f;

            //    maidTransforms[prevIndex].transform.rotation = Quaternion.Euler(t, t, t);
            //}
        }

        void PrintHierarchy(Transform t, HashSet<string> items)
        {
            if (items.Count == 0)
                return;

            if (items.Contains(t.name))
            {
                Log(t.name);
                items.Remove(t.name);
            }

            for (int i = 0; i < t.childCount; i++)
            {
                PrintHierarchy(t.GetChild(i), items);
            }
        }

        GameObject CreateBoneNode(string name)
        {
            GameObject mainObject = new GameObject($"Bone_{name}");

            GameObject forward = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            forward.transform.localScale = new Vector3(0.01f, 0.05f, 0.01f);
            forward.transform.localPosition = new Vector3(0, 0.05f, 0);
            forward.transform.SetParent(mainObject.transform);
            forward.GetComponent<Renderer>().material = new Material(Shader.Find("Transparent/Diffuse")) { color = Color.green };
            forward.SetActive(true);

            GameObject up = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            up.transform.localScale = new Vector3(0.01f, 0.05f, 0.01f);
            up.transform.localPosition = new Vector3(-0.05f, 0, 0);
            up.transform.localRotation = Quaternion.Euler(0f, 0f, 90f);
            up.transform.SetParent(mainObject.transform);
            up.GetComponent<Renderer>().material = new Material(Shader.Find("Transparent/Diffuse")) { color = Color.red };
            up.SetActive(true);

            GameObject left = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            left.transform.localScale = new Vector3(0.01f, 0.05f, 0.01f);
            left.transform.localPosition = new Vector3(0, 0, 0.05f);
            left.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
            left.transform.SetParent(mainObject.transform);
            left.GetComponent<Renderer>().material = new Material(Shader.Find("Transparent/Diffuse")) { color = Color.blue };
            left.SetActive(true);

            return mainObject;
        }

        private bool startedTrackingBones = false;

        void Update()
        {
            // Add delegate to OnLateUpdateEnd in maid body to override all transforms
            if (maidBody != null)
                maidBody.OnLateUpdateEnd =
                    (Action) Delegate.Combine(maidBody.OnLateUpdateEnd, new Action(UpdateMotion));

            // Track the bone
            if (!startedTrackingBones)
            {
                for (var i = 0; i < boneObjects.Count; i++)
                {
                    var sphere = boneObjects[i];
                    var bone = maidTransforms[i];
                    sphere.Value.transform.position = bone.transform.position - 2.0f * Vector3.left;
                    sphere.Value.transform.rotation = bone.transform.rotation;

                    //if (bone.name == "Bip01 Spine")
                    //{
                    //    var rot = bone.transform.rotation;
                    //    Log($"Bip01 Spine quaternion: {rot} with angles: {rot.eulerAngles}");
                    //}
                }
            }
            

            //// Highlight one of the bones
            //if (Input.GetKeyDown(KeyCode.Keypad9))
            //{
            //    var sphere = boneObjects[index];
            //    if (prevSphere != null)
            //    {
            //        maidTransforms[prevIndex].transform.rotation = prevRotation;
            //        prevSphere.transform.localScale = Vector3.one * 0.05f;
            //        prevSphere.GetComponent<Renderer>().material.color = prevColor;
            //    }

            //    Log($"Highlighting {sphere.Key}");

            //    prevSphere = sphere.Value;
            //    sphere.Value.transform.localScale = Vector3.one * 0.08f;
            //    prevColor = prevSphere.GetComponent<Renderer>().material.color;
            //    prevSphere.GetComponent<Renderer>().material.color = new Color(1f, 1f, 1f, 0.5f);
            //    prevRotation = maidTransforms[index].transform.rotation;
            //    prevIndex = index;
            //    index = (index + 1) % boneObjects.Count;
            //}

            // Create custom objects of the bones
            if (Input.GetKeyDown(KeyCode.Keypad9))
            {
                var maid = FindObjectOfType<Maid>();

                if (maid == null)
                {
                    Log("No maid!");
                    return;
                }

                var pos = maid.transform.position;

                var bones = CreateBoneNode("testBone");
                bones.transform.position = pos + 2f * Vector3.forward;
            }
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
                maidBody = maid.body0;
                Log($"Found maid at {pos}");

                //var set = new HashSet<string>(maidBodyToKinectJointsDict.Keys);
                //PrintHierarchy(maid.transform, set);

                InitKinectObject(maidBody, pos - Vector3.back * 1.0f + Vector3.up * 1.0f);

                listener.KinectService.ListenBoneData();
                trackBones = true;
                startedTrackingBones = true;
            }

            if (Input.GetMouseButtonDown(3))
            {
                if (!trackBones)
                    return;

                Log($"Stopping bone tracking");
                trackBones = false;
                listener.KinectService.StopListeningBoneData();
            }

            if (Input.GetMouseButtonDown(4))
            {
                if (trackBones)
                    return;
                Log($"Restarting bone tracking");
                listener.KinectService.ListenBoneData();
                trackBones = true;
            }

            if (Input.GetKeyDown(KeyCode.Keypad8))
                listener.KinectService.StopListeningBoneData();

            if (Input.GetKeyDown(KeyCode.I))
            {
                rotationX += 10f;
                Log($"Rotation X: {rotationX}");
            }
            if (Input.GetKeyDown(KeyCode.K))
            {
                rotationX -= 10f;
                Log($"Rotation X: {rotationX}");
            }
            if (Input.GetKeyDown(KeyCode.J))
            {
                rotationY += 10f;
                Log($"Rotation Y: {rotationY}");
            }
            if (Input.GetKeyDown(KeyCode.L))
            {
                rotationY -= 10f;
                Log($"Rotation Y: {rotationY}");
            }
            if (Input.GetKeyDown(KeyCode.O))
            {
                rotationZ += 10f;
                Log($"Rotation Z: {rotationZ}");
            }
            if (Input.GetKeyDown(KeyCode.U))
            {
                rotationZ -= 10f;
                Log($"Rotation Z: {rotationZ}");
            }
        }


    }
}