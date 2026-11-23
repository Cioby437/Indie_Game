using Cinemachine;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.UI;

public class CursorCameraManager : MonoBehaviour
{

    [Header("Camera Priority")]
    public int activePriority = 10;

    [Header("Animation")]
    public AnimationScript animationScript;


    [Header("Casette Player")]
    public CasettePlayerInspect casettePlayerInspect;

    [Header("Inspectable Objects")]
    [Tooltip("Picks up and shows objects with an InspectableObject (e.g. the office notepad). " +
             "Empty = found in the scene, or created at start.")]
    public ObjectInspector objectInspector;

    [Header("Cinemachine Cameras")]
    public CinemachineVirtualCamera[] cinemachineCameras;
    public float debugRayWidth = 0.01f;

    [Tooltip("When ticked, the cabinet drawer (C_Drawer_Cabinet_1) can also be clicked from the PlayerCamera. " +
             "Off: clicking the drawer unit from the PlayerCamera always goes to the drawer camera first.")]
    public bool cabinetClickableFromMainView = false;

    [Tooltip("At start, the C_Drawer trigger is grown to cover the whole drawer unit (the AnimationScript's model), " +
             "so a click anywhere on it from the PlayerCamera goes to the drawer camera.")]
    public bool fitDrawerColliderToModel = true;

    [Tooltip("Animation State Name (in the AnimationScript) played in reverse when BTN_Back is pressed.")]
    public string drawerAnimationStateName = "WoodenTable_03_drawer01|WoodenTable_03_drawer01Action";

    [Header("Cursor")]
    public Texture2D idleCursor;
    public int inactivePriority = 0;
    public Texture2D pointingCursor;

    [Header("Raycast")]
    public float raycastDistance = 1000f;
    public bool showDebugRay = true;

    [Header("TV Mini DAW")]
    [Tooltip("Clicking the TV (while its camera is active) with the cassette player in hand and a heard cassette " +
             "in it puts the cassette in the TV and opens the mini DAW.")]
    public TVMiniDaw tvMiniDaw;

    [Tooltip("Logs every click: what it hit and why it was ignored. Turn off when everything works.")]
    public bool debugClicks = true;

    [Header("Click Guard")]
    [Tooltip("After a click on something clickable (or on the back button), every further click is ignored " +
             "for at least this many seconds, and for as long as the camera is still blending. " +
             "Stops fast double clicks from registering twice.")]
    public float clickCooldown = 0.4f;
    private CinemachineBrain cinemachineBrain;
    private CanvasGroup windowButtonGroup;

    [Header("UI")]
    public Button windowButton;
    private CinemachineVirtualCamera bedCamera1;
    private CinemachineVirtualCamera bedCamera2;

    private LineRenderer debugRay;
    private CinemachineVirtualCamera doorCamera;
    private CinemachineVirtualCamera drawerCabinetCamera1;
    private CinemachineVirtualCamera drawerCamera;
    private bool isHoveringBed1 = false;
    private bool isHoveringBed2 = false;
    private bool isHoveringCasettePlayer = false;
    private bool isHoveringHeldPlayer = false;
    private InspectableObject hoveredInspectable;
    private bool isHoveringCassette = false;
    private bool isHoveringInventoryCassette = false;
    private ClickableAnimation hoveredAnimation;
    private CassetteItem hoveredInventoryCassette;
    private CassetteItem hoveredCassette;
    private bool isHoveringDoor = false;
    private bool isHoveringDrawer = false;
    private bool isHoveringDrawerCabinet1 = false;
    private bool isHoveringTV = false;

    private bool isHoveringWindow = false;

    [SerializeField] private Camera mainCamera;
    private CinemachineVirtualCamera outsideCamera;
    private CinemachineVirtualCamera tvCamera;

    private CinemachineVirtualCamera windowCamera;


    public void OnWindowButtonClicked()
    {
        // Only the first click of a fast double click counts.
        if (IsClickLocked())
        {
            return;
        }

        LockClicks();

        // Leaving while the Casette Player is being picked up puts it back (or lets it finish into the corner).
        if (casettePlayerInspect != null)
        {
            casettePlayerInspect.OnBackPressed();
        }

        // An object being looked at goes back to its place.
        if (objectInspector != null)
        {
            objectInspector.OnBackPressed();
        }

        // Leaving the TV ejects the cassette.
        if (tvMiniDaw != null && tvMiniDaw.IsOpen)
        {
            tvMiniDaw.Eject();
        }

        if (windowCamera != null)
        {
            windowCamera.Priority = inactivePriority;
        }

        if (tvCamera != null)
        {
            tvCamera.Priority = inactivePriority;
        }

        if (drawerCamera != null)
        {
            drawerCamera.Priority = inactivePriority;
        }

        if (drawerCabinetCamera1 != null)
        {
            drawerCabinetCamera1.Priority = inactivePriority;
        }

        if (windowButton != null)
        {
            windowButton.gameObject.SetActive(false);
        }

        if (bedCamera1 != null)
        {
            bedCamera1.Priority = inactivePriority;
        }

        if (bedCamera2 != null)
        {
            bedCamera2.Priority = inactivePriority;
        }

        if (doorCamera != null)
        {
            doorCamera.Priority = inactivePriority;
        }

        if (outsideCamera != null)
        {
            outsideCamera.Priority = inactivePriority;
        }

        CloseDrawer();

        ClearHoverStates();

        SetIdleCursor();
    }

