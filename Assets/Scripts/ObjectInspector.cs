using System.Collections;
using Cinemachine;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.Rendering;
using HDDepthOfField = UnityEngine.Rendering.HighDefinition.DepthOfField;
using HDDepthOfFieldMode = UnityEngine.Rendering.HighDefinition.DepthOfFieldMode;

// Picking up and looking at an InspectableObject (e.g. the office notepad), like the Casette Player:
//   click it        -> it floats in front of the camera, the background blurs, it turns with the cursor
//   click elsewhere -> it goes back to exactly where it was (the back button does the same)
//
// So an extra click can't send it back by accident:
//   - clicks do nothing while it is flying in, and for "Settle Time" seconds after it arrives
//   - only a full click (press AND release) outside the object counts, and the press must start
//     after the object has settled, so a double click on it while picking it up is ignored
//   - clicks on the object itself never send it back, and clicks on UI are ignored
//   - every accepted click locks the shared ClickGuard, so CursorCameraManager can't pick it up again at once
//
// CursorCameraManager creates one of these automatically if the scene has none.
public class ObjectInspector : MonoBehaviour
{
    [Header("Objects")]
    [Tooltip("Scene objects with these names get an InspectableObject at start (so no scene setup is needed).")]
    public string[] autoInspectableNames = { "OfficeNotePad" };

    [Header("Camera")]
    [Tooltip("Empty = the first CinemachineBrain in the scene.")]
    public CinemachineBrain cinemachineBrain;

    [Header("Movement")]
    public float moveInDuration = 0.8f;
    public float returnDuration = 0.8f;

    [Tooltip("How quickly the object catches up with the cursor.")]
    public float rotationSmoothing = 8f;

    [Header("Accidental Click Protection")]
    [Tooltip("After the object arrives, clicks are ignored for this long.")]
    public float settleTime = 0.5f;

    [Tooltip("Locks all clicks for this long after the object is picked up or sent back.")]
    public float clickLockSeconds = 0.4f;

    [Header("Blur (HDRP Depth of Field)")]
    public float blurFadeDuration = 0.5f;

    [Tooltip("Distance in front of / behind the object that stays sharp.")]
    public float focusMargin = 0.1f;

    [Tooltip("Distance over which the background goes from sharp to fully blurred.")]
    public float blurFalloff = 0.3f;

    private enum State { Idle, MovingIn, Viewing, Returning }
    private State state = State.Idle;

    private InspectableObject current;
    private Transform pivot;
    private Quaternion facing;
    private float viewingSince;
    private bool pressStartedOutside;

    // Where the object sits in the room.
    private Transform originalParent;
    private Vector3 originalLocalPosition;
    private Quaternion originalLocalRotation;
    private Vector3 originalLocalScale;

    private Volume blurVolume;
    private VolumeProfile blurProfile;
    private HDDepthOfField depthOfField;
    private Coroutine blurFadeRoutine;

    // CursorCameraManager ignores the mouse while this is true.
    public bool BlocksInput
    {
        get { return state != State.Idle; }
    }

    public bool CanInspect(InspectableObject obj)
    {
        return state == State.Idle && obj != null && obj.isActiveAndEnabled && cinemachineBrain != null;
    }

    // Called by CursorCameraManager when an InspectableObject is clicked.
    public void Inspect(InspectableObject obj)
    {
        if (!CanInspect(obj))
        {
            return;
        }

        ClickGuard.Lock(clickLockSeconds);
        StartCoroutine(InspectRoutine(obj));
    }

    // Called by CursorCameraManager when the back button is pressed.
    public void OnBackPressed()
    {
        if (state == State.MovingIn || state == State.Viewing)
        {
            StopAllCoroutines();
            blurFadeRoutine = null;
            StartCoroutine(ReturnRoutine());
        }
    }

    // ================================================================ PICK UP / LOOK / PUT BACK

