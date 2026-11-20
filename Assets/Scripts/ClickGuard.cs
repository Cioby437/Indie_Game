using UnityEngine;

// Shared "only the first click counts" lock.
// CursorCameraManager locks it whenever a click is accepted; anything else that reacts to
// the left mouse button (e.g. CasettePlayerInspect) ignores clicks while it is locked.
public static class ClickGuard
{
    private static float lockedUntil = -1f;

    public static bool IsLocked
    {
        get { return Time.unscaledTime < lockedUntil; }
    }

    public static void Lock(float seconds)
    {
        lockedUntil = Mathf.Max(lockedUntil, Time.unscaledTime + seconds);
    }

    // Domain reload can be off in the editor, so reset the static between play sessions.
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetOnPlay()
    {
        lockedUntil = -1f;
    }
}
