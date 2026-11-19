using System.Collections.Generic;
using Cinemachine;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

// Stages > Setup > Build Cutscene Rig In Open Scene
//
// Creates (with Undo):
//   Stages            StageManager + "Cutscene" and "Gameplay Loop" stages, wired to the scene
//   Cutscene_Rig      Cinemachine cameras, walk path (bed -> window), look points,
//                     eyes effect, light blackout, placeholder rabbit
//
// Positions are guessed from C_Bed_1, C_Window and VCam_Window. Adjust them in the Scene view:
// select CS_WalkPath to drag its waypoints, move the CS_Look_* points to aim the camera.
public static class CutsceneRigBuilder
{
    private const string RigName = "Cutscene_Rig";
    private const string StagesName = "Stages";
    private const string NoisePath = "Packages/com.unity.cinemachine/Presets/Noise/Handheld_normal_mild.asset";
    private const string PlaceholderMaterialPath = "Assets/Scripts/Cutscene/Rabbit_Placeholder.mat";

    [MenuItem("Stages/Setup/Build Cutscene Rig In Open Scene", false, 60)]
    private static void Build()
    {
        if (EditorApplication.isPlaying)
        {
            EditorUtility.DisplayDialog("Build Cutscene Rig", "Leave play mode first.", "OK");
            return;
        }

        GameObject oldRig = FindInScene(RigName);
        GameObject oldStages = FindInScene(StagesName);

        if (oldRig != null || oldStages != null)
        {
            if (!EditorUtility.DisplayDialog("Build Cutscene Rig",
                    "A Cutscene_Rig / Stages object already exists. Delete and rebuild it? " +
                    "(Any positions you adjusted will be lost.)", "Rebuild", "Cancel"))
            {
                return;
            }
        }

        Undo.SetCurrentGroupName("Build Cutscene Rig");
        int undoGroup = Undo.GetCurrentGroup();

        if (oldRig != null) Undo.DestroyObjectImmediate(oldRig);
        if (oldStages != null) Undo.DestroyObjectImmediate(oldStages);

        // ------------------------------------------------------------ anchor points from the scene

        Transform bed = FindInScene("C_Bed_1") != null ? FindInScene("C_Bed_1").transform : null;
        Transform window = FindInScene("C_Window") != null ? FindInScene("C_Window").transform : null;
        Transform windowView = FindInScene("VCam_Window") != null ? FindInScene("VCam_Window").transform : null;

        Vector3 bedPos = bed != null ? bed.position : new Vector3(-1.31f, 0.61f, -2.37f);
        Vector3 windowPos = window != null ? window.position : new Vector3(0.2f, 1.38f, -0.93f);
        Vector3 atWindow = windowView != null ? windowView.position : windowPos + new Vector3(-0.6f, 0.15f, 0f);

        float eyeHeight = atWindow.y;
        Vector3 outDir = Flat(windowPos - atWindow, Vector3.right);   // from the room out through the window
        Vector3 side = Vector3.Cross(Vector3.up, outDir).normalized;
        Vector3 bedToWindow = Flat(atWindow - bedPos, outDir);

        Vector3 lying = bedPos + Vector3.up * 0.3f;
        Vector3 sitting = lying + Vector3.up * 0.5f + bedToWindow * 0.1f;
        Vector3 standing = bedPos + bedToWindow * 0.7f;
        standing.y = eyeHeight;
        Vector3 mid = Vector3.Lerp(standing, atWindow, 0.5f);
        mid.y = eyeHeight;

        // ------------------------------------------------------------ rig root

        GameObject rig = new GameObject(RigName);
        Undo.RegisterCreatedObjectUndo(rig, "Build Cutscene Rig");

        Transform lookTarget = Point("CS_LookTarget", rig.transform, lying + Vector3.up * 2f);

        Transform lookRoot = new GameObject("CS_LookPoints").transform;
        lookRoot.SetParent(rig.transform, false);
        Transform[] wakeLook =
        {
            Point("CS_Look_1_Ceiling", lookRoot, lying + Vector3.up * 2f + bedToWindow * 0.3f),
            Point("CS_Look_2_Left", lookRoot, lying + Vector3.up * 0.9f + side * 2.5f),
            Point("CS_Look_3_Right", lookRoot, lying + Vector3.up * 0.9f - side * 2.5f),
            Point("CS_Look_4_Room", lookRoot, lying + bedToWindow * 3f + Vector3.up * 0.7f),
        };
        Transform getUpLook = Point("CS_Look_5_GetUp", lookRoot, standing + bedToWindow * 3f - Vector3.up * 0.15f);
        Transform windowLook = Point("CS_Look_6_Window", lookRoot, windowPos + outDir * 3f - Vector3.up * 0.3f);

        // Walk path: lying -> sitting -> standing -> middle of the room -> window.
        GameObject pathGO = new GameObject("CS_WalkPath");
        pathGO.transform.SetParent(rig.transform, false);
        CinemachineSmoothPath path = pathGO.AddComponent<CinemachineSmoothPath>();
        Vector3[] points = { lying, sitting, standing, mid, atWindow };
        path.m_Waypoints = new CinemachineSmoothPath.Waypoint[points.Length];
        for (int i = 0; i < points.Length; i++)
        {
            path.m_Waypoints[i] = new CinemachineSmoothPath.Waypoint { position = points[i], roll = 0f };
        }
        path.m_Looped = false;
        path.InvalidateDistanceCache();

        float d1 = Vector3.Distance(lying, sitting);
        float d2 = Vector3.Distance(sitting, standing);
        float total = d1 + d2 + Vector3.Distance(standing, mid) + Vector3.Distance(mid, atWindow);

        NoiseSettings noise = FindNoiseProfile();
        if (noise == null)
        {
            Debug.LogWarning("[Cutscene] Couldn't find the Cinemachine noise preset at " + NoisePath +
                             ". Assign a Noise Profile on the CS cameras by hand for breathing/head movement.");
        }

        // Cameras.
        CinemachineVirtualCamera bodyCam = MakeCam("CS_BodyCam", rig.transform, lying, 60f, noise, 0.25f);
        CinemachineTrackedDolly dolly = bodyCam.AddCinemachineComponent<CinemachineTrackedDolly>();
        dolly.m_Path = path;
        dolly.m_PositionUnits = CinemachinePathBase.PositionUnits.Normalized;
        dolly.m_PathPosition = 0f;
        dolly.m_XDamping = 0.2f;
        dolly.m_YDamping = 0.2f;
        dolly.m_ZDamping = 0.2f;
        dolly.m_CameraUp = CinemachineTrackedDolly.CameraUpMode.Default;
        bodyCam.LookAt = lookTarget;

        CinemachineVirtualCamera windowCam = MakeCam("CS_WindowCam", rig.transform, atWindow, 55f, noise, 0.3f);
        CinemachineVirtualCamera stepBackCam = MakeCam("CS_StepBackCam", rig.transform,
            atWindow - outDir * 0.9f - Vector3.up * 0.05f, 62f, noise, 0.6f);
        stepBackCam.LookAt = windowLook;

        // Eyes and lights.
        GameObject eyesGO = new GameObject("CS_Eyes");
        eyesGO.transform.SetParent(rig.transform, false);
        CutsceneEyes eyes = eyesGO.AddComponent<CutsceneEyes>();

        GameObject lightsGO = new GameObject("CS_Lights");
        lightsGO.transform.SetParent(rig.transform, false);
        LightBlackout lights = lightsGO.AddComponent<LightBlackout>();

        // Rabbit: in the field, side-on to the window so its head-turn reads well.
        Vector3 rabbitPos = windowPos + outDir * 10f;
        rabbitPos.y = windowPos.y - 1.4f;
        GameObject rabbitGO = BuildPlaceholderRabbit(rig.transform, rabbitPos, Quaternion.LookRotation(side, Vector3.up));
        RabbitActor rabbit = rabbitGO.GetComponent<RabbitActor>();
        windowCam.LookAt = rabbitGO.transform;

        Vector3 runTargetPos = windowPos + outDir * 1.0f;
        runTargetPos.y = windowPos.y - 1.2f;
        Transform runTarget = Point("CS_RabbitRunTarget", rig.transform, runTargetPos);

        // ------------------------------------------------------------ stages

        GameObject stagesGO = new GameObject(StagesName);
        Undo.RegisterCreatedObjectUndo(stagesGO, "Build Cutscene Rig");
        StageManager manager = stagesGO.AddComponent<StageManager>();

        GameObject cutsceneGO = new GameObject("Stage_Cutscene");
        cutsceneGO.transform.SetParent(stagesGO.transform, false);
        CutsceneStage cutscene = cutsceneGO.AddComponent<CutsceneStage>();
        SetStageName(cutscene, StageMenu.Cutscene);

        GameObject gameplayGO = new GameObject("Stage_GameplayLoop");
        gameplayGO.transform.SetParent(stagesGO.transform, false);
        GameplayLoopStage gameplay = gameplayGO.AddComponent<GameplayLoopStage>();
        SetStageName(gameplay, StageMenu.GameplayLoop);

        manager.stages = new List<GameStage> { cutscene, gameplay };

        cutscene.brain = Object.FindFirstObjectByType<CinemachineBrain>();
        cutscene.bodyCam = bodyCam;
        cutscene.windowCam = windowCam;
        cutscene.stepBackCam = stepBackCam;
        cutscene.walkPath = path;
        cutscene.lookTarget = lookTarget;
        cutscene.wakeLookPoints = wakeLook;
        cutscene.getUpLookPoint = getUpLook;
        cutscene.windowLookPoint = windowLook;
        cutscene.eyes = eyes;
        cutscene.lights = lights;
        cutscene.rabbit = rabbit;
        cutscene.rabbitRunTarget = runTarget;

        if (total > 0.01f)
        {
            cutscene.sitUpPathPosition = Mathf.Clamp01(d1 / total);
            cutscene.standPathPosition = Mathf.Clamp01((d1 + d2) / total);
        }

        // ------------------------------------------------------------ gameplay wiring

        List<string> notes = new List<string>();

        CursorCameraManager input = Object.FindFirstObjectByType<CursorCameraManager>(FindObjectsInactive.Include);
        if (input != null)
        {
            cutscene.disableOnEnter = new Behaviour[] { input };
            gameplay.enableOnEnter = new Behaviour[] { input };
            notes.Add("CursorCameraManager (clicking) is off during the cutscene, on in the Gameplay Loop.");
        }

        GameObject blinking = FindInScene("Canvas_Blinking");
        if (blinking != null)
        {
            cutscene.deactivateOnEnter = new[] { blinking };
            notes.Add("Canvas_Blinking (the old EyelidBlink) is switched off: the cutscene's eyes replace it.");
        }

        List<GameObject> roaming = new List<GameObject>();
        foreach (Test_InteractiveSoundScript s in Object.FindObjectsByType<Test_InteractiveSoundScript>(
                     FindObjectsInactive.Include, FindObjectsSortMode.None))
        {
            Undo.RecordObject(s.gameObject, "Build Cutscene Rig");
            s.gameObject.SetActive(false);
            roaming.Add(s.gameObject);
        }
        if (roaming.Count > 0)
        {
            gameplay.activateOnEnter = roaming.ToArray();
            notes.Add(roaming.Count + " roaming sound(s) (e.g. S_Interactive_4) start disabled and appear when the Gameplay Loop starts.");
        }

        if (cutscene.brain == null)
        {
            notes.Add("WARNING: no CinemachineBrain found. Assign Brain on Stage_Cutscene.");
        }

        Undo.CollapseUndoOperations(undoGroup);
        EditorSceneManager.MarkSceneDirty(stagesGO.scene);
        Selection.activeGameObject = rig;

        Debug.Log("[Stages] Cutscene rig built in '" + stagesGO.scene.name + "'.\n- " + string.Join("\n- ", notes) +
                  "\nNext: adjust CS_WalkPath, the CS_Look_* points and CS_WindowCam in the Scene view, then save the scene.");
    }

