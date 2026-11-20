using UnityEngine;

// Makes a collider clickable: clicking it opens / closes one animation of an AnimationScript
// (e.g. a drawer or a door of a cabinet). CursorCameraManager handles the hover cursor and the click.
// Put it on the object with the (trigger) collider, or on a parent of it.
public class ClickableAnimation : MonoBehaviour
{
    public AnimationScript animationScript;

    [Tooltip("The animation to play: its Animation State Name (or clip name) in the AnimationScript.")]
    public string animationName;

    [Tooltip("Only clickable while looking through the drawer / cabinet cameras. From the PlayerCamera, " +
             "clicking it goes to the drawer camera first.")]
    public bool onlyFromDrawerViews = true;

    public void Toggle()
    {
        if (animationScript == null)
        {
            Debug.LogWarning(name + ": ClickableAnimation has no AnimationScript.", this);
            return;
        }

        int index = animationScript.IndexOf(animationName);
        if (index < 0)
        {
            Debug.LogWarning(name + ": no animation named '" + animationName + "' in the AnimationScript.", this);
            return;
        }

        animationScript.PlayAnimation(index);
    }
}
