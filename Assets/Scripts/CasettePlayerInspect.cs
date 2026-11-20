using System.Collections;
using Cinemachine;
using UnityEngine;
using UnityEngine.Animations;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.Playables;
using UnityEngine.Rendering;
using HDDepthOfField = UnityEngine.Rendering.HighDefinition.DepthOfField;
using HDDepthOfFieldMode = UnityEngine.Rendering.HighDefinition.DepthOfFieldMode;

// Click the Casette Player: it floats in front of the camera and turns to follow the cursor.
// Click it again: it plays "Insert", then floats to the bottom right of the screen.
// Everything around it is blurred until the player clicks away.
public class CasettePlayerInspect : MonoBehaviour
{

    public float blurFadeDuration = 0.5f;

    [Tooltip("Distance over which the background goes from sharp to fully blurred.")]
    public float blurFalloff = 0.3f;

    [Header("Casette Player")]
    [Tooltip("Root of the Casette Player (the object with the Animator).")]
    public GameObject casettePlayer;

    [Header("Camera")]
    [Tooltip("The CinemachineBrain on the camera that renders the game. " +
             "Raycasts and positions use its live (highest priority) virtual camera.")]
    public CinemachineBrain cinemachineBrain;

    public float cornerDistance = 0.4f;

    [Header("Bottom Right Of The Screen")]
    [Tooltip("Viewport position: (0, 0) is bottom left, (1, 1) is top right.")]
    public Vector2 cornerViewportPosition = new Vector2(0.85f, 0.18f);

    [Header("Blur (HDRP Depth of Field)")]
    [Tooltip("Distance in front of / behind the Casette Player that stays sharp.")]
    public float focusMargin = 0.1f;

    [Tooltip("The \"Insert\" clip from Casette_Animated.fbx.")]
    public AnimationClip insertClip;

    [Header("In Front Of The Camera")]
    public float inspectDistance = 0.4f;

    [Tooltip("Rotation of the Casette Player relative to the camera while in front of it.")]
    public Vector3 inspectRotation = Vector3.zero;

    [Tooltip("How far (in degrees) the Casette Player turns when the cursor is at the edge of the screen.")]
    public float maxRotationAngle = 60f;

    public float moveInDuration = 1f;
    public float moveToCornerDuration = 1f;

    [Tooltip("How quickly the Casette Player catches up with the cursor.")]
    public float rotationSmoothing = 8f;

    private Animator animator;
    private Coroutine blurFadeRoutine;
    private VolumeProfile blurProfile;
    private Volume blurVolume;
    private HDDepthOfField depthOfField;
    private PlayableGraph insertGraph;
    private int lastClickAwayFrame = -1;
    private Transform pivot;

    // Where the Casette Player sits in the room, so the back button can put it back.
    private Transform originalParent;
    private Vector3 originalPosition;
    private Quaternion originalRotation;

    // Set once "Insert" starts: from then on the pick-up can't be undone.
    private bool committed;
    // Back was pressed after the pick-up was committed: skip the blur wait at the end.
    private bool leaveRequested;
    private State state = State.Idle;
    private enum State
    {
        Idle,       // in its place in the room, clickable
        Inspecting, // flying in, playing Insert, flying to the corner
        Waiting,    // in the corner, blur on, waiting for a click away
        Held        // in the corner, blur off
    }

    // Clicks right after an accepted click are ignored for this long (see ClickGuard).
    private const float ClickLockSeconds = 0.4f;

    // CursorCameraManager ignores mouse input while this is true,
    // including on the frame the player clicked away.
    public bool BlocksInput
    {
        get
        {
            return state == State.Inspecting ||
                   state == State.Waiting ||
                   lastClickAwayFrame == Time.frameCount;
        }
    }

    public bool CanInspect
    {
        get { return state == State.Idle && casettePlayer != null; }
    }

    // True once the Casette Player has been picked up and sits in the bottom right corner.
    public bool IsHeld
    {
        get { return state == State.Held; }
    }

    // The highest priority virtual camera, i.e. the one the brain is showing.
    private ICinemachineCamera ActiveCamera
    {
        get { return cinemachineBrain != null ? cinemachineBrain.ActiveVirtualCamera : null; }
    }

    // Called by CursorCameraManager when C_CasettePlayer is clicked.
    public void Inspect()
    {
        if (!CanInspect || ActiveCamera == null)
        {
            return;
        }

        originalParent = casettePlayer.transform.parent;
        originalPosition = casettePlayer.transform.position;
        originalRotation = casettePlayer.transform.rotation;
        committed = false;
        leaveRequested = false;

        StartCoroutine(InspectRoutine());
    }

