using UnityEngine;
using System.Collections;
using System.Collections.Generic;

public class EntityWallLightEffect : MonoBehaviour
{
    [Header("Entity Prefabs")]
    [Tooltip("Assign the 4 entity prefabs here. Their tags are used to recognize them.")]
    [SerializeField] private GameObject[] entityPrefabs = new GameObject[4];

    [Header("Area Lights")]
    [SerializeField] private Light[] areaLights;

    [Header("Flicker Strength Per Wall")]
    [Tooltip("How strong the flicker is while the furthest entity is at this wall. 0 = no flicker.")]
    [SerializeField, Range(0f, 1f)]
    private float wall1FlickerStrength = 0.25f;

    [SerializeField, Range(0f, 1f)]
    private float wall2FlickerStrength = 0.6f;

    [SerializeField, Range(0f, 1f)]
    private float wall3FlickerStrength = 1f;

    [Header("Flicker")]
    [SerializeField] private float minimumFlickerMultiplier = 0.05f;
    [SerializeField] private float maximumFlickerMultiplier = 1f;

    [SerializeField] private float minimumFlickerDuration = 0.02f;
    [SerializeField] private float maximumFlickerDuration = 0.12f;

    [Header("Blackout Chance")]
    [Tooltip("Chance of a full blackout at flicker strength 1. Scales down with the strength.")]
    [SerializeField, Range(0f, 1f)]
    private float blackoutChance = 0.3f;

    // ---------------------------------------------------------
    // SHARED STATE
    // ---------------------------------------------------------

    // 0 = no wall reached
    // 1 = Wall_1
    // 2 = Wall_2
    // 3 = Wall_3
    // The highest wall that currently has an entity inside it.
    private static int currentWallStage = 0;

    private static readonly List<EntityWallLightEffect> AllWalls =
        new List<EntityWallLightEffect>();

    // Entity colliders currently inside this wall's trigger.
    private readonly HashSet<Collider> entitiesInside = new HashSet<Collider>();

    private float[] originalIntensities;

    private Coroutine flickerCoroutine;
    private float flickerStrength;

    // ---------------------------------------------------------
    // INITIALIZATION
    // ---------------------------------------------------------

    private void Awake()
    {
        originalIntensities = new float[areaLights.Length];

        for (int i = 0; i < areaLights.Length; i++)
        {
            if (areaLights[i] != null)
            {
                originalIntensities[i] = areaLights[i].intensity;
            }
        }
    }

    private void OnEnable()
    {
        if (!AllWalls.Contains(this))
        {
            AllWalls.Add(this);
        }
    }

    private void OnDisable()
    {
        AllWalls.Remove(this);
        entitiesInside.Clear();
        StopFlicker();
    }

    // ---------------------------------------------------------
    // COLLISION
    // ---------------------------------------------------------

    private void OnTriggerEnter(Collider other)
    {
        // Check whether the collider belongs to one of our
        // four entity prefab types.
        if (!IsValidEntity(other))
        {
            return;
        }

        entitiesInside.Add(other);
        UpdateWallStage();
    }

    // Entities stepping back out of the wall lower the stage again.
    private void OnTriggerExit(Collider other)
    {
        if (entitiesInside.Remove(other))
        {
            UpdateWallStage();
        }
    }

    // ---------------------------------------------------------
    // ENTITY DETECTION
    // ---------------------------------------------------------

    private bool IsValidEntity(Collider other)
    {
        for (int i = 0; i < entityPrefabs.Length; i++)
        {
            if (entityPrefabs[i] == null)
            {
                continue;
            }

            string entityTag = entityPrefabs[i].tag;

            // Check the collider itself.
            if (other.CompareTag(entityTag))
            {
                return true;
            }

            // Also check the root GameObject.
            // This allows the entity to have its collider
            // on a child object.
            if (other.transform.root.CompareTag(entityTag))
            {
                return true;
            }
        }

        return false;
    }

    // ---------------------------------------------------------
    // WALL DETECTION
    // ---------------------------------------------------------

    private int GetWallStage()
    {
        switch (gameObject.tag)
        {
            case "Wall_1":
                return 1;

            case "Wall_2":
                return 2;

            case "Wall_3":
                return 3;

            default:
                Debug.LogWarning(
                    gameObject.name +
                    " does not have a valid wall tag."
                );

                return 0;
        }
    }