    private IEnumerator InspectRoutine(InspectableObject obj)
    {
        state = State.MovingIn;
        current = obj;
        pressStartedOutside = false;

        Transform t = obj.transform;
        originalParent = t.parent;
        originalLocalPosition = t.localPosition;
        originalLocalRotation = t.localRotation;
        originalLocalScale = t.localScale;

        Bounds bounds = GetBounds(t);
        pivot = new GameObject(obj.name + " Inspect Pivot").transform;
        pivot.SetPositionAndRotation(bounds.center, t.rotation);
        t.SetParent(pivot, true);
        pivot.SetParent(cinemachineBrain.transform, true);

        facing = FrontRotation(obj, t);
        Vector3 front = new Vector3(0f, 0f, InspectDistance(obj, bounds));

        StartBlurFade(1f);
        yield return MovePivot(front, facing, moveInDuration);

        state = State.Viewing;
        viewingSince = Time.unscaledTime;

        while (!ClickedOutside())
        {
            UpdateRotation();
            yield return null;
        }

        ClickGuard.Lock(clickLockSeconds);
        yield return ReturnRoutine();
    }

    private IEnumerator ReturnRoutine()
    {
        state = State.Returning;
        StartBlurFade(0f);

        Transform t = current.transform;

        // Off the camera, so it doesn't follow the view any more.
        pivot.SetParent(null, true);

        Vector3 fromPosition = pivot.position;
        Quaternion fromRotation = pivot.rotation;
        float elapsed = 0f;

        while (elapsed < returnDuration)
        {
            elapsed += Time.deltaTime;
            float k = EaseInOut(elapsed / returnDuration);

            GetHomePivotPose(t, out Vector3 toPosition, out Quaternion toRotation);
            pivot.SetPositionAndRotation(Vector3.Lerp(fromPosition, toPosition, k),
                                         Quaternion.Slerp(fromRotation, toRotation, k));
            yield return null;
        }

        // Exactly where it was.
        t.SetParent(originalParent, false);
        t.localPosition = originalLocalPosition;
        t.localRotation = originalLocalRotation;
        t.localScale = originalLocalScale;

        Destroy(pivot.gameObject);
        pivot = null;
        current = null;
        state = State.Idle;
    }

    // True on the frame a full click (press and release) outside the object finishes.
    private bool ClickedOutside()
    {
        Mouse mouse = Mouse.current;
        if (mouse == null)
        {
            return false;
        }

        bool settled = Time.unscaledTime - viewingSince >= settleTime;

        if (mouse.leftButton.wasPressedThisFrame)
        {
            pressStartedOutside = settled && !ClickGuard.IsLocked && !PointerOverUi() && !PointerOverObject();
        }

        if (!mouse.leftButton.wasReleasedThisFrame)
        {
            return false;
        }

        bool accepted = pressStartedOutside && !PointerOverUi() && !PointerOverObject();
        pressStartedOutside = false;
        return accepted;
    }

    private static bool PointerOverUi()
    {
        return EventSystem.current != null && EventSystem.current.IsPointerOverGameObject();
    }

    private bool PointerOverObject()
    {
        Camera cam = cinemachineBrain != null ? cinemachineBrain.OutputCamera : null;
        if (cam == null || Mouse.current == null || current == null)
        {
            return false;
        }

        Physics.SyncTransforms(); // the held object moves without a Rigidbody

        Ray ray = cam.ScreenPointToRay(Mouse.current.position.ReadValue());

        foreach (RaycastHit hit in Physics.RaycastAll(ray, 100f, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Collide))
        {
            if (current.IsMyCollider(hit.collider))
            {
                return true;
            }
        }

        return false;
    }

    // ================================================================ POSES