    // Stages > Setup > Use Selected Model As Rabbit
    // Select the imported rabbit model (Project window or scene), then click this.
    // It goes under a new "Rabbit" object at the placeholder's spot; the placeholder is switched off.
    // Rotate / scale the model (the child) so it stands on the ground facing the blue Z arrow of "Rabbit".
    [MenuItem("Stages/Setup/Use Selected Model As Rabbit", false, 61)]
    private static void UseSelectedModelAsRabbit()
    {
        GameObject model = Selection.activeGameObject;
        CutsceneStage cutscene = Object.FindFirstObjectByType<CutsceneStage>(FindObjectsInactive.Include);

        if (model == null)
        {
            EditorUtility.DisplayDialog("Use Selected Model As Rabbit",
                "Select the imported rabbit model first (e.g. Assets/Models/Rabbit/Rabbit_Cutscene_Anim in the Project window).", "OK");
            return;
        }

        if (cutscene == null)
        {
            EditorUtility.DisplayDialog("Use Selected Model As Rabbit",
                "No cutscene in this scene. Use Stages > Setup > Build Cutscene Rig In Open Scene first.", "OK");
            return;
        }

        Undo.SetCurrentGroupName("Use Selected Model As Rabbit");
        int undoGroup = Undo.GetCurrentGroup();

        RabbitActor old = cutscene.rabbit;
        Transform parent = old != null ? old.transform.parent : cutscene.transform;

        GameObject wrapper = new GameObject("Rabbit");
        Undo.RegisterCreatedObjectUndo(wrapper, "Use Selected Model As Rabbit");
        wrapper.transform.SetParent(parent, false);
        if (old != null) wrapper.transform.SetPositionAndRotation(old.transform.position, old.transform.rotation);

        GameObject instance;
        if (EditorUtility.IsPersistent(model))
        {
            instance = (GameObject)PrefabUtility.InstantiatePrefab(model);
            Undo.RegisterCreatedObjectUndo(instance, "Use Selected Model As Rabbit");
            instance.transform.SetParent(wrapper.transform, false);
            instance.transform.localPosition = Vector3.zero;
            instance.transform.localRotation = Quaternion.identity;
        }
        else
        {
            instance = model;
            Undo.SetTransformParent(instance.transform, wrapper.transform, "Use Selected Model As Rabbit");
            instance.transform.localPosition = Vector3.zero;
        }

        RabbitActor actor = Undo.AddComponent<RabbitActor>(wrapper);
        actor.AutoFindBones();

        if (old != null)
        {
            actor.screamEvent = old.screamEvent;
            Undo.RecordObject(old.gameObject, "Use Selected Model As Rabbit");
            old.gameObject.SetActive(false);
        }

        Undo.RecordObject(cutscene, "Use Selected Model As Rabbit");
        cutscene.rabbit = actor;

        if (cutscene.windowCam != null)
        {
            Undo.RecordObject(cutscene.windowCam, "Use Selected Model As Rabbit");
            cutscene.windowCam.LookAt = wrapper.transform;
        }

        Undo.CollapseUndoOperations(undoGroup);
        EditorSceneManager.MarkSceneDirty(wrapper.scene);
        Selection.activeGameObject = wrapper;

        Debug.Log("[Cutscene] Rabbit model in place. Bones found: " +
                  "body=" + Name(actor.body) + ", neck=" + Name(actor.neck) + ", head=" + Name(actor.head) +
                  ", jaw=" + Name(actor.jaw) + ", ears=" + Name(actor.earLeft) + "/" + Name(actor.earRight) +
                  ", front legs=" + actor.frontLegs.Length + ", back legs=" + actor.backLegs.Length +
                  ".\nRotate/scale the child '" + instance.name + "' so the rabbit stands on the ground facing Rabbit's blue Z arrow. " +
                  "Then try the poses in play mode: right-click RabbitActor > Preview.");
    }

