using Cinemachine;
using FMOD.Studio;
using FMODUnity;
using UnityEngine;

// Runs before StudioEventEmitter so the parameter values are already set
// when an emitter with "Play On: Object Enable" starts its event.
[DefaultExecutionOrder(-100)]
public class FMOD_Spatializer : MonoBehaviour
{
    private const float DistanceMax = 10f;

    [Header("FMOD Parameter Names")]
    [SerializeField] private string distanceParameter = "Distance";

    private float distanceValue;
    [Tooltip("Leave empty to use the StudioEventEmitter on this object.")]
    [SerializeField] private StudioEventEmitter emitter;
    [SerializeField] private string leftParameter = "Distance_L";
    private float leftValue;
    [Tooltip("Leave empty to use whichever Cinemachine virtual camera is currently live.")]
    [SerializeField] private CinemachineVirtualCamera listenerCamera;
    [Tooltip("At or above this distance, Distance = 10.")]
    [SerializeField] private float maxDistance = 20f;

    [Header("Distance")]
    [Tooltip("At or below this distance, Distance = 0.")]
    [SerializeField] private float minDistance = 0f;
    [Header("References")]
    [Tooltip("Leave empty to use the Player_Controller in the scene.")]
    [SerializeField] private Transform player;
    [SerializeField] private string rightParameter = "Distance_R";
    private float rightValue;
    [Tooltip("Max value of Distance_R / Distance_L in FMOD, reached when the sound is directly behind (180 degrees).")]
    [SerializeField] private float sideParameterMax = 5f;

    // For instances created outside the emitter (e.g. with RuntimeManager.CreateInstance).
    // Call it every frame while the instance plays; it works even if this object is inactive.
    public void ApplyTo(EventInstance instance)
    {
        FindPlayerIfMissing();

        if (player == null || !instance.isValid())
        {
            return;
        }

        ComputeValues();
        SetInstanceParameters(instance);
    }

    private void Awake()
    {
        if (emitter == null)
        {
            emitter = GetComponent<StudioEventEmitter>();
        }
    }

    private void ComputeValues()
    {
        Vector3 toObject = transform.position - player.position;

        // Distance: 0 (close) to 10 (far).
        float normalizedDistance = Mathf.InverseLerp(minDistance, maxDistance, toObject.magnitude);
        distanceValue = normalizedDistance * DistanceMax;

        // Angle around the head, in the camera's own horizontal plane, so head pitch and roll
        // rotate the listening angle too: 0 = in front, +90 = right, -90 = left, +/-180 = behind.
        Vector3 localDirection = Quaternion.Inverse(GetListenerOrientation()) * toObject;

        float angle = 0f;
        if (localDirection.x * localDirection.x + localDirection.z * localDirection.z > 0.0001f)
        {
            angle = Mathf.Atan2(localDirection.x, localDirection.z) * Mathf.Rad2Deg;
        }

        // Distance_R covers 0 to +180 degrees, Distance_L covers 0 to -180 degrees.
        float sideAmount = Mathf.Abs(angle) / 180f * sideParameterMax;

        rightValue = angle > 0f ? sideAmount : 0f;
        leftValue = angle < 0f ? sideAmount : 0f;
    }

    private void FindPlayerIfMissing()
    {
        if (player != null)
        {
            return;
        }

        Player_Controller controller = FindFirstObjectByType<Player_Controller>();

        if (controller != null)
        {
            player = controller.transform;
        }
        else
        {
            Debug.LogWarning("FMOD_Spatializer: No player found. Assign the player in the Inspector.", this);
        }
    }

    private Quaternion GetListenerOrientation()
    {
        if (listenerCamera != null)
        {
            return listenerCamera.State.FinalOrientation;
        }

        CinemachineBrain brain = CinemachineCore.Instance.BrainCount > 0
            ? CinemachineCore.Instance.GetActiveBrain(0)
            : null;

        if (brain != null && brain.ActiveVirtualCamera != null)
        {
            return brain.ActiveVirtualCamera.State.FinalOrientation;
        }

        return player.rotation;
    }

    private void OnEnable()
    {
        FindPlayerIfMissing();
        UpdateParameters();
    }

    private void SetInstanceParameters(EventInstance instance)
    {
        instance.setParameterByName(distanceParameter, distanceValue);
        instance.setParameterByName(rightParameter, rightValue);
        instance.setParameterByName(leftParameter, leftValue);
    }

    private void Update()
    {
        UpdateParameters();
    }

    private void UpdateParameters()
    {
        if (player == null)
        {
            return;
        }

        ComputeValues();

        if (emitter != null)
        {
            // SetParameter also stores the value, so it is applied when the emitter next starts.
            emitter.SetParameter(distanceParameter, distanceValue);
            emitter.SetParameter(rightParameter, rightValue);
            emitter.SetParameter(leftParameter, leftValue);
        }
    }
}
