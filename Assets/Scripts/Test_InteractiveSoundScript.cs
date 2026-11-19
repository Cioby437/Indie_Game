using System.Collections;
using System.Collections.Generic;
using Unity.AI.Navigation;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.Events;

/// <summary>
/// Invisible "presence" that roams the NavMesh, flickers lights it passes through,
/// takes a passing interest in the player and occasionally rushes at them.
///
/// Setup: NavMeshAgent + trigger collider (e.g. SphereCollider) on this object.
/// A kinematic Rigidbody is added automatically: Unity only sends trigger events
/// when at least one of the two colliders has a Rigidbody.
/// </summary>
[RequireComponent(typeof(NavMeshAgent))]
[RequireComponent(typeof(Rigidbody))]
public class Test_InteractiveSoundScript : MonoBehaviour
{
    // ---------------------------------------------------------
    // INSPECTOR
    // ---------------------------------------------------------

    [Header("Patrol Area")]
    [Tooltip("The NavMesh Surface it patrols. Its baked bounds (or its Volume box when Collect Objects = Volume) limit where it goes. " +
             "If empty: a surface on this object, otherwise the active surface matching the agent type.")]
    [SerializeField] private NavMeshSurface navMeshSurface;
    [Tooltip("One patrol point per this many square metres of walkable surface.")]
    [SerializeField] private float squareMetersPerPatrolPoint = 4f;
    [SerializeField] private Vector2Int patrolPointCountLimits = new Vector2Int(8, 150);
    [Tooltip("A patrol point counts as visited when the presence passes this close to it.")]
    [SerializeField] private float visitRadius = 1.5f;

    [Header("Movement")]
    [SerializeField] private float walkSpeed = 2.2f;
    [SerializeField] private float walkAcceleration = 6f;
    [SerializeField] private float walkAngularSpeed = 240f;

    [Header("Idle Pauses")]
    [Tooltip("Chance to stop and look around after reaching a patrol point.")]
    [SerializeField, Range(0f, 1f)] private float pauseChance = 0.35f;
    [SerializeField] private Vector2 pauseDuration = new Vector2(1.5f, 4f);
    [Tooltip("Chance that the next destination is picked behind/beside it, instead of ahead.")]
    [SerializeField, Range(0f, 1f)] private float unexpectedTurnChance = 0.6f;

    [Header("Points Of Interest")]
    [Tooltip("Places where it lingers, as if something there interests it. If empty, a few random spots are chosen.")]
    [SerializeField] private Transform[] interestPoints;
    [SerializeField] private int autoInterestPointCount = 3;
    [SerializeField] private Vector2 interestLingerDuration = new Vector2(4f, 10f);
    [Tooltip("How far it drifts around while lingering at a point of interest.")]
    [SerializeField] private float interestWanderRadius = 1.2f;
    [Tooltip("Chance to visit a point of interest instead of a regular patrol point.")]
    [SerializeField, Range(0f, 1f)] private float interestVisitChance = 0.2f;

    [Header("Agitation (angry / searching)")]
    [SerializeField] private float agitatedSpeed = 5f;
    [SerializeField] private float agitatedAcceleration = 14f;
    [SerializeField] private Vector2 agitationDuration = new Vector2(6f, 14f);
    [Tooltip("Random time between agitation bursts.")]
    [SerializeField] private Vector2 agitationInterval = new Vector2(25f, 60f);

    [Header("Player Interest")]
    [Tooltip("Optional. If empty, the object with Player_Controller is used.")]
    [SerializeField] private Transform player;
    [Tooltip("Chance (per roll) that it starts paying attention to the player.")]
    [SerializeField, Range(0f, 1f)] private float playerInterestChance = 0.35f;
    [SerializeField] private Vector2 playerInterestRollInterval = new Vector2(15f, 30f);
    [SerializeField] private Vector2 playerInterestDuration = new Vector2(12f, 25f);
    [Tooltip("Stays this far from the player while stalking (min / max).")]
    [SerializeField] private Vector2 stalkDistance = new Vector2(3f, 7f);
    [SerializeField] private Vector2 stalkWatchDuration = new Vector2(1.5f, 4f);

