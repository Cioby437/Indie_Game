using System.Collections;
using System.Collections.Generic;
using FMODUnity;
using UnityEngine;
using UnityEngine.Events;

// The rabbit in the field, animated in code on top of its rig's rest pose.
//
// Works with the placeholder rabbit and with the real rigged one: bones are found by name
// (head, jaw, ear, leg, spine...) or assigned by hand. If a bone turns the wrong way,
// flip its axis in the Inspector (e.g. (1,0,0) -> (-1,0,0)), or try (0,0,1).
// Right-click the component > "Preview: ..." in play mode to test each pose.
//
// The cutscene calls: StartEating() -> LookAhead() -> Scream() -> SnapLookAt() -> RunTo() -> Hide()
public class RabbitActor : MonoBehaviour
{
    [Header("Bones (found by name when empty)")]
    public Transform body;
    public Transform neck;
    public Transform head;
    public Transform jaw;
    public Transform earLeft;
    public Transform earRight;
    public Transform[] frontLegs = new Transform[0];
    public Transform[] backLegs = new Transform[0];

    [Header("Bone rotation axes (local space)")]
    [Tooltip("Positive angle = head down.")]
    public Vector3 headPitchAxis = Vector3.right;
    [Tooltip("Positive angle = mouth open.")]
    public Vector3 jawOpenAxis = Vector3.right;
    [Tooltip("Positive angle = ears back.")]
    public Vector3 earAxis = Vector3.right;
    [Tooltip("Positive angle = legs swing forward.")]
    public Vector3 legSwingAxis = Vector3.right;
    [Tooltip("Positive angle = body nose-down.")]
    public Vector3 bodyPitchAxis = Vector3.right;

    [Tooltip("Snap the bones to the mesh's bind pose (its undeformed shape) on start. " +
             "Needed for imported models that were saved mid-animation, like the Lapifors rabbit.")]
    public bool resetToBindPose = true;

    [Tooltip("If the model doesn't face its local +Z, the yaw (degrees) to correct it. " +
             "Easier: rotate the model's child object instead.")]
    public float modelYawOffset = 0f;

    [Header("Animator (optional)")]
    [Tooltip("If set, these Animator states are played (e.g. eat / run segments cut from the 'Main' clip), " +
             "and the code adds the head, jaw, ear and snap moves on top.")]
    public Animator animator;
    public string eatState = "";
    public string idleState = "";
    public string runState = "";
    public float crossFade = 0.2f;

    [Header("Eating")]
    public float eatingHeadPitch = 40f;
    public float chewJawAngle = 7f;
    public float chewSpeed = 9f;
    public float nibbleNod = 4f;

    [Header("Alert / scream")]
    public float alertHeadPitch = -8f;
    public float alertEarAngle = -15f;
    public float screamJawAngle = 40f;
    public float screamHeadPitch = -22f;
    public float screamTremble = 2.5f;

    [Header("Run")]
    public float runSpeed = 9f;
    public float runAcceleration = 30f;
    public float hopFrequency = 5f;
    public float hopHeight = 0.22f;
    public float legSwingAngle = 50f;
    public float bodyPitchAngle = 14f;
    public float runEarAngle = 35f;
    public float stopDistance = 0.4f;

    [Header("Ground")]
    public bool snapToGround = true;
    public float groundOffset = 0f;

    [Header("Sound & events")]
    [Tooltip("Optional FMOD event played when it screams (add it later).")]
    public EventReference screamEvent;
    public UnityEvent onScream;
    public UnityEvent onStartRun;
    public UnityEvent onReachedTarget;

    private enum Mode { Idle, Eating, Alert, Screaming, Running }

    private Mode mode = Mode.Idle;
    private readonly Dictionary<Transform, Quaternion> rest = new Dictionary<Transform, Quaternion>();

    // Animated values (degrees).
    private float headPitch, jawAngle, earAngle, bodyPitch, legSwing, hop;
    private float runPhase, speed;
    private Vector3 startPosition;
    private Quaternion startRotation;
    private Renderer[] renderers;

    // ---------------------------------------------------------------- setup

