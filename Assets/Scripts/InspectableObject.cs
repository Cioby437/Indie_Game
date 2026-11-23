using UnityEngine;

// Put this on any object that can be picked up and looked at (e.g. the office notepad).
// The object needs a collider (on it or a child). ObjectInspector does the rest:
//   click it        -> it floats in front of the camera, the background blurs, it turns with the cursor
//   click elsewhere -> it goes back to exactly where it was
//
// Objects named in ObjectInspector's "Auto Inspectable Names" get this component at start automatically.
public class InspectableObject : MonoBehaviour
{
    [Tooltip("Turn the side that faces up in the room (e.g. the notepad's top page) towards the camera.")]
    public bool showTopSideToCamera = true;

    [Tooltip("Extra rotation (degrees, camera space) on top of the automatic facing. Use it to fine-tune.")]
    public Vector3 extraRotation = Vector3.zero;

    [Tooltip("How much of the screen height the object fills in front of the camera (0.1 - 1).")]
    [Range(0.1f, 1f)] public float screenFill = 0.6f;

    [Tooltip("How far (degrees) the object turns when the cursor is at the edge of the screen.")]
    public float maxRotationAngle = 35f;

    // Optional per-object override of the distance from the camera. 0 = worked out from Screen Fill.
    [Tooltip("0 = worked out from Screen Fill.")]
    public float inspectDistance = 0f;

    public bool IsMyCollider(Collider c)
    {
        return c != null && c.transform.IsChildOf(transform);
    }
}