    [Header("Jumpscare")]
    [Tooltip("Minimum / maximum time between two jumpscares.")]
    [SerializeField] private Vector2 jumpscareCooldown = new Vector2(120f, 240f);
    [Tooltip("Once the cooldown is over, chance per check (every few seconds) to actually do it.")]
    [SerializeField, Range(0f, 1f)] private float jumpscareChance = 0.15f;
    [SerializeField] private float jumpscareCheckInterval = 4f;
    [Tooltip("Player must be within this distance range for a rush to start.")]
    [SerializeField] private Vector2 jumpscareRange = new Vector2(6f, 25f);
    [SerializeField] private float jumpscareSpeed = 16f;
    [SerializeField] private float jumpscareAcceleration = 80f;
    [Tooltip("Dead stop before the rush (the 'silence before the scare').")]
    [SerializeField] private Vector2 jumpscareWindup = new Vector2(0.6f, 1.5f);
    [SerializeField] private float jumpscareReachDistance = 1.3f;
    [SerializeField] private float jumpscareMaxDuration = 3.5f;
    [SerializeField] private UnityEvent onJumpscareStart;
    [SerializeField] private UnityEvent onJumpscareReachedPlayer;

    [Header("Vanishing (disables the GameObject)")]
    [Tooltip("Chance (per roll) that it disables itself for a moment.")]
    [SerializeField, Range(0f, 1f)] private float vanishChance = 0.3f;
    [SerializeField] private Vector2 vanishRollInterval = new Vector2(20f, 45f);
    [SerializeField] private Vector2 vanishDuration = new Vector2(1f, 6f);
    [Tooltip("Chance that it comes back at a different place instead of where it vanished.")]
    [SerializeField, Range(0f, 1f)] private float relocateOnReturnChance = 0.3f;

    [Header("Light Flicker")]
    [SerializeField] private string lightTag = "Light";
    [SerializeField] private Vector2 flickerIntensityMultiplier = new Vector2(0.05f, 1.2f);
    [SerializeField] private Vector2 flickerStepDuration = new Vector2(0.02f, 0.15f);
    [Tooltip("Chance per flicker step that the light cuts out briefly.")]
    [SerializeField, Range(0f, 1f)] private float blackoutChance = 0.15f;
    [SerializeField] private Vector2 blackoutDuration = new Vector2(0.05f, 0.6f);
    [Tooltip("Keeps flickering for this long after it leaves the light.")]
    [SerializeField] private Vector2 flickerLinger = new Vector2(0.3f, 1.5f);
    [Tooltip("Chance that the light stays OFF for a while after it leaves.")]
    [SerializeField, Range(0f, 1f)] private float stayOffChance = 0.2f;
    [SerializeField] private Vector2 stayOffDuration = new Vector2(2f, 8f);

    [Header("Debug")]
    [SerializeField] private bool drawGizmos = true;

    // ---------------------------------------------------------
    // STATE
    // ---------------------------------------------------------

    private NavMeshAgent agent;

    // Surface-local bounds of the walkable area, and the pose the NavMesh instance was placed with.
    private Bounds surfaceLocalBounds;
    private Matrix4x4 surfaceToWorld = Matrix4x4.identity;
    private bool hasSurfaceBounds;
    private float walkableArea;

    private readonly List<Vector3> patrolPoints = new List<Vector3>();
    private float[] lastVisitTime;
    private readonly List<Vector3> interestSpots = new List<Vector3>();

    private bool isAgitated;
    private float agitatedUntil;
    private float nextAgitationTime;

    private bool playerInterested;
    private float playerInterestUntil;
    private float nextPlayerInterestRoll;

    private float nextJumpscareAllowed;
    private float nextJumpscareCheck;
    private bool jumpscareRequested;

    private float nextVisitCheck;

    private bool initialized;
    private bool isJumpscaring;
    private float nextVanishRoll;

    // A disabled object can't run its own coroutines, so the re-enable timer lives on a separate object.
    private static VanishTimer vanishTimer;
    private sealed class VanishTimer : MonoBehaviour { }

    // Original light values are shared, so several presences never "bake in" a dimmed value.
    private static readonly Dictionary<Light, float> OriginalIntensity = new Dictionary<Light, float>();
    private readonly Dictionary<Light, int> lightsInside = new Dictionary<Light, int>();
    private readonly Dictionary<Light, Coroutine> flickering = new Dictionary<Light, Coroutine>();
    private readonly Dictionary<Light, float> flickerUntil = new Dictionary<Light, float>();

    // ---------------------------------------------------------
    // UNITY
    // ---------------------------------------------------------