    private void ActivateBedCamera1()
    {
        if (bedCamera1 == null)
        {
            return;
        }

        bedCamera1.Priority = activePriority;

        if (windowButton != null)
        {
            windowButton.gameObject.SetActive(true);
        }
    }

    private void ActivateBedCamera2()
    {
        if (bedCamera2 == null)
        {
            return;
        }

        bedCamera2.Priority = activePriority;

        if (windowButton != null)
        {
            windowButton.gameObject.SetActive(true);
        }
    }

    private void ActivateDrawerCamera()
    {
        if (drawerCamera == null)
        {
            return;
        }

        drawerCamera.Priority = activePriority;

        if (windowButton != null)
        {
            windowButton.gameObject.SetActive(true);
        }
    }

    private void ActivateTVCamera()
    {
        if (tvCamera == null)
        {
            return;
        }

        if (tvCamera.Priority != activePriority)
        {
            if (debugClicks)
            {
                Debug.Log("[TV] First click: switching to the TV camera. Click the TV again to insert the cassette.");
            }

            tvCamera.Priority = activePriority;

            if (windowButton != null)
            {
                windowButton.gameObject.SetActive(true);
            }

            return;
        }

        if (debugClicks)
        {
            Debug.Log("[TV] Second click registered (TV camera already active).");

            if (tvMiniDaw == null)
            {
                Debug.LogWarning("[TV] The 'Tv Mini Daw' field on CursorCameraManager is empty, so the cassette can't be inserted.");
            }
            else
            {
                Debug.Log(tvMiniDaw.DebugStatus());
            }
        }

        // Insert the cassette: the mini DAW takes over the TV.
        if (tvMiniDaw != null &&
            tvMiniDaw.CanInsert)
        {
            ClearHoverStates();
            SetIdleCursor();

            tvMiniDaw.Insert();
            return;
        }

        if (tvMiniDaw != null && tvMiniDaw.InsertBlocker != null)
        {
            Debug.Log("TV Mini DAW: cassette not inserted because " + tvMiniDaw.InsertBlocker);
        }
    }

    private void ActivateWindowCamera()
    {
        if (windowCamera == null)
        {
            return;
        }

        windowCamera.Priority = activePriority;

        if (windowButton != null)
        {
            windowButton.gameObject.SetActive(true);
        }
    }

    private void CheckMouseClick()
    {
        if (Mouse.current == null)
        {
            return;
        }

        if (!Mouse.current.leftButton.wasPressedThisFrame)
        {
            return;
        }

        // Clicks on UI (e.g. the back button) must not reach objects behind it.
        if (EventSystem.current != null &&
            EventSystem.current.IsPointerOverGameObject())
        {
            if (debugClicks)
            {
                Debug.Log("[Click] Went to the UI, not the scene.");
            }

            return;
        }

        // Only the first click of a fast double click counts.
        if (IsClickLocked())
        {
            if (debugClicks)
            {
                Debug.Log("[Click] Ignored: repeated click (click guard / camera still blending).");
            }

            return;
        }

        if (debugClicks)
        {
            Debug.Log("[Click] Target: " + DescribeClickTarget());
        }

        if (IsHoveringAnything())
        {
            LockClicks();
        }

        // -------------------------------------------------
        // INVENTORY CASSETTE (next to the held player):
        // fan out the stack, or put the clicked one in
        // -------------------------------------------------

        if (isHoveringInventoryCassette)
        {
            CassetteItem clicked = hoveredInventoryCassette;

            ClearHoverStates();
            SetIdleCursor();

            casettePlayerInspect.OnInventoryCassetteClicked(clicked);
            return;
        }

        // -------------------------------------------------
        // HELD CASETTE PLAYER (with a cassette in it): play / stop the recording
        // -------------------------------------------------

        if (isHoveringHeldPlayer)
        {
            ClearHoverStates();
            SetIdleCursor();

            casettePlayerInspect.ToggleCassettePlayback();
            return;
        }

        // -------------------------------------------------
        // INSPECTABLE OBJECT (e.g. the notepad): pick it up and look at it
        // -------------------------------------------------

        if (hoveredInspectable != null)
        {
            InspectableObject inspectable = hoveredInspectable;

            ClearHoverStates();
            SetIdleCursor();

            objectInspector.Inspect(inspectable);
            return;
        }

        // -------------------------------------------------
        // CASSETTE IN THE ROOM: pick it up
        // -------------------------------------------------

        if (isHoveringCassette)
        {
            CassetteItem cassette = hoveredCassette;

            ClearHoverStates();
            SetIdleCursor();

            casettePlayerInspect.PickUpCassette(cassette);
            return;
        }

        // -------------------------------------------------
        // C_CASETTE_PLAYER
        // -------------------------------------------------

        if (isHoveringCasettePlayer)
        {
            ClearHoverStates();
            SetIdleCursor();

            casettePlayerInspect.Inspect();
            return;
        }

        // -------------------------------------------------
        // DRAWERS / DOORS WITH A ClickableAnimation: open / close
        // -------------------------------------------------

        if (hoveredAnimation != null)
        {
            ClickableAnimation part = hoveredAnimation;
            part.Toggle();
            return;
        }

        // -------------------------------------------------
        // C_DRAWER_CABINET_1
        // -------------------------------------------------

        if (isHoveringDrawerCabinet1)
        {
            ToggleDrawerCabinet1();
            return;
        }

        // -------------------------------------------------
        // C_WINDOW
        // -------------------------------------------------

        if (isHoveringWindow)
        {
            ActivateWindowCamera();
            return;
        }

        // -------------------------------------------------
        // C_TV
        // -------------------------------------------------

        if (isHoveringTV)
        {
            ActivateTVCamera();
            return;
        }

        // -------------------------------------------------
        // C_DRAWER
        // -------------------------------------------------

        if (isHoveringDrawer)
        {
            ActivateDrawerCamera();
            return;
        }

        // -------------------------------------------------
        // C_DOOR
        // -------------------------------------------------

        if (isHoveringDoor)
        {
            HandleDoorClick();
            return;
        }

        // -------------------------------------------------
        // C_Bed_1 and C_Bed_2
        // -------------------------------------------------

        if (isHoveringBed1)
        {
            ActivateBedCamera1();
            return;
        }

        if (isHoveringBed2)
        {
            ActivateBedCamera2();
            return;
        }
    }

