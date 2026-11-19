using System.Collections;
using UnityEngine;
using UnityEngine.InputSystem;

public class Anim_SubInteractive1_Interactive2 : MonoBehaviour
{
    private static readonly int WalkTrigger = Animator.StringToHash("Walk");

    [SerializeField] private Animator animator;
    [SerializeField] private float disableDelay = 6f;

    private bool triggered;

    void Start()
    {
        if (animator == null)
            animator = GetComponentInChildren<Animator>();
    }

    void Update()
    {
        if (triggered || Keyboard.current == null) return;

        if (Keyboard.current.spaceKey.wasPressedThisFrame)
        {
            triggered = true;

            if (animator != null)
                animator.SetTrigger(WalkTrigger);

            StartCoroutine(DisableAfterDelay());
        }
    }

    private IEnumerator DisableAfterDelay()
    {
        yield return new WaitForSeconds(disableDelay);
        gameObject.SetActive(false);
    }
}
