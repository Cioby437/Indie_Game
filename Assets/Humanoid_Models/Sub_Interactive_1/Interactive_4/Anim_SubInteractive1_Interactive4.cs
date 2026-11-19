using System.Collections;
using UnityEngine;

[RequireComponent(typeof(Animator))]
public class RummageRunner : MonoBehaviour
{
    [Header("Animation")]
    [SerializeField] private Animator animator;
    private float baseX;
    private float baseZ;
    [SerializeField] private float crossFadeTime = 0.15f;
    private int currentIndex;

    [Header("Y Rotations")]
    [SerializeField] private float defaultY = 90f;
    [SerializeField] private float leftY = -5f;
    [SerializeField] private float maxRummageTime = 5f;

    [Header("Timing")]
    [SerializeField] private float minRummageTime = 2f;

    [Header("Movement")]
    [SerializeField] private float moveSpeed = 3f;

    [Header("Points (left to right)")]
    [SerializeField] private Transform point1;
    [SerializeField] private Transform point2;
    [SerializeField] private Transform point3;

    private Transform[] points;
    [SerializeField] private float rightY = 180f;
    [SerializeField] private string rummagingState = "Rummaging";
    [SerializeField] private string runningState = "Running";
    [SerializeField] private float turnSpeed = 900f; // degrees per second

    private void Awake()
    {
        if (animator == null) animator = GetComponent<Animator>();
        animator.applyRootMotion = false;

        points = new[] { point1, point2, point3 };

        Vector3 euler = transform.eulerAngles;
        baseX = euler.x;
        baseZ = euler.z;
    }

    private IEnumerator BehaviourLoop()
    {
        while (true)
        {
            // Rummage for a random amount of time
            yield return new WaitForSeconds(Random.Range(minRummageTime, maxRummageTime));

            // Pick a different point and work out which way to face
            int targetIndex = PickOtherPointIndex();
            float targetY = targetIndex > currentIndex ? rightY : leftY;
            Quaternion targetRotation = RotationWithY(targetY);

            Vector3 destination = points[targetIndex].position;
            destination.y = transform.position.y;

            // Run
            animator.CrossFade(runningState, crossFadeTime);

            while ((transform.position - destination).sqrMagnitude > 0.0001f)
            {
                transform.rotation = Quaternion.RotateTowards(
                    transform.rotation, targetRotation, turnSpeed * Time.deltaTime);
                transform.position = Vector3.MoveTowards(
                    transform.position, destination, moveSpeed * Time.deltaTime);
                yield return null;
            }

            transform.position = destination;
            currentIndex = targetIndex;

            // Back to rummaging, facing the default direction
            animator.CrossFade(rummagingState, crossFadeTime);
            transform.rotation = RotationWithY(defaultY);
        }
    }

    private int GetNearestPointIndex()
    {
        int nearest = 0;
        float bestDistance = float.MaxValue;
        for (int i = 0; i < points.Length; i++)
        {
            float d = (points[i].position - transform.position).sqrMagnitude;
            if (d < bestDistance)
            {
                bestDistance = d;
                nearest = i;
            }
        }
        return nearest;
    }

    private int PickOtherPointIndex()
    {
        int offset = Random.Range(1, points.Length);
        return (currentIndex + offset) % points.Length;
    }

    private Quaternion RotationWithY(float y)
    {
        return Quaternion.Euler(baseX, y, baseZ);
    }

    private void Start()
    {
        currentIndex = GetNearestPointIndex();
        transform.rotation = RotationWithY(defaultY);
        animator.Play(rummagingState, 0, 0f);
        StartCoroutine(BehaviourLoop());
    }
}