    private float GetFlickerStrength(int wallStage)
    {
        switch (wallStage)
        {
            case 1: return wall1FlickerStrength;
            case 2: return wall2FlickerStrength;
            case 3: return wall3FlickerStrength;
            default: return 0f;
        }
    }

    private bool HasEntityInside()
    {
        // Destroyed or disabled entities never send OnTriggerExit.
        entitiesInside.RemoveWhere(
            c => c == null || !c.enabled || !c.gameObject.activeInHierarchy
        );

        return entitiesInside.Count > 0;
    }

    // ---------------------------------------------------------
    // EFFECT
    // ---------------------------------------------------------

    // Finds the highest wall with an entity inside it. Only that wall
    // drives the flicker; every other wall stops its own.
    private static void UpdateWallStage()
    {
        int stage = 0;

        foreach (EntityWallLightEffect wall in AllWalls)
        {
            if (wall.HasEntityInside())
            {
                stage = Mathf.Max(stage, wall.GetWallStage());
            }
        }

        if (stage == currentWallStage)
        {
            return;
        }

        currentWallStage = stage;

        EntityWallLightEffect activeWall = null;

        // Stop the others first, so their intensity restore
        // can't overwrite the active wall's flicker.
        foreach (EntityWallLightEffect wall in AllWalls)
        {
            if (stage != 0 && activeWall == null && wall.GetWallStage() == stage)
            {
                activeWall = wall;
                continue;
            }

            wall.StopFlicker();
        }

        if (activeWall != null)
        {
            activeWall.StartFlicker(activeWall.GetFlickerStrength(stage));
        }
    }

    // ---------------------------------------------------------
    // FLICKER
    // ---------------------------------------------------------

    private void StartFlicker(float strength)
    {
        if (strength <= 0f)
        {
            StopFlicker();
            return;
        }

        // A running flicker picks up the new strength on its next loop.
        flickerStrength = strength;

        if (flickerCoroutine != null)
        {
            return;
        }

        flickerCoroutine = StartCoroutine(
            FlickerRoutine()
        );
    }

    private void StopFlicker()
    {
        if (flickerCoroutine != null)
        {
            StopCoroutine(flickerCoroutine);
            flickerCoroutine = null;
        }

        RestoreLightIntensity();
    }

    private IEnumerator FlickerRoutine()
    {
        while (true)
        {
            /*
             * Completely random intensity.
             */

            float multiplier = Random.Range(
                minimumFlickerMultiplier,
                maximumFlickerMultiplier
            );

            /*
             * Occasionally completely shut the
             * Area Lights off.
             */

            if (Random.value < blackoutChance * flickerStrength)
            {
                multiplier = 0f;
            }

            /*
             * A weaker flicker dips less far from full brightness.
             */

            multiplier = Mathf.Lerp(1f, multiplier, flickerStrength);

            for (int i = 0; i < areaLights.Length; i++)
            {
                if (areaLights[i] != null)
                {
                    areaLights[i].intensity =
                        originalIntensities[i] * multiplier;
                }
            }

            /*
             * Very short random duration.
             */

            yield return new WaitForSeconds(
                Random.Range(
                    minimumFlickerDuration,
                    maximumFlickerDuration
                )
            );

            /*
             * Random chance to immediately return
             * to full brightness. A weaker flicker
             * stays at full brightness for longer.
             */

            if (Random.value < 0.4f)
            {
                RestoreLightIntensity();

                yield return new WaitForSeconds(
                    Random.Range(0.01f, 0.08f) +
                    (1f - flickerStrength) * Random.Range(0.1f, 0.5f)
                );
            }
        }
    }

    private void RestoreLightIntensity()
    {
        if (originalIntensities == null)
        {
            return;
        }

        for (int i = 0; i < areaLights.Length; i++)
        {
            if (areaLights[i] != null)
            {
                areaLights[i].intensity =
                    originalIntensities[i];
            }
        }
    }

    // ---------------------------------------------------------
    // RESET
    // ---------------------------------------------------------

    public void ResetEffect()
    {
        currentWallStage = 0;
        entitiesInside.Clear();

        StopFlicker();
    }

    // ---------------------------------------------------------
    // STATIC RESET
    // ---------------------------------------------------------

    public static void ResetAllEffects(
        EntityWallLightEffect[] wallEffects
    )
    {
        currentWallStage = 0;

        if (wallEffects == null)
        {
            return;
        }

        foreach (EntityWallLightEffect effect in wallEffects)
        {
            if (effect != null)
            {
                effect.ResetEffect();
            }
        }
    }
}