    private void Awake()
    {
        agent = GetComponent<NavMeshAgent>();

        Rigidbody body = GetComponent<Rigidbody>();
        body.isKinematic = true;
        body.useGravity = false;

        if (player == null)
        {
            Player_Controller controller = FindFirstObjectByType<Player_Controller>();
            if (controller != null) player = controller.transform;
        }

        ResolveNavMeshSurface();
    }

    /// <summary>
    /// Finds the surface, and if it sits on this moving object, moves it to a static object.
    /// A NavMeshSurface re-places its NavMesh every time its transform moves, so a surface on
    /// the agent itself would drag the whole walkable area along with it.
    /// </summary>
    private void ResolveNavMeshSurface()
    {
        if (navMeshSurface == null) navMeshSurface = GetComponentInParent<NavMeshSurface>();
        if (navMeshSurface == null)
        {
            foreach (NavMeshSurface s in NavMeshSurface.activeSurfaces)
            {
                if (s.agentTypeID == agent.agentTypeID)
                {
                    navMeshSurface = s;
                    break;
                }
            }
        }

        if (navMeshSurface == null)
        {
            Debug.LogWarning($"{name}: no NavMeshSurface found, patrolling the whole loaded NavMesh.", this);
            return;
        }

        if (navMeshSurface.agentTypeID != agent.agentTypeID)
        {
            Debug.LogWarning($"{name}: the NavMeshSurface was baked for a different agent type than this NavMeshAgent.", this);
        }

        if (navMeshSurface.transform == transform || navMeshSurface.transform.IsChildOf(transform))
        {
            Debug.LogWarning($"{name}: the NavMeshSurface is on the moving object, so its NavMesh would move with it. " +
                             "Moving it to a static object for this play session. Put the NavMeshSurface on a static object " +
                             "(e.g. the room/floor root) and re-bake to fix this permanently.", this);

            NavMeshSurface original = navMeshSurface;
            GameObject holder = new GameObject($"{name}_NavMeshSurface");
            holder.transform.SetPositionAndRotation(original.transform.position, original.transform.rotation);

            holder.SetActive(false); // copy settings before OnEnable adds the data
            NavMeshSurface copy = holder.AddComponent<NavMeshSurface>();
            copy.agentTypeID = original.agentTypeID;
            copy.collectObjects = original.collectObjects;
            copy.size = original.size;
            copy.center = original.center;
            copy.layerMask = original.layerMask;
            copy.useGeometry = original.useGeometry;
            copy.defaultArea = original.defaultArea;
            copy.navMeshData = original.navMeshData;
            holder.SetActive(true);

            Destroy(original); // its OnDisable removes only its own NavMesh instance
            navMeshSurface = copy;
        }

        // The NavMesh instance is placed at the surface's position/rotation (no scale).
        surfaceToWorld = Matrix4x4.TRS(navMeshSurface.transform.position, navMeshSurface.transform.rotation, Vector3.one);

        if (navMeshSurface.collectObjects == CollectObjects.Volume)
        {
            Vector3 s = navMeshSurface.size;
            surfaceLocalBounds = new Bounds(navMeshSurface.center, new Vector3(Mathf.Abs(s.x), Mathf.Abs(s.y), Mathf.Abs(s.z)));
            hasSurfaceBounds = true;
        }
        else if (navMeshSurface.navMeshData != null)
        {
            surfaceLocalBounds = navMeshSurface.navMeshData.sourceBounds;
            hasSurfaceBounds = true;
        }
        else
        {
            Debug.LogWarning($"{name}: the NavMeshSurface has no baked data. Press Bake on it.", this);
        }

        if (hasSurfaceBounds) surfaceLocalBounds.Expand(new Vector3(0.1f, 0.5f, 0.1f)); // tolerance for mesh height offset
    }

    private bool InsideSurface(Vector3 worldPoint)
    {
        if (!hasSurfaceBounds) return true;
        return surfaceLocalBounds.Contains(surfaceToWorld.inverse.MultiplyPoint3x4(worldPoint));
    }

    private void Start()
    {
        BuildPatrolPoints();
        BuildInterestSpots();

        ApplyWalkSettings();

        float now = Time.time;
        nextAgitationTime = now + RandomIn(agitationInterval);
        nextPlayerInterestRoll = now + RandomIn(playerInterestRollInterval);
        nextJumpscareAllowed = now + RandomIn(jumpscareCooldown) * 0.5f; // first one can come a bit sooner
        nextVanishRoll = now + RandomIn(vanishRollInterval);

        if (patrolPoints.Count == 0)
        {
            Debug.LogWarning($"{name}: no NavMesh found to patrol. Bake a NavMesh Surface first.", this);
            return;
        }

        initialized = true;
        StartCoroutine(Brain());
    }

