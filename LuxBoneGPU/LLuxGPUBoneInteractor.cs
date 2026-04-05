using UnityEngine;
using System.Collections.Generic;

/// <summary>
/// Per-character sphere bone interactor that registers with LLuxGPUBoneRegistry.
/// Exposes named bone entries as sphere colliders for inter-character pushing.
/// Used by LLuxGPUBoneOverseer and referenced from LLuxGPUBoneAdvanced for external collision resolution.
/// </summary>
[AddComponentMenu("Lux GPU Bone/LLux GPU Bone Interactor")]
public class LLuxGPUBoneInteractor : MonoBehaviour
{
    [System.Serializable]
    public class BoneEntry
    {
        [Tooltip("Bone transform to expose as a sphere collider.")]
        public Transform m_Bone = null;
        [Tooltip("Collision radius in world-scale units.")]
        public float m_Radius = 0.1f;
        [Tooltip("Local-space offset from the bone pivot.")]
        public Vector3 m_Offset = Vector3.zero;

        [System.NonSerialized] public Vector3 WorldCenter;
        [System.NonSerialized] public float WorldRadius;
    }

    [Header("Owner")]
    [Tooltip("Root GameObject of the character that owns these bones. Auto-assigned to this GameObject if left null.")]
    public GameObject m_Owner = null;

    [Header("Bone Colliders")]
    public List<BoneEntry> m_BoneEntries = new List<BoneEntry>();

    [Header("Interaction Filter")]
    public LayerMask m_AffectLayers = ~0;
    [Tooltip("Only affect LLuxGPUBone particles with this tag. Leave blank to affect all.")]
    public string m_AffectTag = "";

    public GameObject Owner => m_Owner != null ? m_Owner : gameObject;
    public IReadOnlyList<BoneEntry> BoneEntries => m_BoneEntries;

    void Awake() { if (m_Owner == null) m_Owner = gameObject; }
    void OnEnable() { LLuxGPUBoneRegistry.RegisterInteractor(this); }
    void OnDisable() { LLuxGPUBoneRegistry.UnregisterInteractor(this); }

    void LateUpdate()
    {
        float scale = Mathf.Abs(transform.lossyScale.x);
        for (int i = 0; i < m_BoneEntries.Count; ++i)
        {
            BoneEntry e = m_BoneEntries[i];
            if (e.m_Bone == null) continue;
            e.WorldCenter = e.m_Bone.TransformPoint(e.m_Offset);
            e.WorldRadius = e.m_Radius * scale;
        }
    }

    /// <summary>Sphere-push all matching bone entries against a particle position.</summary>
    public bool Collide(ref Vector3 particlePos, float particleRadius, GameObject requester)
    {
        if (!string.IsNullOrEmpty(m_AffectTag) && !requester.CompareTag(m_AffectTag))
            return false;
        if ((m_AffectLayers & (1 << requester.layer)) == 0)
            return false;

        bool hit = false;
        for (int i = 0; i < m_BoneEntries.Count; ++i)
        {
            BoneEntry e = m_BoneEntries[i];
            if (e.m_Bone == null) continue;
            float r = e.WorldRadius + particleRadius;
            Vector3 d = particlePos - e.WorldCenter;
            float dist = d.magnitude;
            if (dist < r && dist > 0.0001f)
            {
                particlePos = e.WorldCenter + d.normalized * r;
                hit = true;
            }
        }
        return hit;
    }

    void OnDrawGizmosSelected()
    {
        if (!enabled) return;
        float scale = Mathf.Abs(transform.lossyScale.x);
        for (int i = 0; i < m_BoneEntries.Count; ++i)
        {
            BoneEntry e = m_BoneEntries[i];
            if (e.m_Bone == null) continue;
            Vector3 center = Application.isPlaying ? e.WorldCenter : e.m_Bone.TransformPoint(e.m_Offset);
            float r = e.m_Radius * scale;
            Gizmos.color = new Color(1f, 0.5f, 0.1f, 0.8f);
            Gizmos.DrawWireSphere(center, r);
            Gizmos.color = new Color(1f, 0.8f, 0.2f, 0.9f);
            Gizmos.DrawWireSphere(e.m_Bone.position, 0.015f);
            Gizmos.DrawLine(e.m_Bone.position, center);
        }
    }
}
