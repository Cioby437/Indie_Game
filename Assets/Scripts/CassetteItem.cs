using System.Collections;
using UnityEngine;

// A cassette that can go into the Casette Player (see CasettePlayerInspect, which drives the whole sequence).
// Put it on the ROOT of a Cassette.fbx instance (the object with the Animator). Any number can be in the scene.
//
// The Insert / Eject clips move the cassette relative to the Cassette.fbx root, which is the same point as the
// Cassette_Player.fbx root (both are the Blender origin). So while it goes in, this object sits under the player's
// socket with an identity transform, and the animation lines up with the player and its lid.
[RequireComponent(typeof(Animator))]
public class CassetteItem : MonoBehaviour
{
    [Tooltip("Cassette.fbx > Insert (the cassette going into the player).")]
    public AnimationClip insertClip;

    [Tooltip("Cassette.fbx > Eject (the cassette coming out of the player).")]
    public AnimationClip ejectClip;

    [Tooltip("Optional name / id, e.g. for which recording this cassette holds.")]
    public string cassetteId;

    [Tooltip("Rotation of the cassette relative to the camera while it is held in front of it.")]
    public Vector3 inspectRotation = Vector3.zero;

    [Tooltip("Name of the animated child as it comes from Blender. The clips find the object they move by this " +
             "name, so if the child was renamed (e.g. to \"Cassette\"), it gets this name back while playing.")]
    public string animatedChildName = "portable_cassette_player_casette";

    private ClipPlayer clips;
    private Collider[] colliders;

    private Transform homeParent;
    private Vector3 homeLocalPosition;
    private Quaternion homeLocalRotation;
    private Vector3 homeLocalScale;

    private bool inWorld = true;

    // Lying in the scene (not picked up, not in the player).
    public bool IsInWorld
    {
        get { return inWorld; }
    }

    private void Awake()
    {
        RestoreAnimatedChildName();

        clips = new ClipPlayer(GetComponent<Animator>());
        colliders = GetComponentsInChildren<Collider>(true);

        if (insertClip == null || ejectClip == null)
        {
            Debug.LogWarning(name + ": CassetteItem needs its Insert and Eject clips (from Cassette.fbx).", this);
        }
    }

    // Animation clips bind to objects by name. If the cassette mesh was renamed in the scene,
    // give it its Blender name back (only while playing; the scene keeps your name).
    private void RestoreAnimatedChildName()
    {
        if (string.IsNullOrEmpty(animatedChildName) || transform.Find(animatedChildName) != null)
        {
            return;
        }

        foreach (Transform child in transform)
        {
            if (child.GetComponent<MeshFilter>() != null)
            {
                child.name = animatedChildName;
                GetComponent<Animator>().Rebind();
                return;
            }
        }

        Debug.LogWarning(name + ": no child named '" + animatedChildName + "' (or with a mesh) to animate.", this);
    }

    // Picked up: remembers where it was found.
    public void BeginPickUp()
    {
        inWorld = false;

        homeParent = transform.parent;
        homeLocalPosition = transform.localPosition;
        homeLocalRotation = transform.localRotation;
        homeLocalScale = transform.localScale;

        // Cassette mesh at the first frame of Insert (its resting pose).
        clips.PoseAt(insertClip, 0f);
    }

    public IEnumerator PlayInsert()
    {
        yield return clips.Play(insertClip);
    }

    public IEnumerator PlayEject()
    {
        yield return clips.Play(ejectClip);
    }

    // Flies back to where it was found and becomes clickable again.
    public IEnumerator ReturnHome(float duration)
    {
        transform.SetParent(homeParent, true);
        yield return FlyLocal(homeLocalPosition, homeLocalRotation, homeLocalScale, duration);

        SetCollidersEnabled(true);
        inWorld = true;
    }

    // Moves in its parent's space with an ease-in / ease-out.
    public IEnumerator FlyLocal(Vector3 toPosition, Quaternion toRotation, Vector3 toScale, float duration)
    {
        Vector3 fromPosition = transform.localPosition;
        Quaternion fromRotation = transform.localRotation;
        Vector3 fromScale = transform.localScale;
        float elapsed = 0f;

        while (elapsed < duration)
        {
            elapsed += Time.deltaTime;

            float t = Mathf.Clamp01(elapsed / duration);
            t = t * t * (3f - 2f * t);

            transform.localPosition = Vector3.Lerp(fromPosition, toPosition, t);
            transform.localRotation = Quaternion.Slerp(fromRotation, toRotation, t);
            transform.localScale = Vector3.Lerp(fromScale, toScale, t);

            yield return null;
        }

        transform.localPosition = toPosition;
        transform.localRotation = toRotation;
        transform.localScale = toScale;
    }

    public bool IsMyCollider(Collider c)
    {
        return c != null && c.transform.IsChildOf(transform);
    }

    public void SetCollidersEnabled(bool enabled)
    {
        foreach (Collider c in colliders)
        {
            if (c != null) c.enabled = enabled;
        }
    }

    private void OnDestroy()
    {
        if (clips != null)
        {
            clips.Dispose();
        }
    }
}