    private void Awake()
    {
        if (resetToBindPose)
        {
            ResetToBindPose();
        }

        AutoFindBones();
        CaptureRest();

        startPosition = transform.position;
        startRotation = transform.rotation;
        renderers = GetComponentsInChildren<Renderer>(true);

        if (snapToGround)
        {
            SnapToGround(0f);
        }
    }

    // Puts every bone of the skinned mesh back where it was when the mesh was bound to the rig,
    // so the mesh shows its modelled shape. Right-click the component to do it in edit mode too.
    [ContextMenu("Reset Bones To Bind Pose")]
    public void ResetToBindPose()
    {
        foreach (SkinnedMeshRenderer smr in GetComponentsInChildren<SkinnedMeshRenderer>(true))
        {
            Mesh mesh = smr.sharedMesh;
            Transform[] bones = smr.bones;

            if (mesh == null || bones == null || mesh.bindposes.Length != bones.Length)
            {
                continue;
            }

            Matrix4x4[] bindposes = mesh.bindposes;
            Matrix4x4 meshToWorld = smr.transform.localToWorldMatrix;

            // Parents before children, so moving a parent doesn't undo its child.
            int[] order = new int[bones.Length];
            for (int i = 0; i < order.Length; i++) order[i] = i;
            System.Array.Sort(order, (a, b) => Depth(bones[a]).CompareTo(Depth(bones[b])));

            foreach (int i in order)
            {
                if (bones[i] == null) continue;

#if UNITY_EDITOR
                if (!Application.isPlaying) UnityEditor.Undo.RecordObject(bones[i], "Reset Bones To Bind Pose");
#endif
                Matrix4x4 world = meshToWorld * bindposes[i].inverse;
                bones[i].SetPositionAndRotation(world.GetPosition(), world.rotation);
            }
        }
    }

    private static int Depth(Transform t)
    {
        int d = 0;
        for (Transform p = t; p != null; p = p.parent) d++;
        return d;
    }

    [ContextMenu("Find Bones By Name")]
    public void AutoFindBones()
    {
        Transform[] all = GetComponentsInChildren<Transform>(true);

        // Bone names of the Lapifors rabbit rig (Rabbit_Cutscene_Anim.blend).
        body = body != null ? body : Exact(all, "Body_Spine1");
        neck = neck != null ? neck : Exact(all, "Body_Neck");
        head = head != null ? head : Exact(all, "Body_Head");
        jaw = jaw != null ? jaw : Exact(all, "Body_headbot");
        earLeft = earLeft != null ? earLeft : Exact(all, "Body_EarL");
        earRight = earRight != null ? earRight : Exact(all, "Body_EarR");

        if (frontLegs.Length == 0 && Exact(all, "Body_Leftarm") != null)
        {
            frontLegs = new[] { Exact(all, "Body_Leftarm"), Exact(all, "Body_Rightarm") };
        }

        if (backLegs.Length == 0 && Exact(all, "Body_leftthigh") != null)
        {
            backLegs = new[] { Exact(all, "Body_leftthigh"), Exact(all, "Body_Rightthigh") };
        }

        if (animator == null)
        {
            animator = GetComponentInChildren<Animator>();
        }

        // Generic search for other rigs.
        head = head != null ? head : FindBone(all, "head");
        neck = neck != null ? neck : FindBone(all, "neck");
        jaw = jaw != null ? jaw : FindBone(all, "jaw", "mouth", "chin");
        body = body != null ? body : FindBone(all, "spine", "body", "torso", "chest");

        if (earLeft == null || earRight == null)
        {
            List<Transform> ears = FindAll(all, "ear");
            foreach (Transform e in ears)
            {
                string n = e.name.ToLowerInvariant();
                if (earLeft == null && (n.EndsWith("l") || n.Contains("left") || n.Contains(".l") || n.Contains("_l"))) earLeft = e;
                else if (earRight == null && (n.EndsWith("r") || n.Contains("right") || n.Contains(".r") || n.Contains("_r"))) earRight = e;
            }
        }

        if (frontLegs.Length == 0)
        {
            frontLegs = TopMost(FindAll(all, "front", "fore", "arm", "hand")).ToArray();
        }

        if (backLegs.Length == 0)
        {
            List<Transform> legs = FindAll(all, "back", "hind", "thigh", "leg", "foot", "shin");
            legs.RemoveAll(t => System.Array.IndexOf(frontLegs, t) >= 0 || IsUnder(t, frontLegs));
            backLegs = TopMost(legs).ToArray();
        }
    }