    private void CheckMouseTarget()
    {
        if (Mouse.current == null)
        {
            return;
        }

        if (mainCamera == null)
        {
            return;
        }

        Vector2 mousePosition =
            Mouse.current.position.ReadValue();

        Ray ray =
            mainCamera.ScreenPointToRay(mousePosition);

        if (debugRay != null)
        {
            debugRay.SetPosition(0, ray.origin);

            debugRay.SetPosition(
                1,
                ray.origin + ray.direction * raycastDistance
            );
        }

        if (TryPickClickHit(ray, out RaycastHit hit))
        {
            // -------------------------------------------------
            // INVENTORY CASSETTE (next to the held player): rises while hovered
            // -------------------------------------------------

            CassetteItem inventoryCassette = casettePlayerInspect != null
                ? casettePlayerInspect.InventoryCassetteFor(hit.collider)
                : null;

            if (inventoryCassette != null)
            {
                ClearHoverStates();

                isHoveringInventoryCassette = true;
                hoveredInventoryCassette = inventoryCassette;
                casettePlayerInspect.NotifyInventoryCassetteHovered(inventoryCassette);

                SetPointingCursor();

                if (debugRay != null)
                {
                    debugRay.SetPosition(1, hit.point);
                }

                return;
            }

            // -------------------------------------------------
            // HELD CASETTE PLAYER with a cassette in it (click = play / stop)
            // -------------------------------------------------

            if (casettePlayerInspect != null &&
                casettePlayerInspect.CanPlayCassette &&
                casettePlayerInspect.IsHeldPlayerCollider(hit.collider))
            {
                ClearHoverStates();

                isHoveringHeldPlayer = true;
                casettePlayerInspect.NotifyHeldPlayerHovered();

                SetPointingCursor();

                if (debugRay != null)
                {
                    debugRay.SetPosition(1, hit.point);
                }

                return;
            }

            // -------------------------------------------------
            // INSPECTABLE OBJECT (e.g. the notepad)
            // -------------------------------------------------

            InspectableObject inspectable = hit.collider.GetComponentInParent<InspectableObject>();

            if (inspectable != null &&
                objectInspector != null &&
                objectInspector.CanInspect(inspectable))
            {
                ClearHoverStates();

                hoveredInspectable = inspectable;

                SetPointingCursor();

                if (debugRay != null)
                {
                    debugRay.SetPosition(1, hit.point);
                }

                return;
            }

            // -------------------------------------------------
            // CASSETTE (only while the Casette Player is held)
            // -------------------------------------------------

            CassetteItem cassette = hit.collider.GetComponentInParent<CassetteItem>();

            if (cassette != null &&
                casettePlayerInspect != null &&
                casettePlayerInspect.CanPickUpCassette(cassette))
            {
                ClearHoverStates();

                isHoveringCassette = true;
                hoveredCassette = cassette;

                SetPointingCursor();

                if (debugRay != null)
                {
                    debugRay.SetPosition(1, hit.point);
                }

                return;
            }

            // -------------------------------------------------
            // C_CASETTE_PLAYER
            // -------------------------------------------------

            if (casettePlayerInspect != null &&
                casettePlayerInspect.CanInspect &&
                casettePlayerInspect.IsCasettePlayerCollider(hit.collider))
            {
                ClearHoverStates();

                isHoveringCasettePlayer = true;

                SetPointingCursor();

                if (debugRay != null)
                {
                    debugRay.SetPosition(1, hit.point);
                }

                return;
            }

            // -------------------------------------------------
            // DRAWERS / DOORS WITH A ClickableAnimation
            // -------------------------------------------------

            ClickableAnimation part = hit.collider.GetComponentInParent<ClickableAnimation>();

            if (part != null && CanClickAnimation(part))
            {
                ClearHoverStates();

                hoveredAnimation = part;

                SetPointingCursor();

                if (debugRay != null)
                {
                    debugRay.SetPosition(1, hit.point);
                }

                return;
            }

            // -------------------------------------------------
            // C_DRAWER_CABINET_1
            // -------------------------------------------------

            if (CanClickDrawerCabinet1() &&
                hit.collider.CompareTag("C_Drawer_Cabinet_1"))
            {
                ClearHoverStates();

                isHoveringDrawerCabinet1 = true;

                SetPointingCursor();

                if (debugRay != null)
                {
                    debugRay.SetPosition(1, hit.point);
                }

                return;
            }

            // -------------------------------------------------
            // C_WINDOW
            // -------------------------------------------------

            if (hit.collider.CompareTag("C_Window"))
            {
                ClearHoverStates();

                isHoveringWindow = true;

                SetPointingCursor();

                if (debugRay != null)
                {
                    debugRay.SetPosition(1, hit.point);
                }

                return;
            }

            // -------------------------------------------------
            // C_TV
            // -------------------------------------------------

            if (hit.collider.CompareTag("C_TV"))
            {
                ClearHoverStates();

                isHoveringTV = true;

                SetPointingCursor();

                if (debugRay != null)
                {
                    debugRay.SetPosition(1, hit.point);
                }

                return;
            }

            // -------------------------------------------------
            // C_DRAWER (from the PlayerCamera, any part of the drawer unit counts as the drawer)
            // -------------------------------------------------

            bool drawerUnitFromMainView = !IsAnyViewCameraActive() && IsPartOfDrawerUnit(hit.collider);

            if ((hit.collider.CompareTag("C_Drawer") || drawerUnitFromMainView) &&
                !IsCabinetCameraActive())
            {
                ClearHoverStates();

                isHoveringDrawer = true;

                SetPointingCursor();

                if (debugRay != null)
                {
                    debugRay.SetPosition(1, hit.point);
                }

                return;
            }

            // -------------------------------------------------
            // C_DOOR
            // -------------------------------------------------

            if (hit.collider.CompareTag("C_Door"))
            {
                ClearHoverStates();

                isHoveringDoor = true;

                SetPointingCursor();

                if (debugRay != null)
                {
                    debugRay.SetPosition(1, hit.point);
                }

                return;
            }

            // -------------------------------------------------
            // C_Bed_1 and C_Bed_2
            // -------------------------------------------------
            if (hit.collider.CompareTag("C_Bed_1"))
            {
                ClearHoverStates();

                isHoveringBed1 = true;

                SetPointingCursor();

                if (debugRay != null)
                {
                    debugRay.SetPosition(1, hit.point);
                }

                return;
            }

            if (hit.collider.CompareTag("C_Bed_2"))
            {
                ClearHoverStates();

                isHoveringBed2 = true;

                SetPointingCursor();

                if (debugRay != null)
                {
                    debugRay.SetPosition(1, hit.point);
                }

                return;
            }
        }

        ClearHoverStates();
        SetIdleCursor();
    }

