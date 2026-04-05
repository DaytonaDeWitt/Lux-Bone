using UnityEngine;
using System.Collections.Generic;

/// <summary>
/// Deforms finger bones when they contact world colliders or interactor bones.
/// Uses LLuxGPUBoneRegistry for world collider access.
/// </summary>
[AddComponentMenu("Lux GPU Bone/LLux GPU Hand Bone Deform")]
public class LLuxGPUHandBoneDeform : MonoBehaviour
{
    [Header("Settings")]
    public float m_MaxDeformation = 0.05f;
    public float m_DeformationStiffness = 0.5f;
    [Tooltip("How much the tip bends relative to the middle joint.")]
    public float m_TipBendFactor = 1.2f;

    [Header("Bones")]
    public List<FingerChain> m_Fingers = new List<FingerChain>();

    [System.Serializable]
    public class FingerChain
    {
        public string m_Name;
        public Transform m_Proximal;
        public Transform m_Intermediate;
        public Transform m_Distal;

        [HideInInspector] public Quaternion m_RestProximal;
        [HideInInspector] public Quaternion m_RestIntermediate;
        [HideInInspector] public Quaternion m_RestDistal;
    }

    private void Start()
    {
        CaptureRestPoses();
    }

    /// <summary>Captures and stores the current rest poses for all finger chains.</summary>
    public void CaptureRestPoses()
    {
        foreach (var finger in m_Fingers)
        {
            if (finger.m_Proximal) finger.m_RestProximal = finger.m_Proximal.localRotation;
            if (finger.m_Intermediate) finger.m_RestIntermediate = finger.m_Intermediate.localRotation;
            if (finger.m_Distal) finger.m_RestDistal = finger.m_Distal.localRotation;
        }
    }

    private void LateUpdate()
    {
        LuxBoneInteractor interactor = GetComponent<LuxBoneInteractor>();
        if (interactor == null) return;

        var worldColliders = LLuxGPUBoneRegistry.WorldColliders;

        foreach (var finger in m_Fingers)
        {
            if (finger.m_Distal == null) continue;

            Vector3 currentPos = finger.m_Distal.position;
            Vector3 targetPos = currentPos;
            bool hit = false;

            foreach (var entry in interactor.m_BoneEntries)
            {
                if (entry.m_Bone == finger.m_Distal)
                {
                    if (Vector3.Distance(currentPos, entry.WorldCenter) > 0.001f)
                    {
                        targetPos = entry.WorldCenter;
                        hit = true;
                    }
                    break;
                }
            }

            float fingerRadius = 0.02f;
            foreach (var worldCol in worldColliders)
            {
                if (worldCol.Collide(ref targetPos, fingerRadius))
                {
                    hit = true;
                }
            }

            if (hit)
            {
                float pressure = Vector3.Distance(currentPos, targetPos);
                ApplyBend(finger, pressure);
            }
            else
            {
                RestorePose(finger);
            }
        }
    }

    private void OnDrawGizmosSelected()
    {
        Gizmos.color = new Color(0.2f, 1f, 0.5f, 0.7f);
        foreach (var finger in m_Fingers)
        {
            if (finger.m_Distal)
            {
                Gizmos.DrawWireSphere(finger.m_Distal.position, 0.02f);
                if (finger.m_Intermediate) Gizmos.DrawLine(finger.m_Intermediate.position, finger.m_Distal.position);
                if (finger.m_Proximal) Gizmos.DrawLine(finger.m_Proximal.position, finger.m_Intermediate.position);
            }
        }
    }

    private void ApplyBend(FingerChain finger, float pressure)
    {
        float intensity = Mathf.Clamp01(pressure / m_MaxDeformation) * m_DeformationStiffness;
        float bendAngle = intensity * 45f;

        if (finger.m_Intermediate)
            finger.m_Intermediate.localRotation = finger.m_RestIntermediate * Quaternion.Euler(bendAngle, 0, 0);
        if (finger.m_Distal)
            finger.m_Distal.localRotation = finger.m_RestDistal * Quaternion.Euler(bendAngle * m_TipBendFactor, 0, 0);
    }

    private void RestorePose(FingerChain finger)
    {
        if (finger.m_Intermediate)
            finger.m_Intermediate.localRotation = Quaternion.Slerp(finger.m_Intermediate.localRotation, finger.m_RestIntermediate, Time.deltaTime * 10f);
        if (finger.m_Distal)
            finger.m_Distal.localRotation = Quaternion.Slerp(finger.m_Distal.localRotation, finger.m_RestDistal, Time.deltaTime * 10f);
    }
}