    private void CaptureRest()
    {
        rest.Clear();
        foreach (Transform t in Bones())
        {
            if (t != null && !rest.ContainsKey(t)) rest[t] = t.localRotation;
        }
    }

    private IEnumerable<Transform> Bones()
    {
        yield return body;
        yield return neck;
        yield return head;
        yield return jaw;
        yield return earLeft;
        yield return earRight;
        foreach (Transform t in frontLegs) yield return t;
        foreach (Transform t in backLegs) yield return t;
    }

    // ---------------------------------------------------------------- actions (called by the cutscene)

    public void ResetToStart()
    {
        StopAllCoroutines();
        Show();
        transform.SetPositionAndRotation(startPosition, startRotation);
        speed = 0f;
        hop = 0f;
        if (snapToGround) SnapToGround(0f);
        mode = Mode.Idle;
    }

    public void StartEating()
    {
        mode = Mode.Eating;
        PlayState(eatState);
    }

    // Stops eating and lifts its head, looking straight ahead.
    public void LookAhead()
    {
        mode = Mode.Alert;
        PlayState(idleState);
    }

    public IEnumerator Scream(float duration)
    {
        mode = Mode.Screaming;

        if (!screamEvent.IsNull)
        {
            RuntimeManager.PlayOneShotAttached(screamEvent, gameObject);
        }

        onScream.Invoke();
        yield return new WaitForSeconds(duration);
        mode = Mode.Alert;
    }

    // Turns to face a point almost instantly (the "it noticed you" moment).
    public IEnumerator SnapLookAt(Vector3 point, float duration = 0.06f)
    {
        Quaternion from = transform.rotation;
        Quaternion to = FacingRotation(point);

        for (float t = 0f; t < duration; t += Time.deltaTime)
        {
            transform.rotation = Quaternion.Slerp(from, to, t / duration);
            yield return null;
        }

        transform.rotation = to;
        headPitch = alertHeadPitch - 10f; // head jerks up a little
    }

    // Hops toward a point as fast as it can. Ends when it gets there (or after maxTime).
    public IEnumerator RunTo(Vector3 point, float maxTime = 6f)
    {
        mode = Mode.Running;
        PlayState(runState);
        onStartRun.Invoke();

        float end = Time.time + maxTime;

        while (Time.time < end)
        {
            Vector3 flat = point - transform.position;
            flat.y = 0f;

            if (flat.magnitude <= stopDistance)
            {
                break;
            }

            transform.rotation = Quaternion.RotateTowards(transform.rotation, FacingRotation(point), 900f * Time.deltaTime);
            speed = Mathf.MoveTowards(speed, runSpeed, runAcceleration * Time.deltaTime);
            transform.position += flat.normalized * Mathf.Min(speed * Time.deltaTime, flat.magnitude);

            runPhase += Time.deltaTime * hopFrequency * Mathf.PI * 2f;
            hop = Mathf.Abs(Mathf.Sin(runPhase * 0.5f)) * hopHeight * Mathf.Clamp01(speed / runSpeed);

            if (snapToGround) SnapToGround(hop);
            yield return null;
        }

        speed = 0f;
        hop = 0f;
        mode = Mode.Alert;
        onReachedTarget.Invoke();
    }

    public void Hide()
    {
        StopAllCoroutines();
        mode = Mode.Idle;
        if (renderers == null) renderers = GetComponentsInChildren<Renderer>(true);
        foreach (Renderer r in renderers) if (r != null) r.enabled = false;
        SetOwnLights(false);
    }

    // Lights parented to the rabbit (e.g. a soft light so it reads in the dark field) go with it.
    // Their objects are switched off, so the lights coming back after the blackout don't relight the empty field.
    private void SetOwnLights(bool on)
    {
        foreach (Light l in GetComponentsInChildren<Light>(true))
        {
            if (l.gameObject != gameObject) l.gameObject.SetActive(on);
        }
    }