    private void OnEnable()
    {
        // Coming back after a vanish (or after anything else disabled it): resume patrolling.
        if (!initialized) return; // first enable: Start() kicks things off once patrol points exist
        ApplyWalkSettings();
        agent.updateRotation = true;
        StartCoroutine(Brain());
    }

    private void Update()
    {
        float now = Time.time;

        // Occasionally blink out of existence.
        if (initialized && now >= nextVanishRoll)
        {
            nextVanishRoll = now + RandomIn(vanishRollInterval);
            if (!isJumpscaring && !jumpscareRequested && Random.value < vanishChance)
            {
                Vanish(RandomIn(vanishDuration));
                return;
            }
        }

        // Agitation bursts.
        if (isAgitated && now >= agitatedUntil)
        {
            isAgitated = false;
            nextAgitationTime = now + RandomIn(agitationInterval);
        }
        else if (!isAgitated && now >= nextAgitationTime)
        {
            isAgitated = true;
            agitatedUntil = now + RandomIn(agitationDuration);
        }

        // Player interest comes and goes.
        if (player != null)
        {
            if (playerInterested && now >= playerInterestUntil)
            {
                playerInterested = false;
                nextPlayerInterestRoll = now + RandomIn(playerInterestRollInterval);
            }
            else if (!playerInterested && now >= nextPlayerInterestRoll)
            {
                if (Random.value < playerInterestChance)
                {
                    playerInterested = true;
                    playerInterestUntil = now + RandomIn(playerInterestDuration);
                }
                else
                {
                    nextPlayerInterestRoll = now + RandomIn(playerInterestRollInterval);
                }
            }

            // Rare jumpscare roll.
            if (!jumpscareRequested && now >= nextJumpscareAllowed && now >= nextJumpscareCheck)
            {
                nextJumpscareCheck = now + jumpscareCheckInterval;
                if (Random.value < jumpscareChance && CanReachPlayerForScare())
                {
                    jumpscareRequested = true;
                }
            }
        }

        // Anything it passes through counts as explored.
        if (now >= nextVisitCheck)
        {
            nextVisitCheck = now + 0.25f;
            MarkNearbyPointsVisited();
        }
    }

    private void OnDisable()
    {
        foreach (KeyValuePair<Light, Coroutine> pair in flickering)
        {
            if (pair.Value != null) StopCoroutine(pair.Value);
            RestoreLight(pair.Key);
        }
        flickering.Clear();
        lightsInside.Clear();
        flickerUntil.Clear();

        isJumpscaring = false;
    }

    // ---------------------------------------------------------
    // VANISHING
    // ---------------------------------------------------------

    /// <summary>Disables the whole GameObject for a while, then enables it again (possibly somewhere else).</summary>
    public void Vanish(float duration)
    {
        if (!gameObject.activeInHierarchy) return;

        if (vanishTimer == null)
        {
            vanishTimer = new GameObject("[Presence Vanish Timer]").AddComponent<VanishTimer>();
        }

        bool relocate = patrolPoints.Count > 0 && Random.value < relocateOnReturnChance;
        Vector3 returnPosition = relocate ? patrolPoints[Random.Range(0, patrolPoints.Count)] : transform.position;

        vanishTimer.StartCoroutine(VanishRoutine(duration, relocate, returnPosition));
    }

    private IEnumerator VanishRoutine(float duration, bool relocate, Vector3 returnPosition)
    {
        gameObject.SetActive(false);
        yield return new WaitForSeconds(duration);

        if (this == null) yield break; // destroyed while hidden (e.g. scene change)

        if (relocate)
        {
            // The agent is off while disabled; on enable it snaps to the NavMesh at this position.
            transform.position = returnPosition + Vector3.up * agent.baseOffset;
        }

        gameObject.SetActive(true);
    }

    // ---------------------------------------------------------
    // BRAIN
    // ---------------------------------------------------------

    private IEnumerator Brain()
    {
        while (true)
        {
            if (jumpscareRequested)
            {
                yield return Jumpscare();
            }
            else if (playerInterested && player != null)
            {
                yield return StalkPlayer();
            }
            else
            {
                yield return PatrolLeg();
            }

            yield return null;
        }
    }

