using System.Collections;
using System.Collections.Generic;
using UnityEngine;

// Turns every light in the scene off, and later back on with a flicker.
// Only lights that were on when GoDark() ran are turned back on.
public class LightBlackout : MonoBehaviour
{
    [Tooltip("Also switch off directional lights (sun / moon). Untick to keep the outside lit.")]
    public bool includeDirectionalLights = true;

    [Tooltip("Lights that are never touched.")]
    public Light[] exclude = new Light[0];

    [Header("Coming back on")]
    [Tooltip("Number of on/off flickers before the lights stay on (min / max).")]
    public Vector2Int flickerCount = new Vector2Int(3, 6);
    [Tooltip("Length of each flicker step in seconds (min / max).")]
    public Vector2 flickerStep = new Vector2(0.04f, 0.18f);

    private readonly List<Light> darkened = new List<Light>();
    private Coroutine restoring;

    public bool IsDark { get; private set; }

    public void GoDark()
    {
        if (restoring != null)
        {
            StopCoroutine(restoring);
            restoring = null;
        }

        if (IsDark)
        {
            return;
        }

        IsDark = true;
        darkened.Clear();

        foreach (Light light in FindObjectsByType<Light>(FindObjectsInactive.Exclude, FindObjectsSortMode.None))
        {
            if (!light.enabled || IsExcluded(light))
            {
                continue;
            }

            if (!includeDirectionalLights && light.type == LightType.Directional)
            {
                continue;
            }

            light.enabled = false;
            darkened.Add(light);
        }

        Debug.Log("[Cutscene] Lights off (" + darkened.Count + ").");
    }

    public void RestoreAfter(float delay, bool flicker = true)
    {
        if (!IsDark)
        {
            return;
        }

        if (restoring != null)
        {
            StopCoroutine(restoring);
        }

        restoring = StartCoroutine(RestoreRoutine(delay, flicker));
    }

    public void RestoreNow()
    {
        if (restoring != null)
        {
            StopCoroutine(restoring);
            restoring = null;
        }

        SetAll(true);
        darkened.Clear();
        IsDark = false;
    }

    private IEnumerator RestoreRoutine(float delay, bool flicker)
    {
        yield return new WaitForSeconds(delay);

        if (flicker)
        {
            int count = Random.Range(flickerCount.x, flickerCount.y + 1);

            for (int i = 0; i < count; i++)
            {
                SetAll(true);
                yield return new WaitForSeconds(Random.Range(flickerStep.x, flickerStep.y));
                SetAll(false);
                yield return new WaitForSeconds(Random.Range(flickerStep.x, flickerStep.y) * 1.5f);
            }
        }

        restoring = null;
        RestoreNow();
        Debug.Log("[Cutscene] Lights back on.");
    }

    private void SetAll(bool on)
    {
        foreach (Light light in darkened)
        {
            if (light != null) light.enabled = on;
        }
    }

    private bool IsExcluded(Light light)
    {
        foreach (Light e in exclude)
        {
            if (e == light) return true;
        }

        return false;
    }
}
