using System.Collections;
using System.Collections.Generic;
using TMPro;
using Cinemachine;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.Rendering;
using HDDepthOfField = UnityEngine.Rendering.HighDefinition.DepthOfField;
using HDDepthOfFieldMode = UnityEngine.Rendering.HighDefinition.DepthOfFieldMode;

// The Casette Player and the cassettes that go into it.
//
// PICKING UP THE PLAYER
//   Click the Casette Player: it floats in front of the camera (background blurred, "Pickup Message" shown)
//   and turns with the cursor. It stays there until you click it again, then goes to the bottom right (held).
//
// COLLECTING CASSETTES (while the player is held)
//   Click a cassette in the room: it floats in front of the camera and stays there.
//   Click it again: it goes to the bottom right, next to the player (the inventory), and the counter goes up.
//   Several cassettes are stacked there, the first one in front.
//
// PUTTING ONE IN
//   One cassette: hover it (it rises slightly) and click it.
//   Several: click the stack, the others slide out next to the first one. Hover any of them (it rises)
//   and click the one you want. The rest fold back into the stack.
//   Then the player comes back in front, the cassette flies to it, the lid opens and the cassette goes in
//   (both "Insert" clips), and the player, with the cassette inside, goes back to the bottom right.
//   If another cassette was inside, it is ejected first and goes back into the inventory.
//   "Eject Key" ejects the cassette that is inside (back into the inventory).
//
// BACK BUTTON
//   While the player is in front of the camera (before it is held): it goes back to the drawer.
//   While a cassette is in front of the camera: the cassette goes back to where it was found.
//   While the inventory is fanned out: it folds back.
public class CasettePlayerInspect : MonoBehaviour
{
    public float blurFadeDuration = 0.5f;

    [Tooltip("Distance over which the background goes from sharp to fully blurred.")]
    public float blurFalloff = 0.3f;

    [Header("Casette Player")]
    [Tooltip("Root of the Casette Player: the object that is picked up and carried.")]
    public GameObject casettePlayer;

    [Tooltip("Root of the Cassette_Player model (has the Animator for the lid). Cassettes are parented here " +
             "while they go in, so they line up with the animation. Empty = the Casette Player root.")]
    public Transform cassetteSocket;

    [Tooltip("Cassette_Player.fbx > Insert (the lid opening and closing while a cassette goes in).")]
    public AnimationClip playerInsertClip;

    [Tooltip("Cassette_Player.fbx > Eject (the lid while a cassette comes out).")]
    public AnimationClip playerEjectClip;

    [Header("Camera")]
    [Tooltip("The CinemachineBrain on the camera that renders the game. " +
             "Raycasts and positions use its live (highest priority) virtual camera.")]
    public CinemachineBrain cinemachineBrain;

    public float cornerDistance = 0.4f;

    [Header("Bottom Right Of The Screen")]
    [Tooltip("Viewport position: (0, 0) is bottom left, (1, 1) is top right.")]
    public Vector2 cornerViewportPosition = new Vector2(0.85f, 0.18f);

    [Header("Blur (HDRP Depth of Field)")]
    [Tooltip("Distance in front of / behind the object in front of the camera that stays sharp.")]
    public float focusMargin = 0.1f;

    [Header("In Front Of The Camera")]
    public float inspectDistance = 0.4f;

    [Tooltip("Rotation of the Casette Player relative to the camera while in front of it.")]
    public Vector3 inspectRotation = Vector3.zero;

    [Tooltip("How far (in degrees) the object turns when the cursor is at the edge of the screen.")]
    public float maxRotationAngle = 60f;

    public float moveInDuration = 1f;
    public float moveToCornerDuration = 1f;

    [Tooltip("How quickly the object catches up with the cursor.")]
    public float rotationSmoothing = 8f;

    [Header("Pickup Message")]
    [Tooltip("Optional. Shown while the Casette Player is in front of the camera after picking it up " +
             "(e.g. a TMP text \"You got the cassette player\").")]
    public GameObject pickupMessage;