    private IEnumerator PatrolLeg()
    {
        bool goToInterest = interestSpots.Count > 0 && Random.value < (isAgitated ? interestVisitChance * 0.3f : interestVisitChance);

        if (goToInterest)
        {
            Vector3 spot = interestSpots[Random.Range(0, interestSpots.Count)];
            yield return MoveTo(spot, 25f);
            if (jumpscareRequested) yield break;
            yield return Investigate(spot);
            yield break;
        }

        int index = PickPatrolPoint();
        yield return MoveTo(patrolPoints[index], 25f);
        if (jumpscareRequested) yield break;

        // Agitated: rarely stops, keeps searching.
        float chance = isAgitated ? pauseChance * 0.25f : pauseChance;
        if (Random.value < chance)
        {
            yield return LookAround(RandomIn(pauseDuration));
        }
    }

    private IEnumerator Investigate(Vector3 spot)
    {
        float until = Time.time + RandomIn(interestLingerDuration) * (isAgitated ? 0.5f : 1f);
        float slowSpeed = walkSpeed * 0.4f;

        while (Time.time < until && !jumpscareRequested)
        {
            // Small drifts around the spot, like sniffing at something.
            if (Random.value < 0.6f && TrySampleNavMesh(spot + RandomFlat(interestWanderRadius), 1f, out Vector3 nudge))
            {
                agent.speed = slowSpeed;
                yield return MoveTo(nudge, 3f, keepSpeed: true);
            }
            yield return LookAround(Random.Range(0.8f, 2f));
        }

        ApplyWalkSettings();
    }

    private IEnumerator LookAround(float duration)
    {
        agent.isStopped = true;
        agent.updateRotation = false;

        float end = Time.time + duration;
        while (Time.time < end && !jumpscareRequested)
        {
            Quaternion target = Quaternion.Euler(0f, Random.Range(0f, 360f), 0f);
            float turnEnd = Mathf.Min(end, Time.time + Random.Range(0.4f, 1.2f));
            while (Time.time < turnEnd && !jumpscareRequested)
            {
                transform.rotation = Quaternion.RotateTowards(transform.rotation, target, walkAngularSpeed * 0.5f * Time.deltaTime);
                yield return null;
            }
        }

        agent.updateRotation = true;
        agent.isStopped = false;
    }

    private IEnumerator StalkPlayer()
    {
        // Hover somewhere around the player, never directly on them.
        Vector3 around = player.position + RandomRing(stalkDistance.x, stalkDistance.y);
        if (TrySampleNavMesh(around, 2f, out Vector3 spot))
        {
            yield return MoveTo(spot, 12f);
            if (jumpscareRequested) yield break;
        }

        // Watch them for a moment.
        agent.isStopped = true;
        agent.updateRotation = false;
        float end = Time.time + RandomIn(stalkWatchDuration);
        while (Time.time < end && !jumpscareRequested && player != null)
        {
            Vector3 dir = player.position - transform.position;
            dir.y = 0f;
            if (dir.sqrMagnitude > 0.01f)
            {
                transform.rotation = Quaternion.RotateTowards(transform.rotation, Quaternion.LookRotation(dir), walkAngularSpeed * Time.deltaTime);
            }
            yield return null;
        }
        agent.updateRotation = true;
        agent.isStopped = false;
    }

    private IEnumerator Jumpscare()
    {
        jumpscareRequested = false;
        nextJumpscareAllowed = Time.time + RandomIn(jumpscareCooldown);

        if (player == null) yield break;
        isJumpscaring = true;

        // Dead stop first.
        agent.isStopped = true;
        yield return new WaitForSeconds(RandomIn(jumpscareWindup));

        agent.isStopped = false;
        agent.speed = jumpscareSpeed;
        agent.acceleration = jumpscareAcceleration;
        agent.angularSpeed = 1080f;
        agent.autoBraking = false;
        onJumpscareStart?.Invoke();

        float end = Time.time + jumpscareMaxDuration;
        bool reached = false;
        while (Time.time < end)
        {
            agent.SetDestination(player.position);
            if (FlatDistance(transform.position, player.position) <= jumpscareReachDistance)
            {
                reached = true;
                break;
            }
            yield return null;
        }

        if (reached) onJumpscareReachedPlayer?.Invoke();
        isJumpscaring = false;

        // Brake hard, then vanish into a far corner and leave the player alone for a while.
        agent.velocity = Vector3.zero;
        ApplyWalkSettings();
        playerInterested = false;
        nextPlayerInterestRoll = Time.time + playerInterestRollInterval.y * 2f;
        isAgitated = false;
        nextAgitationTime = Time.time + RandomIn(agitationInterval);

        yield return MoveTo(FarthestPointFrom(player.position), 20f);
    }

