using UnityEngine;
using System.Collections.Generic;

public static class LLuxGPUBoneRegistry
{
    private static List<LLuxGPUBoneWorldCollider> s_WorldColliders = new List<LLuxGPUBoneWorldCollider>();
    private static List<LLuxGPUBoneWindCollider> s_WindColliders = new List<LLuxGPUBoneWindCollider>();
    private static List<LLuxGPUBoneInteractor> s_Interactors = new List<LLuxGPUBoneInteractor>();

    /// <summary>Registers a world collider with the global registry.</summary>
    public static void RegisterWorldCollider(LLuxGPUBoneWorldCollider c) { if (!s_WorldColliders.Contains(c)) s_WorldColliders.Add(c); }
    /// <summary>Unregisters a world collider from the global registry.</summary>
    public static void UnregisterWorldCollider(LLuxGPUBoneWorldCollider c) { s_WorldColliders.Remove(c); }
    public static IReadOnlyList<LLuxGPUBoneWorldCollider> WorldColliders => s_WorldColliders;

    /// <summary>Registers a wind collider with the global registry.</summary>
    public static void RegisterWindCollider(LLuxGPUBoneWindCollider c) { if (!s_WindColliders.Contains(c)) s_WindColliders.Add(c); }
    /// <summary>Unregisters a wind collider from the global registry.</summary>
    public static void UnregisterWindCollider(LLuxGPUBoneWindCollider c) { s_WindColliders.Remove(c); }
    public static IReadOnlyList<LLuxGPUBoneWindCollider> WindColliders => s_WindColliders;

    /// <summary>Registers a bone interactor with the global registry.</summary>
    public static void RegisterInteractor(LLuxGPUBoneInteractor i) { if (!s_Interactors.Contains(i)) s_Interactors.Add(i); }
    /// <summary>Unregisters a bone interactor from the global registry.</summary>
    public static void UnregisterInteractor(LLuxGPUBoneInteractor i) { s_Interactors.Remove(i); }
    public static IReadOnlyList<LLuxGPUBoneInteractor> Interactors => s_Interactors;
}

[AddComponentMenu("Lux GPU Bone/LLux GPU Bone World Collider")]
public class LLuxGPUBoneWorldCollider : MonoBehaviour
{
    public enum Shape { Plane, Box, Capsule }
    public Shape m_Shape = Shape.Plane;
    public Vector3 m_Center = Vector3.zero;
    public Vector3 m_Size = Vector3.one;
    public Vector3 m_Normal = Vector3.up;

    private void OnEnable() => LLuxGPUBoneRegistry.RegisterWorldCollider(this);
    private void OnDisable() => LLuxGPUBoneRegistry.UnregisterWorldCollider(this);

    public bool Collide(ref Vector3 pos, float pr)
    {
        switch (m_Shape)
        {
            case Shape.Plane: return CollidePlane(ref pos, pr);
            case Shape.Box: return CollideBox(ref pos, pr);
            case Shape.Capsule: return CollideCapsule(ref pos, pr);
        }
        return false;
    }

    private bool CollidePlane(ref Vector3 pos, float pr)
    {
        Vector3 normal = transform.TransformDirection(m_Normal).normalized;
        Vector3 origin = transform.TransformPoint(m_Center);
        Vector3 localPos = transform.InverseTransformPoint(pos) - m_Center;

        if (Mathf.Abs(localPos.x) > m_Size.x * 0.5f || Mathf.Abs(localPos.z) > m_Size.z * 0.5f)
            return false;

        float dist = Vector3.Dot(pos - origin, normal);
        if (dist < pr && dist > -0.05f)
        {
            pos += normal * (pr - dist);
            return true;
        }
        return false;
    }