    private static string Name(Transform t)
    {
        return t != null ? t.name : "(none)";
    }

    // ---------------------------------------------------------------- helpers

    private static CinemachineVirtualCamera MakeCam(string name, Transform parent, Vector3 position, float fov,
                                                    NoiseSettings noise, float amplitude)
    {
        GameObject go = new GameObject(name);
        go.transform.SetParent(parent, false);
        go.transform.position = position;

        CinemachineVirtualCamera cam = go.AddComponent<CinemachineVirtualCamera>();
        cam.Priority = 0;
        cam.m_Lens.FieldOfView = fov;
        cam.m_Lens.NearClipPlane = 0.05f;

        CinemachineComposer composer = cam.AddCinemachineComponent<CinemachineComposer>();
        composer.m_TrackedObjectOffset = new Vector3(0f, 0.12f, 0f);
        composer.m_LookaheadTime = 0f;
        composer.m_HorizontalDamping = 0.8f;
        composer.m_VerticalDamping = 0.8f;
        composer.m_DeadZoneWidth = 0f;
        composer.m_DeadZoneHeight = 0f;

        CinemachineBasicMultiChannelPerlin perlin = cam.AddCinemachineComponent<CinemachineBasicMultiChannelPerlin>();
        perlin.m_NoiseProfile = noise;
        perlin.m_AmplitudeGain = amplitude;
        perlin.m_FrequencyGain = 0.5f;

        return cam;
    }