    // ---------------------------------------------------------
    // MOVEMENT
    // ---------------------------------------------------------

    private IEnumerator MoveTo(Vector3 destination, float timeout, bool keepSpeed = false)
    {
        if (!agent.isOnNavMesh) yield break;

        agent.isStopped = false;
        agent.SetDestination(destination);
        float end = Time.time + timeout;

        while (Time.time < end && !jumpscareRequested)
        {
            if (!keepSpeed) ApplyCurrentSpeed();

            if (!agent.pathPending)
            {
                if (agent.pathStatus == NavMeshPathStatus.PathInvalid) yield break;
                if (agent.remainingDistance <= Mathf.Max(agent.stoppingDistance, 0.3f)) yield break;
            }

            // Lost interest in the patrol because the player suddenly became interesting.
            if (playerInterested && !keepSpeed && Random.value < 0.002f) yield break;

            yield return null;
        }
    }

    private void ApplyCurrentSpeed()
    {
        if (isAgitated)
        {
            // Slightly uneven pace so it feels restless.
            agent.speed = agitatedSpeed * (0.85f + Mathf.PerlinNoise(Time.time * 0.7f, 0f) * 0.3f);
            agent.acceleration = agitatedAcceleration;
            agent.angularSpeed = walkAngularSpeed * 2f;
        }
        else
        {
            ApplyWalkSettings();
        }
    }

    private void ApplyWalkSettings()
    {
        agent.speed = walkSpeed;
        agent.acceleration = walkAcceleration;
        agent.angularSpeed = walkAngularSpeed;
        agent.autoBraking = true;
    }

    // ---------------------------------------------------------
    // PATROL POINTS
    // ---------------------------------------------------------

    private void BuildPatrolPoints()
    {
        patrolPoints.Clear();

        NavMeshTriangulation tri = NavMesh.CalculateTriangulation();
        if (tri.indices.Length < 3) return;

        // Area-weighted list of the triangles that belong to our surface and that this agent may walk on,
        // so big rooms get proportionally more points.
        int triCount = tri.indices.Length / 3;
        float[] cumulative = new float[triCount];
        float total = 0f;
        for (int t = 0; t < triCount; t++)
        {
            Vector3 a = tri.vertices[tri.indices[t * 3]];
            Vector3 b = tri.vertices[tri.indices[t * 3 + 1]];
            Vector3 c = tri.vertices[tri.indices[t * 3 + 2]];

            bool walkable = (agent.areaMask & (1 << tri.areas[t])) != 0 && InsideSurface((a + b + c) / 3f);
            if (walkable) total += Vector3.Cross(b - a, c - a).magnitude * 0.5f;
            cumulative[t] = total;
        }
        walkableArea = total;
        if (total <= 0f) return;

        int patrolPointCount = Mathf.Clamp(
            Mathf.RoundToInt(total / Mathf.Max(0.5f, squareMetersPerPatrolPoint)),
            patrolPointCountLimits.x, patrolPointCountLimits.y);

        // Spread points out evenly (rejects points too close to existing ones).
        float spacing = Mathf.Sqrt(total / patrolPointCount) * 0.75f;
        NavMeshPath path = new NavMeshPath();
        int attempts = patrolPointCount * 40;

        for (int i = 0; i < attempts && patrolPoints.Count < patrolPointCount; i++)
        {
            int t = System.Array.BinarySearch(cumulative, Random.value * total);
            if (t < 0) t = ~t;
            t = Mathf.Clamp(t, 0, triCount - 1);

            Vector3 p = RandomPointInTriangle(
                tri.vertices[tri.indices[t * 3]],
                tri.vertices[tri.indices[t * 3 + 1]],
                tri.vertices[tri.indices[t * 3 + 2]]);

            if (TooClose(p, spacing)) continue;

            // Only keep points it can actually walk to from where it starts.
            if (!NavMesh.CalculatePath(transform.position, p, agent.areaMask, path) || path.status != NavMeshPathStatus.PathComplete) continue;

            patrolPoints.Add(p);
        }

        lastVisitTime = new float[patrolPoints.Count];
        for (int i = 0; i < lastVisitTime.Length; i++) lastVisitTime[i] = -9999f;

        Debug.Log($"{name}: walkable area {walkableArea:0.0} m², {patrolPoints.Count} patrol points.", this);
    }