    // Called by CursorCameraManager when the back button is pressed.
    //   Before "Insert": the pick-up is cancelled and the Casette Player flies back to its place.
    //   During "Insert" / the move to the corner: it finishes, then is held without the blur.
    //   Waiting in the corner: the blur goes off, it stays held.
    public void OnBackPressed()
    {
        if (state == State.Inspecting && !committed)
        {
            StopAllCoroutines(); // the inspect routine and its moves
            blurFadeRoutine = null;
            StartCoroutine(ReturnRoutine());
            return;
        }

        if (state == State.Inspecting)
        {
            leaveRequested = true;
            return;
        }

        if (state == State.Waiting)
        {
            state = State.Held;
            StartBlurFade(0f);
        }
    }

    private IEnumerator ReturnRoutine()
    {
        StartBlurFade(0f);

        // Detach from the camera so it doesn't follow the view any more.
        pivot.SetParent(null, true);

        // Pivot pose that puts the Casette Player exactly back where it was.
        Transform player = casettePlayer.transform;
        Quaternion toRotation = originalRotation * Quaternion.Inverse(player.localRotation);
        Vector3 toPosition = originalPosition - toRotation * Vector3.Scale(pivot.lossyScale, player.localPosition);

        Vector3 fromPosition = pivot.position;
        Quaternion fromRotation = pivot.rotation;
        float elapsed = 0f;

        while (elapsed < moveInDuration)
        {
            elapsed += Time.deltaTime;

            float t = EaseInOut(elapsed / moveInDuration);

            pivot.SetPositionAndRotation(
                Vector3.Lerp(fromPosition, toPosition, t),
                Quaternion.Slerp(fromRotation, toRotation, t)
            );

            yield return null;
        }

        player.SetParent(originalParent, true);
        player.SetPositionAndRotation(originalPosition, originalRotation);

        Destroy(pivot.gameObject);
        pivot = null;

        state = State.Idle;
    }

    public bool IsCasettePlayerCollider(Collider collider)
    {
        return casettePlayer != null &&
               collider != null &&
               collider.transform.IsChildOf(casettePlayer.transform);
    }

    // Hides / shows the held Casette Player (e.g. while its cassette is inserted in the TV).
    public void SetHeldVisible(bool visible)
    {
        if (state == State.Held && pivot != null)
        {
            pivot.gameObject.SetActive(visible);
        }
    }

    private static float EaseInOut(float t)
    {
        t = Mathf.Clamp01(t);

        return t * t * (3f - 2f * t);
    }

    // Direction (with z = 1) through a viewport point of a perspective lens.
    private static Vector3 ViewportOffset(Vector2 viewport, LensSettings lens)
    {
        float halfHeight = Mathf.Tan(lens.FieldOfView * 0.5f * Mathf.Deg2Rad);
        float halfWidth = halfHeight * lens.Aspect;

        return new Vector3(
            (viewport.x * 2f - 1f) * halfWidth,
            (viewport.y * 2f - 1f) * halfHeight,
            1f
        );
    }

    // World point at the given viewport position and depth, as seen by the active virtual camera.
    private Vector3 ActiveCameraViewportToWorld(Vector2 viewport, float depth)
    {
        CameraState cameraState = ActiveCamera.State;

        return cameraState.FinalPosition +
               cameraState.FinalOrientation * (ViewportOffset(viewport, cameraState.Lens) * depth);
    }

    private void CreateBlurVolume()
    {
        blurProfile = ScriptableObject.CreateInstance<VolumeProfile>();

        depthOfField = blurProfile.Add<HDDepthOfField>(true);
        depthOfField.focusMode.Override(HDDepthOfFieldMode.Manual);

        GameObject volumeObject = new GameObject("CasettePlayer Blur Volume");
        volumeObject.transform.SetParent(transform, false);

        blurVolume = volumeObject.AddComponent<Volume>();
        blurVolume.isGlobal = true;
        blurVolume.priority = 100f;
        blurVolume.weight = 0f;
        blurVolume.sharedProfile = blurProfile;
    }