    public void Show()
    {
        if (renderers == null) renderers = GetComponentsInChildren<Renderer>(true);
        foreach (Renderer r in renderers) if (r != null) r.enabled = true;
        SetOwnLights(true);
    }

    private void PlayState(string state)
    {
        if (animator != null && animator.isActiveAndEnabled && animator.runtimeAnimatorController != null &&
            !string.IsNullOrEmpty(state))
        {
            animator.CrossFadeInFixedTime(state, crossFade);
        }
    }

    // True when an Animator poses the bones every frame; the code then adds its moves on top of that pose.
    private bool AnimatorDrivesBones
    {
        get { return animator != null && animator.isActiveAndEnabled && animator.runtimeAnimatorController != null; }
    }

    // ---------------------------------------------------------------- previews (play mode)

    [ContextMenu("Preview: Eat")] private void PreviewEat() { StartEating(); }
    [ContextMenu("Preview: Look Ahead")] private void PreviewAlert() { LookAhead(); }
    [ContextMenu("Preview: Scream")] private void PreviewScream() { StartCoroutine(Scream(2f)); }
    [ContextMenu("Preview: Reset")] private void PreviewReset() { ResetToStart(); }
    [ContextMenu("Preview: Run 3 m Forward")] private void PreviewRun() { StartCoroutine(RunTo(transform.position + transform.forward * 3f, 3f)); }

    // ---------------------------------------------------------------- animation

    private void LateUpdate()
    {
        float dt = Time.deltaTime;
        float time = Time.time;

        float targetHead = 0f, targetJaw = 0f, targetEar = 0f, targetBody = 0f, targetLegs = 0f;
        float headSpeed = 6f;

        switch (mode)
        {
            case Mode.Eating:
                targetHead = eatingHeadPitch + Mathf.Sin(time * chewSpeed * 0.5f) * nibbleNod;
                targetJaw = Mathf.Max(0f, Mathf.Sin(time * chewSpeed)) * chewJawAngle;
                // Occasional ear twitch.
                targetEar = Mathf.PerlinNoise(time * 0.8f, 3.1f) > 0.72f ? 12f : 0f;
                headSpeed = 4f;
                break;

            case Mode.Alert:
                targetHead = alertHeadPitch;
                targetEar = alertEarAngle;
                headSpeed = 9f;
                break;

            case Mode.Screaming:
                targetHead = screamHeadPitch + Mathf.Sin(time * 47f) * screamTremble;
                targetJaw = screamJawAngle + Mathf.Sin(time * 61f) * screamTremble;
                targetEar = alertEarAngle - 10f;
                targetBody = -6f + Mathf.Sin(time * 53f) * screamTremble * 0.5f;
                headSpeed = 14f;
                break;

            case Mode.Running:
                targetHead = -5f;
                targetEar = runEarAngle;
                targetBody = Mathf.Sin(runPhase) * bodyPitchAngle;
                targetLegs = Mathf.Sin(runPhase) * legSwingAngle;
                headSpeed = 16f;
                break;
        }

        headPitch = Mathf.Lerp(headPitch, targetHead, 1f - Mathf.Exp(-headSpeed * dt));
        jawAngle = Mathf.Lerp(jawAngle, targetJaw, 1f - Mathf.Exp(-18f * dt));
        earAngle = Mathf.Lerp(earAngle, targetEar, 1f - Mathf.Exp(-10f * dt));
        bodyPitch = Mathf.Lerp(bodyPitch, targetBody, 1f - Mathf.Exp(-20f * dt));
        legSwing = mode == Mode.Running ? targetLegs : Mathf.Lerp(legSwing, 0f, 1f - Mathf.Exp(-10f * dt));

        // Breathing when idle-ish.
        float breathe = mode == Mode.Running ? 0f : Mathf.Sin(time * 2.4f) * 1.2f;

        Pose(body, bodyPitchAxis, bodyPitch + breathe);

        if (neck != null)
        {
            Pose(neck, headPitchAxis, headPitch * 0.4f);
            Pose(head, headPitchAxis, headPitch * 0.6f);
        }
        else
        {
            Pose(head, headPitchAxis, headPitch);
        }

        Pose(jaw, jawOpenAxis, jawAngle);
        Pose(earLeft, earAxis, earAngle);
        Pose(earRight, earAxis, earAngle);

        foreach (Transform leg in frontLegs) Pose(leg, legSwingAxis, legSwing);
        foreach (Transform leg in backLegs) Pose(leg, legSwingAxis, -legSwing);

        // Placeholder without bones: tilt the whole rabbit instead.
        if (body == null && head == null)
        {
            transform.rotation = Quaternion.Euler(0f, transform.eulerAngles.y, 0f) *
                                 Quaternion.AngleAxis(bodyPitch + headPitch * 0.2f, Vector3.right);
        }
    }