    // Facing the camera: the side that is up in the room turned towards the lens, plus the extra rotation.
    private Quaternion FrontRotation(InspectableObject obj, Transform t)
    {
        Transform cam = cinemachineBrain.transform;
        Quaternion world = t.rotation;

        if (obj.showTopSideToCamera)
        {
            Vector3 top = UpmostAxis(t);
            Quaternion turn = Quaternion.FromToRotation(top, -cam.forward);
            world = turn * world;

            // Keep the object's "far" edge pointing up on screen, so it isn't upside down.
            Vector3 away = Vector3.ProjectOnPlane(turn * Vector3.ProjectOnPlane(cam.forward, Vector3.up), cam.forward);
            if (away.sqrMagnitude > 1e-6f)
            {
                world = Quaternion.AngleAxis(Vector3.SignedAngle(away, cam.up, cam.forward), cam.forward) * world;
            }
        }

        world = cam.rotation * Quaternion.Euler(obj.extraRotation) * Quaternion.Inverse(cam.rotation) * world;

        return Quaternion.Inverse(cam.rotation) * world;
    }

    // The object's local axis (±X, ±Y, ±Z) that points most upwards in the world.
    private static Vector3 UpmostAxis(Transform t)
    {
        Vector3[] axes = { t.right, -t.right, t.up, -t.up, t.forward, -t.forward };
        Vector3 best = t.up;
        float bestDot = -2f;

        foreach (Vector3 axis in axes)
        {
            float dot = Vector3.Dot(axis, Vector3.up);
            if (dot > bestDot)
            {
                bestDot = dot;
                best = axis;
            }
        }

        return best;
    }

    private float InspectDistance(InspectableObject obj, Bounds bounds)
    {
        if (obj.inspectDistance > 0f)
        {
            return obj.inspectDistance;
        }

        Camera cam = cinemachineBrain.OutputCamera;
        float fov = cam != null ? cam.fieldOfView : 60f;
        float radius = Mathf.Max(bounds.extents.magnitude, 0.01f);

        // Distance at which the object's bounding sphere fills "Screen Fill" of the screen height.
        float distance = radius / (Mathf.Tan(fov * 0.5f * Mathf.Deg2Rad) * obj.screenFill);

        float near = cam != null ? cam.nearClipPlane : 0.05f;
        return Mathf.Max(distance, near + radius + 0.02f);
    }

    // Pivot pose that puts the object back at its original place (re-read every frame in case the parent moves).
    private void GetHomePivotPose(Transform t, out Vector3 position, out Quaternion rotation)
    {
        Vector3 worldPosition = originalParent != null ? originalParent.TransformPoint(originalLocalPosition) : originalLocalPosition;
        Quaternion worldRotation = originalParent != null ? originalParent.rotation * originalLocalRotation : originalLocalRotation;

        rotation = worldRotation * Quaternion.Inverse(t.localRotation);
        position = worldPosition - rotation * Vector3.Scale(pivot.lossyScale, t.localPosition);
    }

    private static Bounds GetBounds(Transform t)
    {
        Bounds bounds = new Bounds(t.position, Vector3.zero);
        bool hasBounds = false;

        foreach (Renderer r in t.GetComponentsInChildren<Renderer>())
        {
            if (!r.enabled) continue;

            if (hasBounds) bounds.Encapsulate(r.bounds);
            else { bounds = r.bounds; hasBounds = true; }
        }

        return bounds;
    }

    private IEnumerator MovePivot(Vector3 toPosition, Quaternion toRotation, float duration)
    {
        Vector3 fromPosition = pivot.localPosition;
        Quaternion fromRotation = pivot.localRotation;
        float elapsed = 0f;

        while (elapsed < duration)
        {
            elapsed += Time.deltaTime;
            float k = EaseInOut(elapsed / duration);

            pivot.localPosition = Vector3.Lerp(fromPosition, toPosition, k);
            pivot.localRotation = Quaternion.Slerp(fromRotation, toRotation, k);
            yield return null;
        }

        pivot.localPosition = toPosition;
        pivot.localRotation = toRotation;
    }

