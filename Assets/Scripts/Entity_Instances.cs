
using UnityEngine;

public class Entity_Instances : MonoBehaviour
{
    [Header("Entity Prefabs")]
    [SerializeField] private GameObject entityYPrefab;
    [SerializeField] private GameObject entityYNegativePrefab;
    [SerializeField] private GameObject entityZPrefab;
    [SerializeField] private GameObject entityZNegativePrefab;

    [Header("Entity Instantiation")]
    [SerializeField] private bool instantiateEntities = false;

    [Header("Clone Settings")]
    [SerializeField] private int numberOfClones = 3;
    [SerializeField] private float cloneOffset = 0.15f;

    private GameObject[] spawnedEntities;

    private bool previousInstantiateState;

    private void Start()
    {
        previousInstantiateState = instantiateEntities;

        // Make sure nothing is spawned when the game starts
        // unless the boolean is manually changed at runtime.
        spawnedEntities = new GameObject[0];

        if (instantiateEntities)
        {
            InstantiateEntities();
        }
    }

    private void Update()
    {
        // Check if the boolean has changed.
        if (instantiateEntities != previousInstantiateState)
        {
            if (instantiateEntities)
            {
                InstantiateEntities();
            }
            else
            {
                DestroyEntities();
            }

            previousInstantiateState = instantiateEntities;
        }
    }

    private void InstantiateEntities()
    {
        // Prevent duplicate spawning.
        if (spawnedEntities.Length > 0)
        {
            return;
        }

        int totalEntities = numberOfClones * 4;

        spawnedEntities = new GameObject[totalEntities];

        int index = 0;

        // EntityY
        for (int i = 0; i < numberOfClones; i++)
        {
            if (entityYPrefab != null)
            {
                Vector3 spawnPosition = entityYPrefab.transform.position;

                // EntityY: subtract 0.15 on Z for every clone.
                spawnPosition.z -= cloneOffset * i;

                spawnedEntities[index] = Instantiate(
                    entityYPrefab,
                    spawnPosition,
                    entityYPrefab.transform.rotation
                );

                index++;
            }
        }

        // EntityYNegative
        for (int i = 0; i < numberOfClones; i++)
        {
            if (entityYNegativePrefab != null)
            {
                Vector3 spawnPosition = entityYNegativePrefab.transform.position;

                // EntityYNegative: add 0.15 on Z for every clone.
                spawnPosition.z += cloneOffset * i;

                spawnedEntities[index] = Instantiate(
                    entityYNegativePrefab,
                    spawnPosition,
                    entityYNegativePrefab.transform.rotation
                );

                index++;
            }
        }

        // EntityZ
        for (int i = 0; i < numberOfClones; i++)
        {
            if (entityZPrefab != null)
            {
                Vector3 spawnPosition = entityZPrefab.transform.position;

                // EntityZ: add 0.15 on Y for every clone.
                spawnPosition.y += cloneOffset * i;

                spawnedEntities[index] = Instantiate(
                    entityZPrefab,
                    spawnPosition,
                    entityZPrefab.transform.rotation
                );

                index++;
            }
        }

        // EntityZNegative
        for (int i = 0; i < numberOfClones; i++)
        {
            if (entityZNegativePrefab != null)
            {
                Vector3 spawnPosition = entityZNegativePrefab.transform.position;

                // EntityZNegative: subtract 0.15 on Y for every clone.
                spawnPosition.y -= cloneOffset * i;

                spawnedEntities[index] = Instantiate(
                    entityZNegativePrefab,
                    spawnPosition,
                    entityZNegativePrefab.transform.rotation
                );

                index++;
            }
        }
    }

    private void DestroyEntities()
    {
        foreach (GameObject entity in spawnedEntities)
        {
            if (entity != null)
            {
                Destroy(entity);
            }
        }

        // Clear the array so they can be spawned again.
        spawnedEntities = new GameObject[0];
    }
}