    [Header("Cassettes")]
    [Tooltip("Distance from the camera of a cassette held in front of it.")]
    public float cassetteInspectDistance = 0.3f;

    [Tooltip("Time a cassette takes to fly to the player, and back to where it was found after an eject.")]
    public float cassetteFlyDuration = 0.6f;

    [Tooltip("Where a picked cassette waits (viewport) while the player ejects the previous one.")]
    public Vector2 cassetteWaitViewportPosition = new Vector2(0.25f, 0.6f);

    [Tooltip("Where the collected cassettes wait next to the held player (viewport), before one is put in. " +
             "Can be changed live in play mode.")]
    public Vector2 cassetteReadyViewportPosition = new Vector2(0.7f, 0.16f);

    [Tooltip("When several cassettes are fanned out: viewport offset from one cassette to the next.")]
    public Vector2 cassetteSlotSpacing = new Vector2(-0.08f, 0f);

    [Tooltip("When folded: how far (metres) each cassette sits behind the one in front.")]
    public float stackDepthOffset = 0.01f;

    [Header("Cassette Counter")]
    [Tooltip("The CassetteCounter UI object. Empty = found by name (\"CassetteCounter\" or \"Cassette_Counter\").")]
    public GameObject cassetteCounter;

    [Tooltip("The text showing the number. Empty = the child named \"Cassette_Counter_Number\" " +
             "(or the first TextMeshPro text under the counter).")]
    public TMP_Text cassetteCounterNumber;

    [Tooltip("Text shown on the number. {0} = number of cassettes collected.")]
    public string cassetteCounterFormat = "{0}";

    [Tooltip("The counter stays hidden until the first cassette is picked up.")]
    public bool showCounterOnlyAfterPickup = true;

    [Tooltip("How far (metres, upwards on screen) the ready cassette rises while the cursor is over it.")]
    public float hoverRiseDistance = 0.015f;

    [Tooltip("How quickly the ready cassette rises / sinks.")]
    public float hoverRiseSpeed = 12f;

    [Tooltip("Ejects the cassette that is inside. None = no key.")]
    public Key ejectKey = Key.E;

    private Coroutine blurFadeRoutine;
    private VolumeProfile blurProfile;
    private Volume blurVolume;
    private HDDepthOfField depthOfField;

    private Transform pivot;         // the Casette Player's pivot (child of the camera once picked up)
    private Transform cassettePivot; // the picked cassette's pivot while it is in front of the camera
    private Transform focusTarget;   // what the blur keeps sharp

    private ClipPlayer lidClips;

    // Where the Casette Player sits in the room, so the back button can put it back.
    // Stored relative to its parent (the drawer), which may move (close) while it flies back.
    private Transform originalParent;
    private Vector3 originalLocalPosition;
    private Quaternion originalLocalRotation;
    private Vector3 originalLocalScale;

    private CassetteItem currentCassette; // inside the player
    private CassetteItem pickedCassette;  // in front of the camera

    // Collected cassettes waiting next to the held player, each in its own pivot under the camera.
    private readonly List<CassetteItem> inventory = new List<CassetteItem>();
    private readonly Dictionary<CassetteItem, Transform> slotPivots = new Dictionary<CassetteItem, Transform>();
    private bool inventoryExpanded;
    private CassetteItem hoveredInventoryCassette;
    private int inventoryHoverFrame = -10;


    private bool wantsPointingCursor;

    private State state = State.Idle;
    private enum State
    {
        Idle,           // the player is in its place in the room
        PlayerMovingIn, // the player flies in front of the camera
        PlayerWaiting,  // the player is in front of the camera, waiting for a click on it
        PlayerToCorner, // the player flies to the corner
        Held,           // the player is in the corner, blur off
        CassetteMovingIn,
        CassetteWaiting, // a cassette is in front of the camera, waiting for a click on it
        Busy             // inserting / ejecting / returning
    }