    // The cabinet drawer can be clicked from the drawer / cabinet views
    // (and from the PlayerCamera if "Cabinet Clickable From Main View" is ticked).
    private bool CanClickDrawerCabinet1()
    {
        return IsDrawerOrCabinetCameraActive() ||
               (cabinetClickableFromMainView && !IsAnyViewCameraActive());
    }

    // A collider that belongs to the drawer unit: its drawers / door (ClickableAnimation), the cabinet drawer,
    // or anything under the AnimationScript's model.
    private bool IsPartOfDrawerUnit(Collider c)
    {
        if (c.GetComponentInParent<ClickableAnimation>() != null || c.CompareTag("C_Drawer_Cabinet_1"))
        {
            return true;
        }

        return animationScript != null &&
               animationScript.animator != null &&
               c.transform.IsChildOf(animationScript.animator.transform);
    }

    // Grows the C_Drawer trigger so it covers the whole drawer unit (closed), plus a small margin.
    private void FitDrawerCollider()
    {
        if (!fitDrawerColliderToModel || animationScript == null || animationScript.animator == null)
        {
            return;
        }

        BoxCollider box = null;
        foreach (GameObject go in GameObject.FindGameObjectsWithTag("C_Drawer"))
        {
            box = go.GetComponent<BoxCollider>();
            if (box != null) break;
        }

        if (box == null)
        {
            return;
        }

        Bounds model = new Bounds();
        bool hasBounds = false;

        foreach (Renderer r in animationScript.animator.GetComponentsInChildren<Renderer>())
        {
            if (r.GetComponentInParent<CassetteItem>() != null) continue;
            if (casettePlayerInspect != null && casettePlayerInspect.casettePlayer != null &&
                r.transform.IsChildOf(casettePlayerInspect.casettePlayer.transform)) continue;

            if (hasBounds) model.Encapsulate(r.bounds);
            else { model = r.bounds; hasBounds = true; }
        }

        if (!hasBounds)
        {
            return;
        }

        model.Expand(0.04f);

        // Box space (C_Drawer is world-aligned; this also works if it is rotated, using its corners).
        Transform t = box.transform;
        Bounds local = new Bounds(t.InverseTransformPoint(model.center), Vector3.zero);
        Vector3 e = model.extents;

        for (int i = 0; i < 8; i++)
        {
            Vector3 corner = model.center + new Vector3((i & 1) == 0 ? -e.x : e.x,
                                                        (i & 2) == 0 ? -e.y : e.y,
                                                        (i & 4) == 0 ? -e.z : e.z);
            local.Encapsulate(t.InverseTransformPoint(corner));
        }

        // Keep what the box already covered, add the model.
        local.Encapsulate(new Bounds(box.center, box.size));

        box.center = local.center;
        box.size = local.size;

        if (debugClicks)
        {
            Debug.Log("[Click] C_Drawer trigger grown to cover the whole drawer unit.", box);
        }
    }