    private void Pose(Transform bone, Vector3 axis, float degrees)
    {
        if (bone == null || !rest.TryGetValue(bone, out Quaternion r))
        {
            return;
        }

        if (AnimatorDrivesBones)
        {
            r = bone.localRotation;
        }

        bone.localRotation = r * Quaternion.AngleAxis(degrees, axis.sqrMagnitude > 0f ? axis.normalized : Vector3.right);
    }

    private Quaternion FacingRotation(Vector3 point)
    {
        Vector3 dir = point - transform.position;
        dir.y = 0f;
        if (dir.sqrMagnitude < 0.0001f) return transform.rotation;
        return Quaternion.LookRotation(dir.normalized, Vector3.up) * Quaternion.Euler(0f, modelYawOffset, 0f);
    }

    private void SnapToGround(float extraHeight)
    {
        Vector3 origin = transform.position + Vector3.up * 20f;
        RaycastHit[] hits = Physics.RaycastAll(origin, Vector3.down, 60f, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore);
        float best = float.MaxValue;
        float groundY = transform.position.y - extraHeight;
        bool found = false;

        foreach (RaycastHit hit in hits)
        {
            if (hit.transform.IsChildOf(transform)) continue;
            if (hit.distance < best)
            {
                best = hit.distance;
                groundY = hit.point.y;
                found = true;
            }
        }

        if (found)
        {
            Vector3 p = transform.position;
            p.y = groundY + groundOffset + extraHeight;
            transform.position = p;
        }
    }

    // ---------------------------------------------------------------- bone search helpers

    private static Transform Exact(Transform[] all, string boneName)
    {
        foreach (Transform t in all)
        {
            if (t.name == boneName) return t;
        }
        return null;
    }

    private static Transform FindBone(Transform[] all, params string[] keys)
    {
        List<Transform> found = FindAll(all, keys);
        return found.Count > 0 ? TopMost(found)[0] : null;
    }

    private static List<Transform> FindAll(Transform[] all, params string[] keys)
    {
        // Matches whole name parts: "Ear.L", "ear_l", "EarL" are ears, "Rear_Leg" is not;
        // "Armature" is not an arm. End bones, IK targets and poles are ignored.
        List<Transform> result = new List<Transform>();
        foreach (Transform t in all)
        {
            string n = t.name.ToLowerInvariant();
            if (n.Contains("armature") || n.Contains("_end") || n.EndsWith("end") ||
                n.Contains("target") || n.Contains("pole") || n.StartsWith("ik") || n.Contains("_ik") || n.Contains(".ik"))
            {
                continue;
            }

            string[] parts = System.Text.RegularExpressions.Regex.Split(n, "[^a-z]+");
            bool match = false;

            foreach (string part in parts)
            {
                foreach (string k in keys)
                {
                    if (part.StartsWith(k)) { match = true; break; }
                }
                if (match) break;
            }

            if (match) result.Add(t);
        }
        return result;
    }

    // Keeps only the highest bone of each chain (e.g. the thigh, not the shin and foot under it).
    private static List<Transform> TopMost(List<Transform> bones)
    {
        List<Transform> result = new List<Transform>();
        foreach (Transform t in bones)
        {
            bool hasAncestorInList = false;
            for (Transform p = t.parent; p != null; p = p.parent)
            {
                if (bones.Contains(p)) { hasAncestorInList = true; break; }
            }
            if (!hasAncestorInList) result.Add(t);
        }
        return result;
    }

    private static bool IsUnder(Transform t, Transform[] parents)
    {
        foreach (Transform p in parents)
        {
            if (p != null && t != p && t.IsChildOf(p)) return true;
        }
        return false;
    }
}
