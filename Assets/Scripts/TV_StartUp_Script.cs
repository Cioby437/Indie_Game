using System.Collections;
using UnityEngine;

public class TV_StartUp_Script : MonoBehaviour
{

    [Header("FMOD Events")]
    [Tooltip("Loop event. Enabled after the third spark. Keep it disabled in the scene.")]
    public GameObject FMOD_Event1;

    [Tooltip("Dialogue event. Played line by line by the DialogueManager " +
             "(assign the same object to its FMOD_Event_Emitter_TV field).")]
    public GameObject FMOD_Event2;

    [Header("TV Screen")]
    [Tooltip("Camera that renders the TV screen. Its GameObject flickers on with each spark " +
             "and stays on after the last one.")]
    public Camera TV_Screen;

    [Header("TV Startup")]
    [Tooltip("Enabled after the third spark. Keep it disabled in the scene.")]
    public GameObject TV_Startup;

    public float maxSparkDuration = 0.12f;
    public float maxTimeBetweenSparks = 1.5f;

    [Tooltip("Minimum / maximum time a spark stays visible.")]
    public float minSparkDuration = 0.05f;

    [Tooltip("Minimum / maximum pause between sparks.")]
    public float minTimeBetweenSparks = 0.6f;
    [Header("Spark Particle Effects")]
    [Tooltip("The three spark particle effect GameObjects. Keep them disabled in the scene. " +
             "They are played once each, in a random order.")]
    public GameObject[] sparkEffects = new GameObject[3];

    [Header("Spark Light")]
    [Tooltip("GameObject containing the Area Light. Flickers on with each spark " +
             "and stays on after the last one.")]
    public GameObject sparkLightObject;

    [Header("Timing")]
    [Tooltip("Delay after entering play mode before the first spark.")]
    public float startDelay = 5f;

    [Tooltip("Start the TV sequence as soon as play mode starts. Off when the Stages system " +
             "starts it at the beginning of the Gameplay Loop (calls StartUp()).")]
    public bool startOnPlay = true;

    private bool startedUp;

    private int[] GetShuffledSparkOrder()
    {
        int[] order = new int[sparkEffects.Length];

        for (int i = 0; i < order.Length; i++)
        {
            order[i] = i;
        }

        // Fisher-Yates shuffle.
        for (int i = order.Length - 1; i > 0; i--)
        {
            int j = Random.Range(0, i + 1);

            int temp = order[i];
            order[i] = order[j];
            order[j] = temp;
        }

        return order;
    }

    private IEnumerator PlaySpark(GameObject sparkEffect)
    {
        if (sparkEffect == null)
        {
            Debug.LogWarning(
                "TV_StartUp_Script: A spark effect has not been assigned."
            );

            yield break;
        }

        float sparkDuration =
            Random.Range(minSparkDuration, maxSparkDuration);

        sparkEffect.SetActive(true);
        SetFlashActive(true);

        yield return new WaitForSeconds(sparkDuration);

        sparkEffect.SetActive(false);
        SetFlashActive(false);
    }

    // Turns the area light and TV screen GameObjects on/off together with the spark.
    private void SetFlashActive(bool active)
    {
        if (sparkLightObject != null)
        {
            sparkLightObject.SetActive(active);
        }

        if (TV_Screen != null)
        {
            TV_Screen.gameObject.SetActive(active);
        }
    }

    private void Start()
    {
        // Everything starts off until a spark happens.
        foreach (GameObject sparkEffect in sparkEffects)
        {
            if (sparkEffect != null)
            {
                sparkEffect.SetActive(false);
            }
        }

        SetFlashActive(false);

        if (TV_Startup != null)
        {
            TV_Startup.SetActive(false);
        }

        if (FMOD_Event1 != null)
        {
            FMOD_Event1.SetActive(false);
        }

        if (FMOD_Event2 != null)
        {
            FMOD_Event2.SetActive(false);
        }

        if (startOnPlay)
        {
            StartUp();
        }
    }

    // Starts the TV sequence (sparks, then screen, light and sound). Runs once.
    public void StartUp()
    {
        if (startedUp)
        {
            return;
        }

        startedUp = true;
        StartCoroutine(StartUpSequence());
    }

    private IEnumerator StartUpSequence()
    {
        yield return new WaitForSeconds(startDelay);

        int[] sparkOrder = GetShuffledSparkOrder();

        for (int i = 0; i < sparkOrder.Length; i++)
        {
            yield return PlaySpark(sparkEffects[sparkOrder[i]]);

            // No pause needed after the last spark.
            if (i < sparkOrder.Length - 1)
            {
                yield return new WaitForSeconds(
                    Random.Range(minTimeBetweenSparks, maxTimeBetweenSparks)
                );
            }
        }

        // The third spark has finished.
        // The area light and TV screen now stay on.
        SetFlashActive(true);

        if (TV_Startup != null)
        {
            TV_Startup.SetActive(true);
        }
        else
        {
            Debug.LogWarning(
                "TV_StartUp_Script: No TV_Startup object has been assigned."
            );
        }

        if (FMOD_Event1 != null)
        {
            FMOD_Event1.SetActive(true);
        }
        else
        {
            Debug.LogWarning(
                "TV_StartUp_Script: No FMOD_Event1 object has been assigned."
            );
        }
    }
}