    // True while any of the close-up cameras has the priority, i.e. we are NOT looking through the PlayerCamera.
    private bool IsAnyViewCameraActive()
    {
        CinemachineVirtualCamera[] views =
        {
            windowCamera, tvCamera, drawerCamera, drawerCabinetCamera1,
            doorCamera, outsideCamera, bedCamera1, bedCamera2
        };

        foreach (CinemachineVirtualCamera view in views)
        {
            if (view != null && view.Priority == activePriority)
            {
                return true;
            }
        }

        return false;
    }

    private bool IsCabinetCameraActive()
    {
        return drawerCabinetCamera1 != null &&
               drawerCabinetCamera1.Priority == activePriority;
    }

    private bool IsHoveringAnything()
    {
        return hoveredAnimation != null || hoveredInspectable != null || isHoveringInventoryCassette || isHoveringHeldPlayer || isHoveringCassette || isHoveringCasettePlayer || isHoveringDrawerCabinet1 || isHoveringWindow || isHoveringTV ||
               isHoveringDrawer || isHoveringDoor || isHoveringBed1 || isHoveringBed2;
    }

    private bool IsClickLocked()
    {
        return ClickGuard.IsLocked ||
               (cinemachineBrain != null && cinemachineBrain.IsBlending);
    }

    private void LockClicks()
    {
        ClickGuard.Lock(clickCooldown);
    }

    // Normally the nearest hit wins. Exception: C_Drawer is a big trigger box around the whole drawer unit,
    // so from the PlayerCamera it is always hit before the C_Drawer_Cabinet_1 trigger inside it.
    // When the ray continues into C_Drawer_Cabinet_1, that one is used instead, unless something solid
    // outside the C_Drawer box is in the way.
    private bool TryPickClickHit(Ray ray, out RaycastHit picked)
    {
        Physics.SyncTransforms(); // objects held in front of the camera move without a Rigidbody

        RaycastHit[] hits = Physics.RaycastAll(
            ray,
            raycastDistance,
            Physics.DefaultRaycastLayers,
            QueryTriggerInteraction.Collide
        );

        picked = default;

        if (hits.Length == 0)
        {
            return false;
        }

        System.Array.Sort(hits, (a, b) => a.distance.CompareTo(b.distance));

        picked = hits[0];

        if (!hits[0].collider.CompareTag("C_Drawer"))
        {
            return true;
        }

        Bounds drawerBox = hits[0].collider.bounds;

        for (int i = 1; i < hits.Length; i++)
        {
            Collider other = hits[i].collider;

            if (IsClickableInsideDrawer(other))
            {
                picked = hits[i];
                return true;
            }

            // Something solid outside the drawer box hides the cabinet.
            if (!other.isTrigger && !drawerBox.Contains(hits[i].point))
            {
                break;
            }
        }

        return true;
    }

    private bool CanClickAnimation(ClickableAnimation part)
    {
        return !part.onlyFromDrawerViews || IsDrawerOrCabinetCameraActive();
    }

    // Things inside the C_Drawer box that should win over the box itself.
    private bool IsClickableInsideDrawer(Collider other)
    {
        ClickableAnimation part = other.GetComponentInParent<ClickableAnimation>();
        if (part != null && CanClickAnimation(part))
        {
            return true;
        }

        if (other.CompareTag("C_Drawer_Cabinet_1") && CanClickDrawerCabinet1())
        {
            return true;
        }

        InspectableObject inspectable = other.GetComponentInParent<InspectableObject>();
        if (inspectable != null && objectInspector != null && objectInspector.CanInspect(inspectable))
        {
            return true;
        }

        if (casettePlayerInspect == null)
        {
            return false;
        }

        CassetteItem cassette = other.GetComponentInParent<CassetteItem>();
        if (cassette != null)
        {
            return casettePlayerInspect.CanPickUpCassette(cassette);
        }

        return casettePlayerInspect.CanInspect && casettePlayerInspect.IsCasettePlayerCollider(other);
    }