    // Cinemachine's "Handheld_normal_mild" preset (breathing / head movement), wherever it is.
    private static NoiseSettings FindNoiseProfile()
    {
        NoiseSettings noise = AssetDatabase.LoadAssetAtPath<NoiseSettings>(NoisePath);
        if (noise != null) return noise;

        string[] guids = AssetDatabase.FindAssets("Handheld_normal_mild t:NoiseSettings");
        if (guids.Length == 0) guids = AssetDatabase.FindAssets("t:NoiseSettings");

        return guids.Length > 0
            ? AssetDatabase.LoadAssetAtPath<NoiseSettings>(AssetDatabase.GUIDToAssetPath(guids[0]))
            : null;
    }

    private static Transform Point(string name, Transform parent, Vector3 position)
    {
        GameObject go = new GameObject(name);
        go.transform.SetParent(parent, false);
        go.transform.position = position;
        return go.transform;
    }

    private static Vector3 Flat(Vector3 v, Vector3 fallback)
    {
        v.y = 0f;
        return v.sqrMagnitude > 0.0001f ? v.normalized : fallback;
    }

    private static void SetStageName(GameStage stage, string stageName)
    {
        SerializedObject so = new SerializedObject(stage);
        so.FindProperty("stageName").stringValue = stageName;
        so.ApplyModifiedPropertiesWithoutUndo();
    }

