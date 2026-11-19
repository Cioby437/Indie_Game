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

    public bool bed1Clicked = false;
    public bool bed2Clicked = false;

    [Header("Casette Player")]
    public CasettePlayerInspect casettePlayerInspect;

    [Header("Cinemachine Cameras")]
    public CinemachineVirtualCamera[] cinemachineCameras;
    public float debugRayWidth = 0.01f;

    [Header("Dialogue")]
    public DialogueManager dialogueManager;

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
    [Tooltip("Clicking the TV (while its camera is active) with the recorder in hand and a recording on it " +
             "inserts the cassette and opens the mini DAW.")]
    public TVMiniDaw tvMiniDaw;

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
    private bool isHoveringDoor = false;
    private bool isHoveringDrawer = false;
    private bool isHoveringDrawerCabinet1 = false;
    private bool isHoveringTV = false;

    private bool isHoveringWindow = false;

    [SerializeField] private Camera mainCamera;
    private CinemachineVirtualCamera outsideCamera;
    private CinemachineVirtualCamera tvCamera;

    private CinemachineVirtualCamera windowCamera;

    public event System.Action BedClicked;

    public void OnWindowButtonClicked()
    {
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

        bed1Clicked = true;

        BedClicked?.Invoke();
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

        bed2Clicked = true;

        BedClicked?.Invoke();
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
            tvCamera.Priority = activePriority;

            if (windowButton != null)
            {
                windowButton.gameObject.SetActive(true);
            }

            return;
        }

        // Insert the cassette: the mini DAW takes over the TV.
        if (tvMiniDaw != null &&
            tvMiniDaw.CanInsert &&
            (dialogueManager == null || !dialogueManager.IsDialogueActive))
        {
            ClearHoverStates();
            SetIdleCursor();

            tvMiniDaw.Insert();
            return;
        }

        if (dialogueManager != null)
        {
            dialogueManager.HandleTVClick();
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

        if (Physics.Raycast(
            ray,
            out RaycastHit hit,
            raycastDistance,
            Physics.DefaultRaycastLayers,
            QueryTriggerInteraction.Collide))
        {
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
            // C_DRAWER_CABINET_1
            // -------------------------------------------------

            if (IsDrawerOrCabinetCameraActive() &&
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
            // C_DRAWER
            // -------------------------------------------------

            if (hit.collider.CompareTag("C_Drawer"))
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

    private void ClearHoverStates()
    {
        isHoveringWindow = false;
        isHoveringTV = false;
        isHoveringDrawer = false;
        isHoveringDrawerCabinet1 = false;
        isHoveringCasettePlayer = false;
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

    private void LateUpdate()
    {
        // The Casette Player handles the mouse while it is being inspected.
        if (casettePlayerInspect != null && casettePlayerInspect.BlocksInput)
        {
            return;
        }

        // The mini DAW handles the mouse while the cassette is in the TV.
        if (tvMiniDaw != null && tvMiniDaw.BlocksInput)
        {
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
            if (animationScript != null)
            {
                animationScript.PlayAnimation(0);
            }

            drawerCamera.Priority = inactivePriority;
            drawerCabinetCamera1.Priority = activePriority;
        }
        else
        {
            if (animationScript != null)
            {
                animationScript.PlayAnimation(0);
            }

            drawerCabinetCamera1.Priority = inactivePriority;
            drawerCamera.Priority = activePriority;
        }
    }
}