    // While clicks are locked the back button lets clicks through (to the also-locked scene),
    // so a double click can't press it right after it appears. No visual change.
    private void UpdateWindowButtonGuard()
    {
        if (windowButtonGroup != null)
        {
            windowButtonGroup.blocksRaycasts = !IsClickLocked();
        }
    }

    private void ClearHoverStates()
    {
        isHoveringCassette = false;
        isHoveringInventoryCassette = false;
        hoveredInventoryCassette = null;
        hoveredAnimation = null;
        hoveredCassette = null;
        isHoveringWindow = false;
        isHoveringTV = false;
        isHoveringDrawer = false;
        isHoveringDrawerCabinet1 = false;
        isHoveringCasettePlayer = false;
        isHoveringHeldPlayer = false;
        hoveredInspectable = null;
        isHoveringDoor = false;
        isHoveringBed1 = false;
        isHoveringBed2 = false;
    }

    // Plays the drawer animation in reverse, but only if the drawer is open.
    private void CloseDrawer()
    {
        if (animationScript == null || animationScript.animations == null)
        {
            return;
        }

        for (int i = 0; i < animationScript.animations.Length; i++)
        {
            AnimationScript.AnimationData animation = animationScript.animations[i];

            if (animation == null ||
                animation.animationStateName != drawerAnimationStateName)
            {
                continue;
            }

            // A second PlayAnimation on an open animation plays it backwards.
            if (animationScript.IsAnimationOpen(i))
            {
                animationScript.PlayAnimation(i);
            }

            return;
        }

        Debug.LogWarning(
            "CursorCameraManager: No animation named '" + drawerAnimationStateName +
            "' found in the AnimationScript."
        );
    }

    private void CreateDebugRay()
    {
        if (!showDebugRay)
        {
            return;
        }

        GameObject rayObject = new GameObject("Cursor Ray Debug");

        debugRay = rayObject.AddComponent<LineRenderer>();

        debugRay.positionCount = 2;
        debugRay.startWidth = debugRayWidth;
        debugRay.endWidth = debugRayWidth;

        debugRay.material = new Material(Shader.Find("Sprites/Default"));

        debugRay.startColor = Color.red;
        debugRay.endColor = Color.red;
    }

    private void DoorTransition(
        CinemachineVirtualCamera from,
        CinemachineVirtualCamera to
    )
    {
        from.Priority = inactivePriority;
        to.Priority = activePriority;

        // The back button is only available while looking through C_Door.
        if (windowButton != null)
        {
            windowButton.gameObject.SetActive(to == doorCamera);
        }
    }

    private void FindBedCamera1()
    {
        if (cinemachineCameras == null ||
            cinemachineCameras.Length == 0)
        {
            return;
        }

        foreach (CinemachineVirtualCamera camera in cinemachineCameras)
        {
            if (camera != null && camera.CompareTag("C_Bed_1"))
            {
                bedCamera1 = camera;
                break;
            }
        }

        if (bedCamera1 == null)
        {
            Debug.LogWarning(
                "CursorCameraManager: No Cinemachine camera tagged 'C_Bed_1' was found."
            );
        }
    }

    private void FindBedCamera2()
    {
        if (cinemachineCameras == null ||
            cinemachineCameras.Length == 0)
        {
            return;
        }

        foreach (CinemachineVirtualCamera camera in cinemachineCameras)
        {
            if (camera != null && camera.CompareTag("C_Bed_2"))
            {
                bedCamera2 = camera;
                break;
            }
        }

        if (bedCamera2 == null)
        {
            Debug.LogWarning(
                "CursorCameraManager: No Cinemachine camera tagged 'C_Bed_2' was found."
            );
        }
    }

    private void FindDoorCamera()
    {
        if (cinemachineCameras == null ||
            cinemachineCameras.Length == 0)
        {
            return;
        }

        foreach (CinemachineVirtualCamera camera in cinemachineCameras)
        {
            if (camera != null && camera.CompareTag("C_Door"))
            {
                doorCamera = camera;
                break;
            }
        }

        if (doorCamera == null)
        {
            Debug.LogWarning(
                "CursorCameraManager: No Cinemachine camera tagged 'C_Door' was found."
            );
        }
    }

    private void FindDrawerCabinetCamera1()
    {
        if (cinemachineCameras == null ||
            cinemachineCameras.Length == 0)
        {
            return;
        }

        foreach (CinemachineVirtualCamera camera in cinemachineCameras)
        {
            if (camera != null &&
                camera.CompareTag("C_Drawer_Cabinet_1"))
            {
                drawerCabinetCamera1 = camera;
                break;
            }
        }

        if (drawerCabinetCamera1 == null)
        {
            Debug.LogWarning(
                "CursorCameraManager: No Cinemachine camera tagged 'C_Drawer_Cabinet_1' was found."
            );
        }
    }