    // Puts a pivot at the visual centre of the Casette Player and parents it to the camera.
    // The player's rotation goes on the pivot, so it never fights the Animator.
    private void CreatePivot()
    {
        Bounds bounds = new Bounds(casettePlayer.transform.position, Vector3.zero);
        bool hasBounds = false;

        foreach (Renderer renderer in casettePlayer.GetComponentsInChildren<Renderer>())
        {
            if (!renderer.enabled)
            {
                continue;
            }

            if (hasBounds)
            {
                bounds.Encapsulate(renderer.bounds);
            }
            else
            {
                bounds = renderer.bounds;
                hasBounds = true;
            }
        }

        pivot = new GameObject("CasettePlayer Pivot").transform;
        pivot.SetPositionAndRotation(bounds.center, casettePlayer.transform.rotation);

        casettePlayer.transform.SetParent(pivot, true);
        pivot.SetParent(cinemachineBrain.transform, true);
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

    private IEnumerator InspectRoutine()
    {
        state = State.Inspecting;

        CreatePivot();
        StartBlurFade(1f);

        // =========================================================
        // STEP 1 - FLOAT IN FRONT OF THE CAMERA
        // =========================================================

        // Turned 180° around the camera's up axis so the front faces the camera.
        ToPivotSpace(
            ActiveCameraViewportToWorld(new Vector2(0.5f, 0.5f), inspectDistance),
            ActiveCamera.State.FinalOrientation *
                Quaternion.Euler(0f, 180f, 0f) *
                Quaternion.Euler(inspectRotation),
            out Vector3 inspectPosition,
            out Quaternion facingRotation
        );

        yield return StartCoroutine(
            MovePivot(
                inspectPosition,
                facingRotation,
                moveInDuration
            )
        );

        // =========================================================
        // STEP 2 - WAIT FOR ANOTHER CLICK ON THE CASETTE PLAYER
        // =========================================================

        // Skip the frame of the first click.
        yield return null;

        while (Mouse.current == null ||
               !Mouse.current.leftButton.wasPressedThisFrame ||
               ClickGuard.IsLocked ||
               !PointerIsOverCasettePlayer())
        {
            UpdateRotation(facingRotation);

            yield return null;
        }

        // Only the first click of a fast double click counts.
        ClickGuard.Lock(ClickLockSeconds);

        // =========================================================
        // STEP 3 - PLAY "INSERT" (it keeps following the cursor meanwhile)
        // =========================================================

        committed = true;

        if (animator != null && insertClip != null)
        {
            if (insertGraph.IsValid())
            {
                insertGraph.Destroy();
            }

            AnimationClipPlayable playable =
                AnimationPlayableUtilities.PlayClip(animator, insertClip, out insertGraph);

            while (playable.GetTime() < insertClip.length)
            {
                UpdateRotation(facingRotation);

                yield return null;
            }

            // Hold the last frame.
            playable.SetTime(insertClip.length);
            playable.Pause();
        }
        else
        {
            Debug.LogWarning(
                "CasettePlayerInspect: No Animator or Insert clip assigned."
            );
        }

        // =========================================================
        // STEP 4 - FLOAT TO THE BOTTOM RIGHT OF THE SCREEN
        //          (and turn back to face the camera)
        // =========================================================

        ToPivotSpace(
            ActiveCameraViewportToWorld(cornerViewportPosition, cornerDistance),
            pivot.rotation,
            out Vector3 cornerPosition,
            out Quaternion _
        );

        yield return StartCoroutine(
            MovePivot(
                cornerPosition,
                facingRotation,
                moveToCornerDuration
            )
        );

        if (leaveRequested)
        {
            // Back was pressed on the way: just hold it, no blur.
            state = State.Held;
            StartBlurFade(0f);
            yield break;
        }

        // The blur stays on until the player clicks away (see Update).
        state = State.Waiting;
    }

    // Moves the pivot in camera space with an ease-in / ease-out.
    private IEnumerator MovePivot(
        Vector3 toPosition,
        Quaternion toRotation,
        float duration
    )
    {
        Vector3 fromPosition = pivot.localPosition;
        Quaternion fromRotation = pivot.localRotation;

        float elapsed = 0f;

        while (elapsed < duration)
        {
            elapsed += Time.deltaTime;

            float t = EaseInOut(elapsed / duration);

            pivot.localPosition = Vector3.Lerp(fromPosition, toPosition, t);
            pivot.localRotation = Quaternion.Slerp(fromRotation, toRotation, t);

            yield return null;
        }

        pivot.localPosition = toPosition;
        pivot.localRotation = toRotation;
    }

    private void OnDestroy()
    {
        if (insertGraph.IsValid())
        {
            insertGraph.Destroy();
        }

        if (blurProfile != null)
        {
            Destroy(blurProfile);
        }
    }

    // Raycasts from the active virtual camera through the mouse position.
    private bool PointerIsOverCasettePlayer()
    {
        if (ActiveCamera == null)
        {
            return false;
        }

        CameraState cameraState = ActiveCamera.State;
        Vector2 mousePosition = Mouse.current.position.ReadValue();

        Vector2 viewport = new Vector2(
            mousePosition.x / Screen.width,
            mousePosition.y / Screen.height
        );

        Ray ray = new Ray(
            cameraState.FinalPosition,
            cameraState.FinalOrientation * ViewportOffset(viewport, cameraState.Lens).normalized
        );

        return Physics.Raycast(
                   ray,
                   out RaycastHit hit,
                   Mathf.Infinity,
                   Physics.DefaultRaycastLayers,
                   QueryTriggerInteraction.Collide) &&
               IsCasettePlayerCollider(hit.collider);
    }

    private void Start()
    {
        if (cinemachineBrain == null)
        {
            cinemachineBrain = FindObjectOfType<CinemachineBrain>();
        }

        if (cinemachineBrain == null)
        {
            Debug.LogWarning(
                "CasettePlayerInspect: No CinemachineBrain assigned or found."
            );
        }

        if (casettePlayer != null)
        {
            animator = casettePlayer.GetComponentInChildren<Animator>();

            if (animator == null)
            {
                animator = casettePlayer.AddComponent<Animator>();
            }
        }
        else
        {
            Debug.LogWarning(
                "CasettePlayerInspect: No Casette Player assigned."
            );
        }

        CreateBlurVolume();
    }

    private void StartBlurFade(float to)
    {
        if (blurFadeRoutine != null)
        {
            StopCoroutine(blurFadeRoutine);
        }

        blurFadeRoutine = StartCoroutine(FadeBlur(to));
    }

    // Converts a world pose into the pivot's parent (the brain's camera) space.
    private void ToPivotSpace(
        Vector3 worldPosition,
        Quaternion worldRotation,
        out Vector3 localPosition,
        out Quaternion localRotation
    )
    {
        Transform parent = cinemachineBrain.transform;

        localPosition = parent.InverseTransformPoint(worldPosition);
        localRotation = Quaternion.Inverse(parent.rotation) * worldRotation;
    }

    private void Update()
    {
        // Keep the Casette Player in focus and blur everything nearer or further away.
        if (pivot != null && depthOfField != null && blurVolume.weight > 0f && ActiveCamera != null)
        {
            float distance = Vector3.Distance(ActiveCamera.State.FinalPosition, pivot.position);

            depthOfField.nearFocusStart.Override(0f);
            depthOfField.nearFocusEnd.Override(Mathf.Max(0.01f, distance - focusMargin));
            depthOfField.farFocusStart.Override(distance + focusMargin);
            depthOfField.farFocusEnd.Override(distance + focusMargin + blurFalloff);
        }

        if (state != State.Waiting || Mouse.current == null)
        {
            return;
        }

        if (Mouse.current.leftButton.wasPressedThisFrame &&
            !ClickGuard.IsLocked &&
            !PointerIsOverCasettePlayer())
        {
            lastClickAwayFrame = Time.frameCount;
            state = State.Held;
            StartBlurFade(0f);

            // A click away onto the back button must still reach the button.
            if (EventSystem.current == null || !EventSystem.current.IsPointerOverGameObject())
            {
                ClickGuard.Lock(ClickLockSeconds);
            }
        }
    }

    // Turns the front of the Casette Player towards the cursor.
    // Cursor in the middle of the screen = facing the camera straight on.
    private void UpdateRotation(Quaternion facingRotation)
    {
        if (Mouse.current == null)
        {
            return;
        }

        Vector2 mousePosition = Mouse.current.position.ReadValue();

        // -1 to 1 on both axes, 0 at the centre of the screen.
        Vector2 offset = new Vector2(
            Mathf.Clamp(mousePosition.x / Screen.width * 2f - 1f, -1f, 1f),
            Mathf.Clamp(mousePosition.y / Screen.height * 2f - 1f, -1f, 1f)
        );

        Quaternion targetRotation =
            Quaternion.AngleAxis(-offset.x * maxRotationAngle, Vector3.up) *
            Quaternion.AngleAxis(offset.y * maxRotationAngle, Vector3.right) *
            facingRotation;

        pivot.localRotation = Quaternion.Slerp(
            pivot.localRotation,
            targetRotation,
            1f - Mathf.Exp(-rotationSmoothing * Time.deltaTime)
        );
    }
}