    private void BuildInterestSpots()
    {
        interestSpots.Clear();

        if (interestPoints != null)
        {
            foreach (Transform t in interestPoints)
            {
                if (t != null && TrySampleNavMesh(t.position, 2f, out Vector3 p)) interestSpots.Add(p);
            }
        }

        if (interestSpots.Count == 0 && patrolPoints.Count > 0)
        {
            for (int i = 0; i < autoInterestPointCount; i++)
            {
                interestSpots.Add(patrolPoints[Random.Range(0, patrolPoints.Count)]);
            }
        }
    }

    /// <summary>Prefers places it hasn't been to in a while; sometimes deliberately picks one behind it.</summary>
    private int PickPatrolPoint()
    {
        Vector3 forward = transform.forward;
        bool unexpected = Random.value < unexpectedTurnChance;
        float now = Time.time;

        int best = 0;
        float bestScore = float.MinValue;
        for (int i = 0; i < patrolPoints.Count; i++)
        {
            Vector3 to = patrolPoints[i] - transform.position;
            float dist = to.magnitude;
            if (dist < visitRadius * 2f) continue;

            float staleness = Mathf.Min(now - lastVisitTime[i], 120f) / 120f; // 0..1, 1 = long ago / never
            float score = staleness * 2f + Random.value * 1.2f;

            float dot = Vector3.Dot(forward, to / dist);
            if (unexpected) score += (1f - dot) * 0.6f; // favour behind / sideways
            if (isAgitated) score += Random.value; // erratic

            if (score > bestScore)
            {
                bestScore = score;
                best = i;
            }
        }
        return best;
    }

    private void MarkNearbyPointsVisited()
    {
        if (lastVisitTime == null) return;
        float r2 = visitRadius * visitRadius;
        Vector3 pos = transform.position;
        for (int i = 0; i < patrolPoints.Count; i++)
        {
            if ((patrolPoints[i] - pos).sqrMagnitude <= r2) lastVisitTime[i] = Time.time;
        }
    }

    private Vector3 FarthestPointFrom(Vector3 from)
    {
        Vector3 best = transform.position;
        float bestDist = 0f;
        foreach (Vector3 p in patrolPoints)
        {
            float d = (p - from).sqrMagnitude * Random.Range(0.7f, 1f);
            if (d > bestDist)
            {
                bestDist = d;
                best = p;
            }
        }
        return best;
    }

    private bool CanReachPlayerForScare()
    {
        if (player == null || !agent.isOnNavMesh) return false;
        float d = FlatDistance(transform.position, player.position);
        if (d < jumpscareRange.x || d > jumpscareRange.y) return false;

        NavMeshPath path = new NavMeshPath();
        return NavMesh.CalculatePath(transform.position, player.position, agent.areaMask, path)
               && path.status == NavMeshPathStatus.PathComplete;
    }

    // ---------------------------------------------------------
    // LIGHTS
    // ---------------------------------------------------------

    private void OnTriggerEnter(Collider other)
    {
        if (!other.CompareTag(lightTag)) return;
        Light light = FindLight(other);
        if (light == null) return;

        lightsInside.TryGetValue(light, out int count);
        lightsInside[light] = count + 1;

        if (!OriginalIntensity.ContainsKey(light)) OriginalIntensity[light] = light.intensity;

        if (!flickering.ContainsKey(light))
        {
            flickering[light] = StartCoroutine(Flicker(light));
        }
    }

    private void OnTriggerExit(Collider other)
    {
        if (!other.CompareTag(lightTag)) return;
        Light light = FindLight(other);
        if (light == null || !lightsInside.TryGetValue(light, out int count)) return;

        count--;
        if (count <= 0)
        {
            lightsInside.Remove(light);
            flickerUntil[light] = Time.time + RandomIn(flickerLinger);
        }
        else
        {
            lightsInside[light] = count;
        }
    }