    // CursorCameraManager ignores the mouse while this is true (this script handles it).
    public bool BlocksInput
    {
        get { return state != State.Idle && state != State.Held; }
    }

    // While BlocksInput: true when the cursor is over the thing that can be clicked.
    public bool WantsPointingCursor
    {
        get { return wantsPointingCursor; }
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

    // The cassette inside the player (null = empty).
    public CassetteItem CurrentCassette
    {
        get { return currentCassette; }
    }

    public bool HasCassette
    {
        get { return currentCassette != null; }
    }

    private Transform Socket
    {
        get { return cassetteSocket != null ? cassetteSocket : casettePlayer.transform; }
    }

    // The highest priority virtual camera, i.e. the one the brain is showing.
    private ICinemachineCamera ActiveCamera
    {
        get { return cinemachineBrain != null ? cinemachineBrain.ActiveVirtualCamera : null; }
    }

    // ================================================================ PLAYER

    // Called by CursorCameraManager when C_CasettePlayer is clicked.
    public void Inspect()
    {
        if (!CanInspect || ActiveCamera == null)
        {
            return;
        }

        originalParent = casettePlayer.transform.parent;
        originalLocalPosition = casettePlayer.transform.localPosition;
        originalLocalRotation = casettePlayer.transform.localRotation;
        originalLocalScale = casettePlayer.transform.localScale;

        StartCoroutine(PickUpPlayerRoutine());
    }

    private IEnumerator PickUpPlayerRoutine()
    {
        state = State.PlayerMovingIn;

        pivot = CreatePivot(casettePlayer.transform, "CasettePlayer Pivot", true);
        focusTarget = pivot;
        StartBlurFade(1f);

        Quaternion facing = PlayerFrontPose(out Vector3 front);
        yield return MovePivot(pivot, front, facing, moveInDuration);

        // Stays in front of the camera until it is clicked.
        SetPickupMessage(true);
        state = State.PlayerWaiting;

        while (!ClickedOn(IsPlayerCollider))
        {
            UpdateRotation(pivot, facing);
            yield return null;
        }

        SetPickupMessage(false);
        state = State.PlayerToCorner;

        yield return MovePivot(pivot, CornerPosition(), facing, moveToCornerDuration);

        StartBlurFade(0f);
        state = State.Held;
    }

    // ================================================================ CASSETTES

    // True when this cassette in the room may be clicked right now.
    public bool CanPickUpCassette(CassetteItem cassette)
    {
        return state == State.Held && cassette != null && cassette.IsInWorld;
    }

    // Cassettes collected so far: the ones next to the player plus the one inside it.
    public int CollectedCount
    {
        get { return inventory.Count + (currentCassette != null ? 1 : 0); }
    }

    // The collected cassettes next to the held player, in the order they were collected.
    public IReadOnlyList<CassetteItem> Inventory
    {
        get { return inventory; }
    }

    // The inventory cassette that a collider belongs to, as far as clicking goes
    // (folded stack: always the front cassette). Null if it isn't one / can't be clicked now.
    public CassetteItem InventoryCassetteFor(Collider c)
    {
        if (state != State.Held || c == null)
        {
            return null;
        }

        foreach (CassetteItem item in inventory)
        {
            if (item.IsMyCollider(c))
            {
                return inventoryExpanded ? item : inventory[0];
            }
        }

        return null;
    }

    // Called by CursorCameraManager every frame the cursor is over an inventory cassette (makes it rise).
    public void NotifyInventoryCassetteHovered(CassetteItem item)
    {
        hoveredInventoryCassette = item;
        inventoryHoverFrame = Time.frameCount;
    }

    // Called by CursorCameraManager when an inventory cassette is clicked.
    //   One cassette, or the stack is fanned out: that cassette goes into the player.
    //   Several cassettes, stack folded: the stack fans out (the others slide next to the first one).
    public void OnInventoryCassetteClicked(CassetteItem item)
    {
        if (state != State.Held || item == null || !inventory.Contains(item))
        {
            return;
        }

        if (inventory.Count > 1 && !inventoryExpanded)
        {
            inventoryExpanded = true;
            return;
        }

        inventoryExpanded = false;
        inventory.Remove(item);

        Transform itemPivot = slotPivots[item];
        slotPivots.Remove(item);

        StartCoroutine(InsertRoutine(item, itemPivot));
    }

    // Called by CursorCameraManager when a cassette in the room is clicked.
    public void PickUpCassette(CassetteItem cassette)
    {
        if (!CanPickUpCassette(cassette) || ActiveCamera == null)
        {
            return;
        }

        inventoryExpanded = false;
        StartCoroutine(CassetteRoutine(cassette));
    }

    public void EjectCassette()
    {
        if (state != State.Held || currentCassette == null)
        {
            return;
        }

        StartCoroutine(EjectInCornerRoutine());
    }

    private IEnumerator CassetteRoutine(CassetteItem cassette)
    {
        // ---- 1. the cassette floats in front of the camera and waits for a click
        state = State.CassetteMovingIn;
        pickedCassette = cassette;

        cassette.BeginPickUp();
        cassettePivot = CreatePivot(cassette.transform, "Cassette Pivot", false);
        focusTarget = cassettePivot;
        StartBlurFade(1f);

        Quaternion cassetteFacing = FrontRotation(cassette.inspectRotation);
        Vector3 cassetteFront = ToCameraSpace(ViewportToWorld(new Vector2(0.5f, 0.5f), cassetteInspectDistance));
        yield return MovePivot(cassettePivot, cassetteFront, cassetteFacing, moveInDuration);

        state = State.CassetteWaiting;

        while (!ClickedOn(cassette.IsMyCollider))
        {
            UpdateRotation(cassettePivot, cassetteFacing);
            yield return null;
        }

        // ---- 2. it joins the cassettes next to the held player (behind the first one)
        state = State.Busy;
        pickedCassette = null;
        StartBlurFade(0f);

        Transform itemPivot = cassettePivot;
        cassettePivot = null;

        yield return MovePivot(itemPivot, SlotPosition(inventory.Count, false), cassetteFacing, moveToCornerDuration);

        AddToInventory(cassette, itemPivot);
        focusTarget = pivot;
        state = State.Held;
    }

    private void AddToInventory(CassetteItem cassette, Transform itemPivot)
    {
        itemPivot.name = "Cassette Slot (" + cassette.name + ")";
        cassette.SetCollidersEnabled(true);

        inventory.Add(cassette);
        slotPivots[cassette] = itemPivot;

        UpdateCounter();
    }

    private IEnumerator InsertRoutine(CassetteItem cassette, Transform itemPivot)
    {
        // ---- 3. the player and the cassette come in front of the camera
        state = State.Busy;
        cassette.SetCollidersEnabled(false);
        focusTarget = pivot;
        StartBlurFade(1f);

        Quaternion cassetteFacing = FrontRotation(cassette.inspectRotation);
        Quaternion playerFacing = PlayerFrontPose(out Vector3 playerFront);
        Coroutine playerIn = StartCoroutine(MovePivot(pivot, playerFront, playerFacing, moveInDuration));

        if (currentCassette != null)
        {
            // Out of the way while the old cassette comes out.
            Vector3 wait = ToCameraSpace(ViewportToWorld(cassetteWaitViewportPosition, cassetteInspectDistance));
            yield return MovePivot(itemPivot, wait, cassetteFacing, moveInDuration);
            yield return playerIn;

            // ---- 3b. eject the cassette that is inside (it goes back next to the player)
            yield return EjectCurrent();
        }
        else
        {
            yield return playerIn;
        }

        // ---- 4. the cassette flies next to the player (first frame of Insert)
        Transform cassetteTransform = cassette.transform;
        cassetteTransform.SetParent(Socket, true);
        DestroyPivot(ref itemPivot);

        yield return cassette.FlyLocal(Vector3.zero, Quaternion.identity, Vector3.one, cassetteFlyDuration);

        // ---- 5. lid opens, cassette goes in
        Coroutine lid = StartCoroutine(LidClips().Play(playerInsertClip));
        yield return cassette.PlayInsert();
        yield return lid;

        currentCassette = cassette;
        UpdateCounter();

        // ---- 6. the player, with the cassette, goes back to the corner
        yield return MovePivot(pivot, CornerPosition(), playerFacing, moveToCornerDuration);

        StartBlurFade(0f);
        state = State.Held;
    }

    // Lid + cassette Eject, then the cassette goes back to the cassettes next to the player.
    private IEnumerator EjectCurrent()
    {
        CassetteItem old = currentCassette;
        currentCassette = null;

        Coroutine lid = StartCoroutine(LidClips().Play(playerEjectClip));
        yield return old.PlayEject();
        yield return lid;

        Transform oldPivot = CreatePivot(old.transform, "Cassette Slot", false);
        yield return MovePivot(oldPivot, SlotPosition(inventory.Count, false), FrontRotation(old.inspectRotation),
                               cassetteFlyDuration);

        AddToInventory(old, oldPivot);
    }

    private IEnumerator EjectInCornerRoutine()
    {
        state = State.Busy;
        inventoryExpanded = false;
        yield return EjectCurrent();
        state = State.Held;
    }

    // Camera-space position of inventory slot "index".
    //   Folded: every cassette at the first slot, each one a bit further back (hidden behind the first).
    //   Fanned out: side by side, "Cassette Slot Spacing" apart.
    private Vector3 SlotPosition(int index, bool expanded)
    {
        Vector2 viewport = cassetteReadyViewportPosition + (expanded ? cassetteSlotSpacing * index : Vector2.zero);
        float depth = cornerDistance + (expanded ? 0f : stackDepthOffset * index);

        return ToCameraSpace(ViewportToWorld(viewport, depth));
    }

    // Slides the inventory cassettes to their slots and raises the hovered one. Runs every frame while held,
    // so the slot settings can be tuned live in play mode.
    private void UpdateInventoryLayout()
    {
        if (state != State.Held || inventory.Count == 0 || ActiveCamera == null)
        {
            return;
        }

        bool hoverActive = Time.frameCount - inventoryHoverFrame <= 1;
        float k = 1f - Mathf.Exp(-hoverRiseSpeed * Time.deltaTime);

        for (int i = 0; i < inventory.Count; i++)
        {
            CassetteItem item = inventory[i];
            Transform p = slotPivots[item];

            bool hovered = hoverActive && hoveredInventoryCassette == item;
            Vector3 target = SlotPosition(i, inventoryExpanded) + (hovered ? Vector3.up * hoverRiseDistance : Vector3.zero);

            p.localPosition = Vector3.Lerp(p.localPosition, target, k);
            p.localRotation = Quaternion.Slerp(p.localRotation, FrontRotation(item.inspectRotation), k);
        }
    }

    private void UpdateCounter()
    {
        FindCounter();

        if (cassetteCounterNumber != null)
        {
            cassetteCounterNumber.text = string.Format(cassetteCounterFormat, CollectedCount);
        }

        if (cassetteCounter != null)
        {
            cassetteCounter.SetActive(!showCounterOnlyAfterPickup || CollectedCount > 0);
        }
    }

    private void FindCounter()
    {
        if (cassetteCounter == null)
        {
            cassetteCounter = FindSceneObject("CassetteCounter") ?? FindSceneObject("Cassette_Counter");

            if (cassetteCounter == null)
            {
                Debug.LogWarning("CasettePlayerInspect: no CassetteCounter UI found. Assign it under Cassette Counter.");
            }
        }

        if (cassetteCounterNumber == null && cassetteCounter != null)
        {
            foreach (TMP_Text t in cassetteCounter.GetComponentsInChildren<TMP_Text>(true))
            {
                if (t.name == "Cassette_Counter_Number")
                {
                    cassetteCounterNumber = t;
                    break;
                }
            }

            if (cassetteCounterNumber == null)
            {
                cassetteCounterNumber = cassetteCounter.GetComponentInChildren<TMP_Text>(true);
            }
        }
    }

    // Finds an object by name in the loaded scenes, inactive ones included.
    private static GameObject FindSceneObject(string objectName)
    {
        foreach (Transform t in FindObjectsByType<Transform>(FindObjectsInactive.Include, FindObjectsSortMode.None))
        {
            if (t.name == objectName && t.gameObject.scene.IsValid())
            {
                return t.gameObject;
            }
        }

        return null;
    }

    // ================================================================ BACK BUTTON

    // Called by CursorCameraManager when the back button is pressed.
    public void OnBackPressed()
    {
        if (state == State.PlayerMovingIn || state == State.PlayerWaiting)
        {
            StopAllCoroutines(); // the pick-up routine and its moves
            blurFadeRoutine = null;
            SetPickupMessage(false);
            StartCoroutine(ReturnPlayerRoutine());
            return;
        }

        if (state == State.CassetteMovingIn || state == State.CassetteWaiting)
        {
            StopAllCoroutines();
            blurFadeRoutine = null;
            StartCoroutine(ReturnCassetteRoutine());
            return;
        }

        if (state == State.Held)
        {
            inventoryExpanded = false;
        }
    }

    private IEnumerator ReturnCassetteRoutine()
    {
        state = State.Busy;
        StartBlurFade(0f);

        CassetteItem cassette = pickedCassette;
        pickedCassette = null;

        if (cassette != null)
        {
            Transform cassetteTransform = cassette.transform;
            cassetteTransform.SetParent(null, true);
            DestroyPivot(ref cassettePivot);

            yield return cassette.ReturnHome(cassetteFlyDuration);
        }

        focusTarget = pivot;
        state = State.Held;
    }

    // Pivot pose that puts the Casette Player at its original place in the drawer, as the drawer is right now.
    private void GetReturnPivotPose(Transform player, out Vector3 position, out Quaternion rotation)
    {
        Vector3 worldPosition = originalParent != null
            ? originalParent.TransformPoint(originalLocalPosition)
            : originalLocalPosition;

        Quaternion worldRotation = originalParent != null
            ? originalParent.rotation * originalLocalRotation
            : originalLocalRotation;

        rotation = worldRotation * Quaternion.Inverse(player.localRotation);
        position = worldPosition - rotation * Vector3.Scale(pivot.lossyScale, player.localPosition);
    }

    private IEnumerator ReturnPlayerRoutine()
    {
        state = State.Busy;
        StartBlurFade(0f);

        // Detach from the camera so it doesn't follow the view any more.
        pivot.SetParent(null, true);

        Transform player = casettePlayer.transform;
        Vector3 fromPosition = pivot.position;
        Quaternion fromRotation = pivot.rotation;
        float elapsed = 0f;

        while (elapsed < moveInDuration)
        {
            elapsed += Time.deltaTime;

            float t = EaseInOut(elapsed / moveInDuration);

            // Target is re-read every frame, so it follows the drawer while it closes.
            GetReturnPivotPose(player, out Vector3 toPosition, out Quaternion toRotation);

            pivot.SetPositionAndRotation(
                Vector3.Lerp(fromPosition, toPosition, t),
                Quaternion.Slerp(fromRotation, toRotation, t)
            );

            yield return null;
        }

        // Back in the drawer, exactly where it was.
        player.SetParent(originalParent, false);
        player.localPosition = originalLocalPosition;
        player.localRotation = originalLocalRotation;
        player.localScale = originalLocalScale;

        DestroyPivot(ref pivot);
        focusTarget = null;

        state = State.Idle;
    }

    // ================================================================ PUBLIC HELPERS

    public bool IsCasettePlayerCollider(Collider collider)
    {
        return casettePlayer != null &&
               collider != null &&
               collider.transform.IsChildOf(casettePlayer.transform) &&
               collider.GetComponentInParent<CassetteItem>() == null; // a cassette inside isn't the player
    }

    // Hides / shows the held Casette Player (e.g. while its cassette is inserted in the TV).
    public void SetHeldVisible(bool visible)
    {
        if (state == State.Held && pivot != null)
        {
            pivot.gameObject.SetActive(visible);

            // The cassettes waiting next to it hide with it.
            foreach (Transform slot in slotPivots.Values)
            {
                if (slot != null) slot.gameObject.SetActive(visible);
            }
        }
    }

    // ================================================================ INTERNALS

    private bool IsPlayerCollider(Collider c)
    {
        return IsCasettePlayerCollider(c);
    }

    // True on the frame the player clicks on something accepted by "isTarget" (not on UI).
    // Also keeps the cursor state up to date.
    private bool ClickedOn(System.Func<Collider, bool> isTarget)
    {
        bool over = PointerIsOver(isTarget);
        wantsPointingCursor = over;

        if (!over || Mouse.current == null || !Mouse.current.leftButton.wasPressedThisFrame || ClickGuard.IsLocked)
        {
            return false;
        }

        if (EventSystem.current != null && EventSystem.current.IsPointerOverGameObject())
        {
            return false;
        }

        wantsPointingCursor = false;
        ClickGuard.Lock(0.4f);
        return true;
    }

    // Raycasts from the camera through the cursor and checks every hit (the held object is close to the camera).
    private bool PointerIsOver(System.Func<Collider, bool> isTarget)
    {
        if (Mouse.current == null || cinemachineBrain == null)
        {
            return false;
        }

        Camera cam = cinemachineBrain.OutputCamera;
        if (cam == null)
        {
            return false;
        }

        Physics.SyncTransforms(); // the held objects move every frame without a Rigidbody

        Ray ray = cam.ScreenPointToRay(Mouse.current.position.ReadValue());
        RaycastHit[] hits = Physics.RaycastAll(ray, 100f, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Collide);

        foreach (RaycastHit hit in hits)
        {
            if (isTarget(hit.collider))
            {
                return true;
            }
        }

        return false;
    }

    private ClipPlayer LidClips()
    {
        if (lidClips == null)
        {
            Animator animator = Socket.GetComponent<Animator>();
            if (animator == null)
            {
                animator = Socket.gameObject.AddComponent<Animator>();
            }

            lidClips = new ClipPlayer(animator);
        }

        return lidClips;
    }

    private void SetPickupMessage(bool visible)
    {
        if (pickupMessage != null)
        {
            pickupMessage.SetActive(visible);
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
    private Vector3 ViewportToWorld(Vector2 viewport, float depth)
    {
        CameraState cameraState = ActiveCamera.State;

        return cameraState.FinalPosition +
               cameraState.FinalOrientation * (ViewportOffset(viewport, cameraState.Lens) * depth);
    }

    // Facing the camera (turned 180° around its up axis), plus an extra rotation, in camera space.
    private Quaternion FrontRotation(Vector3 extraRotation)
    {
        Quaternion world = ActiveCamera.State.FinalOrientation *
                           Quaternion.Euler(0f, 180f, 0f) *
                           Quaternion.Euler(extraRotation);

        return Quaternion.Inverse(cinemachineBrain.transform.rotation) * world;
    }

    private Quaternion PlayerFrontPose(out Vector3 position)
    {
        position = ToCameraSpace(ViewportToWorld(new Vector2(0.5f, 0.5f), inspectDistance));
        return FrontRotation(inspectRotation);
    }

    private Vector3 CornerPosition()
    {
        return ToCameraSpace(ViewportToWorld(cornerViewportPosition, cornerDistance));
    }

    private Vector3 ToCameraSpace(Vector3 worldPosition)
    {
        return cinemachineBrain.transform.InverseTransformPoint(worldPosition);
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

    // Puts a pivot at the visual centre of "target" and parents it to the camera.
    private Transform CreatePivot(Transform target, string pivotName, bool skipCassettes)
    {
        Bounds bounds = new Bounds(target.position, Vector3.zero);
        bool hasBounds = false;

        foreach (Renderer renderer in target.GetComponentsInChildren<Renderer>())
        {
            if (!renderer.enabled || (skipCassettes && renderer.GetComponentInParent<CassetteItem>() != null))
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

        Transform newPivot = new GameObject(pivotName).transform;
        newPivot.SetPositionAndRotation(bounds.center, target.rotation);

        target.SetParent(newPivot, true);
        newPivot.SetParent(cinemachineBrain.transform, true);

        return newPivot;
    }

    private static void DestroyPivot(ref Transform p)
    {
        if (p != null)
        {
            Destroy(p.gameObject);
            p = null;
        }
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

    // Moves a pivot in camera space with an ease-in / ease-out.
    private IEnumerator MovePivot(Transform p, Vector3 toPosition, Quaternion toRotation, float duration)
    {
        Vector3 fromPosition = p.localPosition;
        Quaternion fromRotation = p.localRotation;

        float elapsed = 0f;

        while (elapsed < duration)
        {
            elapsed += Time.deltaTime;

            float t = EaseInOut(elapsed / duration);

            p.localPosition = Vector3.Lerp(fromPosition, toPosition, t);
            p.localRotation = Quaternion.Slerp(fromRotation, toRotation, t);

            yield return null;
        }

        p.localPosition = toPosition;
        p.localRotation = toRotation;
    }

    private void OnDestroy()
    {
        if (blurProfile != null)
        {
            Destroy(blurProfile);
        }

        if (lidClips != null)
        {
            lidClips.Dispose();
        }
    }

    private void Start()
    {
        if (cinemachineBrain == null)
        {
            cinemachineBrain = FindFirstObjectByType<CinemachineBrain>();
        }

        if (cinemachineBrain == null)
        {
            Debug.LogWarning("CasettePlayerInspect: No CinemachineBrain assigned or found.");
        }

        if (casettePlayer == null)
        {
            Debug.LogWarning("CasettePlayerInspect: No Casette Player assigned.");
        }

        if (playerInsertClip == null || playerEjectClip == null)
        {
            Debug.LogWarning("CasettePlayerInspect: Player Insert / Eject clips (lid) are not assigned. " +
                             "Run Tools > Cassette > Update Cassette Animations.");
        }

        SetPickupMessage(false);
        UpdateCounter();
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

    private void Update()
    {
        if (!BlocksInput)
        {
            wantsPointingCursor = false;
        }

        // Keep the object in front of the camera in focus and blur everything nearer or further away.
        if (focusTarget != null && depthOfField != null && blurVolume.weight > 0f && ActiveCamera != null)
        {
            float distance = Vector3.Distance(ActiveCamera.State.FinalPosition, focusTarget.position);

            // No near blur: only things behind the held object blur. (The UI is rendered by the camera
            // close to the lens, so a near blur would blur it too.)
            depthOfField.nearFocusStart.Override(0f);
            depthOfField.nearFocusEnd.Override(0.001f);
            depthOfField.farFocusStart.Override(distance + focusMargin);
            depthOfField.farFocusEnd.Override(distance + focusMargin + blurFalloff);
        }

        UpdateInventoryLayout();

        if (ejectKey != Key.None &&
            Keyboard.current != null &&
            Keyboard.current[ejectKey].wasPressedThisFrame)
        {
            EjectCassette();
        }
    }

    // Turns the front of the object towards the cursor.
    // Cursor in the middle of the screen = facing the camera straight on.
    private void UpdateRotation(Transform p, Quaternion facingRotation)
    {
        if (Mouse.current == null || p == null)
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

        p.localRotation = Quaternion.Slerp(
            p.localRotation,
            targetRotation,
            1f - Mathf.Exp(-rotationSmoothing * Time.deltaTime)
        );
    }
}