    private void FindDrawerCamera()
    {
        if (cinemachineCameras == null ||
            cinemachineCameras.Length == 0)
        {
            return;
        }

        foreach (CinemachineVirtualCamera camera in cinemachineCameras)
        {
            if (camera != null && camera.CompareTag("C_Drawer"))
            {
                drawerCamera = camera;
                break;
            }
        }

        if (drawerCamera == null)
        {
            Debug.LogWarning(
                "CursorCameraManager: No Cinemachine camera tagged 'C_Drawer' was found."
            );
        }
    }

    private void FindOutsideCamera()
    {
        if (cinemachineCameras == null ||
            cinemachineCameras.Length == 0)
        {
            return;
        }

        foreach (CinemachineVirtualCamera camera in cinemachineCameras)
        {
            if (camera != null && camera.CompareTag("C_Outside"))
            {
                outsideCamera = camera;
                break;
            }
        }

        if (outsideCamera == null)
        {
            Debug.LogWarning(
                "CursorCameraManager: No Cinemachine camera tagged 'C_Outside' was found."
            );
        }
    }

    private void FindTVCamera()
    {
        if (cinemachineCameras == null ||
            cinemachineCameras.Length < 3)
        {
            Debug.LogWarning(
                "CursorCameraManager: A 3rd Cinemachine camera is required for the TV."
            );

            return;
        }

        tvCamera = cinemachineCameras[2];

        if (tvCamera == null)
        {
            Debug.LogWarning(
                "CursorCameraManager: The 3rd Cinemachine camera is null."
            );
        }
    }

    private void FindWindowCamera()
    {
        if (cinemachineCameras == null ||
            cinemachineCameras.Length == 0)
        {
            Debug.LogWarning(
                "CursorCameraManager: No Cinemachine cameras assigned."
            );

            return;
        }

        foreach (CinemachineVirtualCamera camera in cinemachineCameras)
        {
            if (camera != null && camera.CompareTag("C_Window"))
            {
                windowCamera = camera;
                break;
            }
        }

        if (windowCamera == null)
        {
            Debug.LogWarning(
                "CursorCameraManager: No Cinemachine camera tagged 'C_Window' was found."
            );
        }
    }

    // 1st click: go to C_Door.
    // Looking through C_Door: walk out to C_Outside.
    // Looking through C_Outside: walk back in to C_Door.
    private void HandleDoorClick()
    {
        if (doorCamera == null)
        {
            return;
        }

        if (doorCamera.Priority == activePriority && outsideCamera != null)
        {
            DoorTransition(doorCamera, outsideCamera);
            return;
        }

        if (outsideCamera != null && outsideCamera.Priority == activePriority)
        {
            DoorTransition(outsideCamera, doorCamera);
            return;
        }

        doorCamera.Priority = activePriority;

        if (windowButton != null)
        {
            windowButton.gameObject.SetActive(true);
        }
    }

    private bool IsDrawerOrCabinetCameraActive()
    {
        bool drawerActive =
            drawerCamera != null &&
            drawerCamera.Priority == activePriority;

        bool cabinetActive =
            drawerCabinetCamera1 != null &&
            drawerCabinetCamera1.Priority == activePriority;

        return drawerActive || cabinetActive;
    }

    // What the current click would hit, for the debug log.
    private string DescribeClickTarget()
    {
        if (hoveredAnimation != null) return "Animated part " + hoveredAnimation.name + " (" + hoveredAnimation.animationName + ")";
        if (isHoveringInventoryCassette) return "Inventory cassette " + (hoveredInventoryCassette != null ? hoveredInventoryCassette.name : "");
        if (isHoveringCassette) return "Cassette " + (hoveredCassette != null ? hoveredCassette.name : "");
        if (isHoveringCasettePlayer) return "Casette Player";
        if (hoveredInspectable != null) return "Inspectable object " + hoveredInspectable.name;
        if (isHoveringHeldPlayer) return "Held Casette Player (play / stop the cassette)";
        if (isHoveringDrawerCabinet1) return "Drawer Cabinet 1";
        if (isHoveringWindow) return "Window";
        if (isHoveringTV) return "TV";
        if (isHoveringDrawer) return "Drawer";
        if (isHoveringDoor) return "Door";
        if (isHoveringBed1) return "Bed 1";
        if (isHoveringBed2) return "Bed 2";

        if (mainCamera != null && Mouse.current != null)
        {
            Ray ray = mainCamera.ScreenPointToRay(Mouse.current.position.ReadValue());

            if (Physics.Raycast(ray, out RaycastHit hit, raycastDistance,
                    Physics.DefaultRaycastLayers, QueryTriggerInteraction.Collide))
            {
                return "nothing clickable (ray hit '" + hit.collider.name + "', tag '" + hit.collider.tag +
                       "'). If you meant the TV, this object is in front of it or the TV collider isn't tagged C_TV.";
            }
        }

        return "nothing (the ray hit no collider).";
    }

