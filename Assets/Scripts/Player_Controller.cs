using UnityEngine;
using UnityEngine.InputSystem;

public class Player_Controller : MonoBehaviour
{
    [Header("Movement")]
    [SerializeField] private float moveSpeed = 5f;

    [Header("Camera")]
    [SerializeField] private float mouseSensitivity = 180f;
    [SerializeField] private float maxLookAngle = 80f;

    private CharacterController characterController;
    private Transform cameraTransform;
    private float pitch;

    private InputActionMap playerActions;
    private InputAction moveAction;
    private InputAction lookAction;

    private void Awake()
    {
        characterController = GetComponent<CharacterController>();
        if (characterController == null)
        {
            characterController = gameObject.AddComponent<CharacterController>();
        }

        cameraTransform = GetComponentInChildren<Camera>()?.transform;
        if (cameraTransform == null)
        {
            Debug.LogWarning("No camera was found under this object. Add a camera as a child of the player capsule.");
        }

        playerActions = new InputActionMap("Player");

        moveAction = playerActions.AddAction("Move");
        moveAction.AddCompositeBinding("2DVector")
            .With("Up", "<Keyboard>/w")
            .With("Down", "<Keyboard>/s")
            .With("Left", "<Keyboard>/a")
            .With("Right", "<Keyboard>/d");

        lookAction = playerActions.AddAction("Look", binding: "<Mouse>/delta");
    }

    private void OnEnable()
    {
        playerActions.Enable();
        Cursor.lockState = CursorLockMode.Locked;
        Cursor.visible = false;
    }

    private void OnDisable()
    {
        playerActions.Disable();
        Cursor.lockState = CursorLockMode.None;
        Cursor.visible = true;
    }

    private void Update()
    {
        HandleMovement();
        HandleLook();
    }

    private void HandleMovement()
    {
        Vector2 input = moveAction.ReadValue<Vector2>();
        Vector3 moveDirection = transform.forward * input.y + transform.right * input.x;
        moveDirection = Vector3.ProjectOnPlane(moveDirection, Vector3.up).normalized;

        if (moveDirection.sqrMagnitude > 0f)
        {
            characterController.Move(moveDirection * moveSpeed * Time.deltaTime);
        }
    }

    private void HandleLook()
    {
        if (cameraTransform == null)
        {
            return;
        }

        Vector2 mouseDelta = lookAction.ReadValue<Vector2>();
        float lookX = mouseDelta.x * mouseSensitivity * Time.deltaTime;
        float lookY = mouseDelta.y * mouseSensitivity * Time.deltaTime;

        transform.Rotate(Vector3.up, lookX);

        pitch -= lookY;
        pitch = Mathf.Clamp(pitch, -maxLookAngle, maxLookAngle);
        cameraTransform.localRotation = Quaternion.Euler(pitch, 0f, 0f);
    }
}