    private bool CollideBox(ref Vector3 pos, float pr)
    {
        Vector3 localPos = transform.InverseTransformPoint(pos) - m_Center;
        Vector3 half = m_Size * 0.5f;
        Vector3 worldScale = transform.lossyScale;
        Vector3 expandedHalf = half + new Vector3(
            pr / Mathf.Max(worldScale.x, 0.0001f),
            pr / Mathf.Max(worldScale.y, 0.0001f),
            pr / Mathf.Max(worldScale.z, 0.0001f)
        );

        if (Mathf.Abs(localPos.x) > expandedHalf.x ||
            Mathf.Abs(localPos.y) > expandedHalf.y ||
            Mathf.Abs(localPos.z) > expandedHalf.z) return false;

        float dx = expandedHalf.x - Mathf.Abs(localPos.x);
        float dy = expandedHalf.y - Mathf.Abs(localPos.y);
        float dz = expandedHalf.z - Mathf.Abs(localPos.z);

        Vector3 push = Vector3.zero;
        if (dx <= dy && dx <= dz) push.x = dx * Mathf.Sign(localPos.x);
        else if (dy <= dx && dy <= dz) push.y = dy * Mathf.Sign(localPos.y);
        else push.z = dz * Mathf.Sign(localPos.z);

        pos += transform.TransformDirection(push);
        return true;
    }

    private bool CollideCapsule(ref Vector3 pos, float pr)
    {
        Vector3 localPos = transform.InverseTransformPoint(pos) - m_Center;
        float radius = m_Size.x;
        float height = m_Size.y;
        float halfLine = Mathf.Max(0, (height * 0.5f) - radius);

        Vector3 closestPointOnLine = new Vector3(0, Mathf.Clamp(localPos.y, -halfLine, halfLine), 0);
        float distToLine = Vector3.Distance(localPos, closestPointOnLine);

        float worldScale = Mathf.Max(transform.lossyScale.x, transform.lossyScale.y, transform.lossyScale.z);
        float totalRadius = radius + (pr / Mathf.Max(worldScale, 0.0001f));

        if (distToLine < totalRadius && distToLine > 0.0001f)
        {
            Vector3 localPush = (localPos - closestPointOnLine).normalized * (totalRadius - distToLine);
            pos += transform.TransformDirection(localPush);
            return true;
        }
        return false;
    }

    private void OnDrawGizmos()
    {
        Gizmos.color = new Color(0.2f, 0.8f, 1f, 0.5f);

        switch (m_Shape)
        {
            case Shape.Box:
                Gizmos.matrix = transform.localToWorldMatrix;
                Gizmos.DrawWireCube(m_Center, m_Size);
                break;

            case Shape.Plane:
                Vector3 worldNormal = transform.TransformDirection(m_Normal).normalized;
                Vector3 worldOrigin = transform.TransformPoint(m_Center);
                Gizmos.DrawRay(worldOrigin, worldNormal * 0.5f);

                Gizmos.matrix = Matrix4x4.TRS(worldOrigin, transform.rotation * Quaternion.LookRotation(m_Normal), transform.lossyScale);
                Gizmos.DrawWireCube(Vector3.zero, new Vector3(m_Size.x, m_Size.z, 0f));
                break;

            case Shape.Capsule:
                float capRadius = m_Size.x;
                float capHeight = m_Size.y;
                float halfLineG = Mathf.Max(0, (capHeight * 0.5f) - capRadius);

                Gizmos.matrix = transform.localToWorldMatrix;
                Gizmos.DrawWireSphere(m_Center + Vector3.up * halfLineG, capRadius);
                Gizmos.DrawWireSphere(m_Center - Vector3.up * halfLineG, capRadius);
                Gizmos.DrawLine(m_Center + Vector3.up * halfLineG + Vector3.right * capRadius, m_Center - Vector3.up * halfLineG + Vector3.right * capRadius);
                Gizmos.DrawLine(m_Center + Vector3.up * halfLineG - Vector3.right * capRadius, m_Center - Vector3.up * halfLineG - Vector3.right * capRadius);
                Gizmos.DrawLine(m_Center + Vector3.up * halfLineG + Vector3.forward * capRadius, m_Center - Vector3.up * halfLineG + Vector3.forward * capRadius);
                Gizmos.DrawLine(m_Center + Vector3.up * halfLineG - Vector3.forward * capRadius, m_Center - Vector3.up * halfLineG - Vector3.forward * capRadius);
                break;
        }

        Gizmos.matrix = Matrix4x4.identity;
    }
}
