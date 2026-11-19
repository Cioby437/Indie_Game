using UnityEngine;
using System.Collections;
using System.Collections.Generic;

public class Entity_Rise : MonoBehaviour
{
    public enum MoveDirection
    {
        YPositive,
        YNegative,
        ZPositive,
        ZNegative
    }

    // Keeps track of every active Entity_Rise instance.
    public static List<Entity_Rise> AllEntities = new List<Entity_Rise>();

    [Header("Direction")]
    [Tooltip("World direction this entity moves in when it rises (towards the walls). " +
             "EntityY = YPositive, EntityYNegative = YNegative, etc.")]
    [SerializeField] private MoveDirection moveDirection = MoveDirection.YPositive;

    [Header("Movement")]
    [SerializeField] private float movementAmount = 0.15f;
    [Tooltip("Time it takes to slide one step.")]
    [SerializeField] private float moveDuration = 1f;

    [Header("Maximum Y Movement")]
    [SerializeField] private float maxYPositiveValue = 1.0f;
    [SerializeField] private float maxYNegativeValue = 1.0f;

    [Header("Maximum Z Movement")]
    [SerializeField] private float maxZPositiveValue = 1.0f;
    [SerializeField] private float maxZNegativeValue = 1.0f;

    [Header("Random Delay")]
    [SerializeField] private float minDelay = 0f;
    [SerializeField] private float maxDelay = 10f;

    private Vector3 startingPosition;
    private Coroutine movementCoroutine;

    // How far along moveDirection the entity should be, from its starting position.
    // 0 = starting position, never negative (stepping back stops at the start).
    private float targetOffset = 0f;

    private void OnEnable()
    {
        if (!AllEntities.Contains(this))
        {
            AllEntities.Add(this);
        }
    }

    private void OnDisable()
    {
        AllEntities.Remove(this);
    }

    private void Start()
    {
        // Remember the position when the entity is instantiated.
        startingPosition = transform.position;

    }

    // ---------------------------------------------------------
    // STATIC CONTROLS (called by ScenarioManager)
    // ---------------------------------------------------------

    // Wrong answer: every entity moves one step closer to the walls.
    public static void RiseAll()
    {
        foreach (Entity_Rise entity in AllEntities)
        {
            if (entity != null)
            {
                entity.Rise();
            }
        }
    }

    // Correct answer: every entity slides one step back.
    public static void StepBackAll()
    {
        foreach (Entity_Rise entity in AllEntities)
        {
            if (entity != null)
            {
                entity.StepBack();
            }
        }
    }

    // ---------------------------------------------------------
    // MOVEMENT
    // ---------------------------------------------------------

    public void Rise()
    {
        MoveToOffset(Mathf.Min(targetOffset + movementAmount, GetMaxOffset()));
    }

    public void StepBack()
    {
        MoveToOffset(Mathf.Max(targetOffset - movementAmount, 0f));
    }

    private void MoveToOffset(float offset)
    {
        targetOffset = offset;

        if (movementCoroutine != null)
        {
            StopCoroutine(movementCoroutine);
        }

        movementCoroutine = StartCoroutine(MoveRoutine());
    }

    private IEnumerator MoveRoutine()
    {
        // Each entity starts moving after its own random delay,
        // so they don't all move at the same time.
        yield return new WaitForSeconds(Random.Range(minDelay, maxDelay));

        Vector3 from = transform.position;
        Vector3 to = startingPosition + GetDirectionVector() * targetOffset;

        float elapsed = 0f;

        while (elapsed < moveDuration)
        {
            elapsed += Time.deltaTime;

            transform.position = Vector3.Lerp(
                from,
                to,
                Mathf.SmoothStep(0f, 1f, elapsed / moveDuration)
            );

            yield return null;
        }

        transform.position = to;
        movementCoroutine = null;
    }

    private Vector3 GetDirectionVector()
    {
        switch (moveDirection)
        {
            case MoveDirection.YPositive: return Vector3.up;
            case MoveDirection.YNegative: return Vector3.down;
            case MoveDirection.ZPositive: return Vector3.forward;
            default:                      return Vector3.back;
        }
    }

    private float GetMaxOffset()
    {
        switch (moveDirection)
        {
            case MoveDirection.YPositive: return maxYPositiveValue;
            case MoveDirection.YNegative: return maxYNegativeValue;
            case MoveDirection.ZPositive: return maxZPositiveValue;
            default:                      return maxZNegativeValue;
        }
    }

}
