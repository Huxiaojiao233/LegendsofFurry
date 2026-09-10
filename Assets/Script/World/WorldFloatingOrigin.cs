using UnityEngine;

/// <summary>逻辑坐标不变，只平移场景根节点，避免远距离浮点抖动。</summary>
public sealed class WorldFloatingOrigin : MonoBehaviour
{
    [SerializeField] private float threshold = 128f;
    [SerializeField] private Transform root;

    public Vector3 VisualOffset { get; private set; }

    public void Bind(Transform worldRoot, float recenterThreshold = 128f)
    {
        root = worldRoot;
        threshold = Mathf.Max(8f, recenterThreshold);
    }

    public bool RecenterOn(Vector3 focusWorldPosition)
    {
        Transform target = root != null ? root : transform;
        Vector3 local = target.InverseTransformPoint(focusWorldPosition);
        local.y = 0f;
        if (local.sqrMagnitude < threshold * threshold) return false;
        target.position -= target.TransformVector(local);
        VisualOffset += local;
        return true;
    }
}