    private static GameObject FindInScene(string name)
    {
        foreach (GameObject root in EditorSceneManager.GetActiveScene().GetRootGameObjects())
        {
            if (root.name == name) return root;

            foreach (Transform t in root.GetComponentsInChildren<Transform>(true))
            {
                if (t.name == name) return t.gameObject;
            }
        }

        return null;
    }

    // A simple stand-in rabbit made of spheres and capsules, with "bones" named like a real rig,
    // so the cutscene works before the real model is imported.
    private static GameObject BuildPlaceholderRabbit(Transform parent, Vector3 position, Quaternion rotation)
    {
        Material material = AssetDatabase.LoadAssetAtPath<Material>(PlaceholderMaterialPath);
        if (material == null)
        {
            Shader shader = Shader.Find("HDRP/Lit");
            if (shader != null)
            {
                material = new Material(shader);
                material.SetColor("_BaseColor", new Color(0.42f, 0.35f, 0.28f));
                AssetDatabase.CreateAsset(material, PlaceholderMaterialPath);
            }
        }

        GameObject root = new GameObject("Rabbit (placeholder)");
        root.transform.SetParent(parent, false);
        root.transform.SetPositionAndRotation(position, rotation);

        Transform body = Bone("Body", root.transform, new Vector3(0f, 0.16f, 0f));
        AddMesh(PrimitiveType.Sphere, body, Vector3.zero, new Vector3(0.28f, 0.24f, 0.38f), material);

        Transform neck = Bone("Neck", body, new Vector3(0f, 0.08f, 0.15f));
        Transform head = Bone("Head", neck, new Vector3(0f, 0.05f, 0.05f));
        AddMesh(PrimitiveType.Sphere, head, new Vector3(0f, 0.03f, 0.07f), new Vector3(0.17f, 0.16f, 0.2f), material);

        Transform jaw = Bone("Jaw", head, new Vector3(0f, -0.02f, 0.08f));
        AddMesh(PrimitiveType.Cube, jaw, new Vector3(0f, -0.015f, 0.04f), new Vector3(0.08f, 0.03f, 0.08f), material);

        Transform earL = Bone("Ear_L", head, new Vector3(-0.04f, 0.09f, 0.03f));
        AddMesh(PrimitiveType.Capsule, earL, new Vector3(0f, 0.09f, 0f), new Vector3(0.04f, 0.09f, 0.02f), material);
        Transform earR = Bone("Ear_R", head, new Vector3(0.04f, 0.09f, 0.03f));
        AddMesh(PrimitiveType.Capsule, earR, new Vector3(0f, 0.09f, 0f), new Vector3(0.04f, 0.09f, 0.02f), material);

        Transform[] front =
        {
            Bone("FrontLeg_L", body, new Vector3(-0.07f, -0.06f, 0.13f)),
            Bone("FrontLeg_R", body, new Vector3(0.07f, -0.06f, 0.13f)),
        };
        Transform[] back =
        {
            Bone("BackLeg_L", body, new Vector3(-0.1f, -0.04f, -0.12f)),
            Bone("BackLeg_R", body, new Vector3(0.1f, -0.04f, -0.12f)),
        };
        foreach (Transform leg in front) AddMesh(PrimitiveType.Capsule, leg, new Vector3(0f, -0.06f, 0f), new Vector3(0.05f, 0.06f, 0.05f), material);
        foreach (Transform leg in back) AddMesh(PrimitiveType.Capsule, leg, new Vector3(0f, -0.06f, 0.02f), new Vector3(0.08f, 0.07f, 0.12f), material);

        RabbitActor actor = root.AddComponent<RabbitActor>();
        actor.body = body;
        actor.neck = neck;
        actor.head = head;
        actor.jaw = jaw;
        actor.earLeft = earL;
        actor.earRight = earR;
        actor.frontLegs = front;
        actor.backLegs = back;
        actor.earAxis = Vector3.left; // positive = ears fold back on this placeholder

        return root;
    }

    private static Transform Bone(string name, Transform parent, Vector3 localPosition)
    {
        Transform t = new GameObject(name).transform;
        t.SetParent(parent, false);
        t.localPosition = localPosition;
        return t;
    }

    private static void AddMesh(PrimitiveType type, Transform parent, Vector3 localPosition, Vector3 scale, Material material)
    {
        GameObject go = GameObject.CreatePrimitive(type);
        go.name = "Mesh";
        Object.DestroyImmediate(go.GetComponent<Collider>());
        go.transform.SetParent(parent, false);
        go.transform.localPosition = localPosition;
        go.transform.localScale = scale;
        if (material != null) go.GetComponent<Renderer>().sharedMaterial = material;
    }
}