    private void LateUpdate()
    {
        UpdateWindowButtonGuard();

        bool clicked = debugClicks && Mouse.current != null && Mouse.current.leftButton.wasPressedThisFrame;

        // The Casette Player handles the mouse while it is being inspected.
        if (casettePlayerInspect != null && casettePlayerInspect.BlocksInput)
        {
            // ...but the cursor still shows when its object can be clicked.
            if (casettePlayerInspect.WantsPointingCursor)
            {
                SetPointingCursor();
            }
            else
            {
                SetIdleCursor();
            }

            if (clicked)
            {
                Debug.Log("[Click] Ignored: the Casette Player is being inspected (or this click puts it away).");
            }

            return;
        }

        // The inspector handles the mouse while an object (e.g. the notepad) is being looked at.
        if (objectInspector != null && objectInspector.BlocksInput)
        {
            SetIdleCursor();

            if (clicked)
            {
                Debug.Log("[Click] Handled by the Object Inspector (click outside the object to put it back).");
            }

            return;
        }

        // The mini DAW handles the mouse while the cassette is in the TV.
        if (tvMiniDaw != null && tvMiniDaw.BlocksInput)
        {
            if (clicked && !tvMiniDaw.IsOpen)
            {
                Debug.Log("[Click] Ignored: this click ejected the cassette.");
            }

            return;
        }

        CheckMouseTarget();
        CheckMouseClick();
    }

    private void OnDestroy()
    {
        if (windowButton != null)
        {
            windowButton.onClick.RemoveListener(
                OnWindowButtonClicked
            );
        }

        if (debugRay != null)
        {
            Destroy(debugRay.gameObject);
        }
    }

    private void SetIdleCursor()
    {
        if (idleCursor != null)
        {
            Cursor.SetCursor(
                idleCursor,
                Vector2.zero,
                CursorMode.Auto
            );
        }
    }

    private void SetPointingCursor()
    {
        if (pointingCursor != null)
        {
            Cursor.SetCursor(
                pointingCursor,
                Vector2.zero,
                CursorMode.Auto
            );
        }
    }

    private void Start()
    {
        SetIdleCursor();

        FindWindowCamera();
        FindTVCamera();
        FindDrawerCamera();
        FindDrawerCabinetCamera1();
        FindDoorCamera();
        FindOutsideCamera();
        FindBedCamera1();
        FindBedCamera2();

        if (windowCamera != null)
        {
            windowCamera.Priority = inactivePriority;
        }

        if (tvCamera != null)
        {
            tvCamera.Priority = inactivePriority;
        }

        if (drawerCamera != null)
        {
            drawerCamera.Priority = inactivePriority;
        }

        if (drawerCabinetCamera1 != null)
        {
            drawerCabinetCamera1.Priority = inactivePriority;
        }

        if (doorCamera != null)
        {
            doorCamera.Priority = inactivePriority;
        }

        if (outsideCamera != null)
        {
            outsideCamera.Priority = inactivePriority;
        }

        if (windowButton != null)
        {
            windowButton.gameObject.SetActive(false);
            windowButton.onClick.AddListener(OnWindowButtonClicked);
        }

        cinemachineBrain = mainCamera != null ? mainCamera.GetComponent<CinemachineBrain>() : null;

        if (cinemachineBrain == null)
        {
            cinemachineBrain = FindFirstObjectByType<CinemachineBrain>();
        }

        if (windowButton != null)
        {
            windowButtonGroup = windowButton.GetComponent<CanvasGroup>();

            if (windowButtonGroup == null)
            {
                windowButtonGroup = windowButton.gameObject.AddComponent<CanvasGroup>();
            }
        }

        if (objectInspector == null)
        {
            objectInspector = FindFirstObjectByType<ObjectInspector>();
        }

        if (objectInspector == null)
        {
            objectInspector = new GameObject("Object Inspector").AddComponent<ObjectInspector>();
        }

        FitDrawerCollider();

        CreateDebugRay();
    }

    private void ToggleDrawerCabinet1()
    {
        if (drawerCamera == null ||
            drawerCabinetCamera1 == null)
        {
            return;
        }

        bool cabinetIsActive =
            drawerCabinetCamera1.Priority == activePriority;

        if (!cabinetIsActive)
        {
            // Open it, unless it is already open.
            if (animationScript != null && !animationScript.IsAnimationOpen(0))
            {
                animationScript.PlayAnimation(0);
            }

            drawerCamera.Priority = inactivePriority;
            drawerCabinetCamera1.Priority = activePriority;

            // Also reachable straight from the PlayerCamera, so make sure the back button is there.
            if (windowButton != null)
            {
                windowButton.gameObject.SetActive(true);
            }
        }
        else
        {
            // Close it, unless it is already closed.
            if (animationScript != null && animationScript.IsAnimationOpen(0))
            {
                animationScript.PlayAnimation(0);
            }

            drawerCabinetCamera1.Priority = inactivePriority;
            drawerCamera.Priority = activePriority;
        }
    }
}