    // Turns the object towards the cursor. Cursor in the middle of the screen = straight on.
    private void UpdateRotation()
    {
        if (Mouse.current == null || pivot == null)
        {
            return;
        }

        Vector2 mouse = Mouse.current.position.ReadValue();
        Vector2 offset = new Vector2(
            Mathf.Clamp(mouse.x / Screen.width * 2f - 1f, -1f, 1f),
            Mathf.Clamp(mouse.y / Screen.height * 2f - 1f, -1f, 1f));

        float angle = current.maxRotationAngle;
        Quaternion target =
            Quaternion.AngleAxis(-offset.x * angle, Vector3.up) *
            Quaternion.AngleAxis(offset.y * angle, Vector3.right) *
            facing;

        pivot.localRotation = Quaternion.Slerp(pivot.localRotation, target,
                                               1f - Mathf.Exp(-rotationSmoothing * Time.deltaTime));
    }

    private static float EaseInOut(float t)
    {
        t = Mathf.Clamp01(t);
        return t * t * (3f - 2f * t);
    }

    // ================================================================ BLUR

    private void CreateBlurVolume()
    {
        blurProfile = ScriptableObject.CreateInstance<VolumeProfile>();
        depthOfField = blurProfile.Add<HDDepthOfField>(true);
        depthOfField.focusMode.Override(HDDepthOfFieldMode.Manual);

        GameObject volumeObject = new GameObject("Inspect Blur Volume");
        volumeObject.transform.SetParent(transform, false);

        blurVolume = volumeObject.AddComponent<Volume>();
        blurVolume.isGlobal = true;
        blurVolume.priority = 100f;
        blurVolume.weight = 0f;
        blurVolume.sharedProfile = blurProfile;
    }

    private void StartBlurFade(float to)
    {
        if (blurFadeRoutine != null)
        {
            StopCoroutine(blurFadeRoutine);
        }

        blurFadeRoutine = StartCoroutine(FadeBlur(to));
    }

    private IEnumerator FadeBlur(float to)
    {
        float from = blurVolume.weight;
        float elapsed = 0f;

        while (elapsed < blurFadeDuration)
        {
            elapsed += Time.deltaTime;
            blurVolume.weight = Mathf.Lerp(from, to, elapsed / blurFadeDuration);
            yield return null;
        }

        blurVolume.weight = to;
        blurFadeRoutine = null;
    }

    // ================================================================ LIFETIME

    private void Start()
    {
        if (cinemachineBrain == null)
        {
            cinemachineBrain = FindFirstObjectByType<CinemachineBrain>();
        }

        if (cinemachineBrain == null)
        {
            Debug.LogWarning("ObjectInspector: no CinemachineBrain found.");
        }

        CreateBlurVolume();
        AddAutoInspectables();
    }

    private void AddAutoInspectables()
    {
        if (autoInspectableNames == null || autoInspectableNames.Length == 0)
        {
            return;
        }

        foreach (Transform t in FindObjectsByType<Transform>(FindObjectsInactive.Include, FindObjectsSortMode.None))
        {
            if (System.Array.IndexOf(autoInspectableNames, t.name) < 0 || t.GetComponent<InspectableObject>() != null)
            {
                continue;
            }

            if (t.GetComponentInChildren<Collider>(true) == null)
            {
                Debug.LogWarning("ObjectInspector: '" + t.name + "' has no collider, so it can't be clicked.", t);
                continue;
            }

            t.gameObject.AddComponent<InspectableObject>();
        }
    }

    private void Update()
    {
        // Keep the object in focus and blur everything behind it (no near blur, so the UI stays sharp).
        if (pivot != null && depthOfField != null && blurVolume.weight > 0f && cinemachineBrain != null)
        {
            float distance = Vector3.Distance(cinemachineBrain.transform.position, pivot.position);

            depthOfField.nearFocusStart.Override(0f);
            depthOfField.nearFocusEnd.Override(0.001f);
            depthOfField.farFocusStart.Override(distance + focusMargin);
            depthOfField.farFocusEnd.Override(distance + focusMargin + blurFalloff);
        }
    }

    private void OnDestroy()
    {
        if (blurProfile != null)
        {
            Destroy(blurProfile);
        }
    }
}