    private IEnumerator Flicker(Light light)
    {
        float baseIntensity = OriginalIntensity[light];

        while (light != null && (lightsInside.ContainsKey(light) || Time.time < GetFlickerUntil(light)))
        {
            float blackout = isAgitated ? blackoutChance * 2f : blackoutChance;
            float roll = Random.value;

            if (roll < blackout)
            {
                light.enabled = false;
                yield return new WaitForSeconds(RandomIn(blackoutDuration));
                if (light != null) light.enabled = true;
            }
            else if (roll < blackout + 0.15f)
            {
                // Brief steady moment so it's not a constant strobe.
                light.intensity = baseIntensity * Random.Range(0.6f, 1f);
                yield return new WaitForSeconds(Random.Range(0.1f, 0.4f));
            }
            else
            {
                light.intensity = baseIntensity * RandomIn(flickerIntensityMultiplier);
                yield return new WaitForSeconds(RandomIn(flickerStepDuration));
            }
        }

        if (light == null) yield break;

        // Sometimes it leaves the light dead for a while.
        if (Random.value < stayOffChance)
        {
            light.enabled = false;
            float offEnd = Time.time + RandomIn(stayOffDuration);
            while (Time.time < offEnd && !lightsInside.ContainsKey(light)) yield return null;
        }

        RestoreLight(light);
        flickering.Remove(light);
        flickerUntil.Remove(light);

        // It came back while the light was dead: start over.
        if (lightsInside.ContainsKey(light)) flickering[light] = StartCoroutine(Flicker(light));
    }

    private float GetFlickerUntil(Light light)
    {
        return flickerUntil.TryGetValue(light, out float t) ? t : 0f;
    }

    private static void RestoreLight(Light light)
    {
        if (light == null) return;
        if (OriginalIntensity.TryGetValue(light, out float i)) light.intensity = i;
        light.enabled = true;
    }

    private static Light FindLight(Collider c)
    {
        Light l = c.GetComponent<Light>();
        if (l == null) l = c.GetComponentInParent<Light>();
        if (l == null) l = c.GetComponentInChildren<Light>();
        return l;
    }

    // ---------------------------------------------------------
    // HELPERS
    // ---------------------------------------------------------

    private bool TooClose(Vector3 p, float spacing)
    {
        float s2 = spacing * spacing;
        foreach (Vector3 q in patrolPoints)
        {
            if ((q - p).sqrMagnitude < s2) return true;
        }
        return false;
    }

    private bool TrySampleNavMesh(Vector3 around, float maxDistance, out Vector3 result)
    {
        if (NavMesh.SamplePosition(around, out NavMeshHit hit, maxDistance, agent.areaMask))
        {
            result = hit.position;
            return true;
        }
        result = around;
        return false;
    }

    private static Vector3 RandomPointInTriangle(Vector3 a, Vector3 b, Vector3 c)
    {
        float r1 = Random.value, r2 = Random.value;
        if (r1 + r2 > 1f) { r1 = 1f - r1; r2 = 1f - r2; }
        return a + (b - a) * r1 + (c - a) * r2;
    }

    private static Vector3 RandomFlat(float radius)
    {
        Vector2 v = Random.insideUnitCircle * radius;
        return new Vector3(v.x, 0f, v.y);
    }

    private static Vector3 RandomRing(float min, float max)
    {
        Vector2 dir = Random.insideUnitCircle.normalized;
        float d = Random.Range(min, max);
        return new Vector3(dir.x, 0f, dir.y) * d;
    }

    private static float RandomIn(Vector2 range) => Random.Range(range.x, range.y);

    private static float FlatDistance(Vector3 a, Vector3 b)
    {
        a.y = 0f; b.y = 0f;
        return Vector3.Distance(a, b);
    }

    private void OnDrawGizmosSelected()
    {
        if (!drawGizmos) return;

        if (hasSurfaceBounds)
        {
            Gizmos.color = new Color(0.4f, 1f, 0.4f, 0.5f);
            Gizmos.matrix = surfaceToWorld;
            Gizmos.DrawWireCube(surfaceLocalBounds.center, surfaceLocalBounds.size);
            Gizmos.matrix = Matrix4x4.identity;
        }

        Gizmos.color = new Color(0.3f, 0.8f, 1f, 0.8f);
        for (int i = 0; i < patrolPoints.Count; i++)
        {
            Gizmos.DrawWireSphere(patrolPoints[i], 0.25f);
        }

        Gizmos.color = new Color(1f, 0.6f, 0.1f, 0.9f);
        foreach (Vector3 p in interestSpots) Gizmos.DrawWireSphere(p, interestWanderRadius);

        if (Application.isPlaying && agent != null && agent.hasPath)
        {
            Gizmos.color = isAgitated ? Color.red : (playerInterested ? Color.magenta : Color.green);
            Vector3[] corners = agent.path.corners;
            for (int i = 1; i < corners.Length; i++) Gizmos.DrawLine(corners[i - 1], corners[i]);
        }
    }
}
