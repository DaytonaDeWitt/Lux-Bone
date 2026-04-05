using UnityEngine;
using System.Collections.Generic;

#if UNITY_EDITOR
using UnityEditor;
#endif

/// <summary>
/// Character-level manager for the LLuxGPUBone system.
/// Auto-collects all LLuxGPUBoneAdvanced, LLuxGPUBoneCollider, world and wind colliders
/// from the hierarchy. Drives the distance-optimization trigger.
/// </summary>
[AddComponentMenu("Lux GPU Bone/LLux GPU Bone Overseer")]
[RequireComponent(typeof(LLuxGPUBoneInteractor))]
public class LLuxGPUBoneOverseer : MonoBehaviour
{
    // ─────────────────────────────────────────────────────────────────────────
    // Enums
    // ─────────────────────────────────────────────────────────────────────────

    public enum AssignMode { AutoHumanoid, Manual }

    // ─────────────────────────────────────────────────────────────────────────
    // Auto Assign
    // ─────────────────────────────────────────────────────────────────────────

    [Header("Auto Assign")]
    public AssignMode m_AssignMode = AssignMode.AutoHumanoid;
    [Tooltip("Run full auto-assign on Start.")]
    public bool m_AutoAssign = true;
    [Tooltip("Auto-collect all LLuxGPUBoneAdvanced components from hierarchy.")]
    public bool m_AutoCollectBones = true;
    [Tooltip("Auto-collect all LLuxGPUBoneCollider components from hierarchy.")]
    public bool m_AutoCollectColliders = true;

    // ─────────────────────────────────────────────────────────────────────────
    // Body Part Radii
    // ─────────────────────────────────────────────────────────────────────────

    [Header("Body Part Radii")]
    public float m_HeadRadius = 0.12f;
    public float m_ChestRadius = 0.18f;
    public float m_WaistRadius = 0.15f;
    public float m_HandRadius = 0.07f;
    public float m_FootRadius = 0.09f;
    public float m_FingerRadius = 0.015f;
    public float m_ToeRadius = 0.035f;

    // ─────────────────────────────────────────────────────────────────────────
    // Deformation Settings
    // ─────────────────────────────────────────────────────────────────────────

    [Header("Deformation")]
    [Tooltip("Add deformer to body bones (chest, waist, head) as well as fingers.")]
    public bool m_DeformBodyBones = true;
    public float m_BodyDeformMax = 0.015f;
    public float m_BodyDeformStiff = 0.7f;
    [Tooltip("Collect and setup fingers for LLuxGPUHandBoneDeform.")]
    public bool m_AutoSetupFingers = true;

    // ─────────────────────────────────────────────────────────────────────────
    // Performance / Optimization
    // ─────────────────────────────────────────────────────────────────────────

    [Header("Performance")]
    [Tooltip("Enable distance-based optimization using a trigger box.")]
    public bool m_EnableDistanceOptimization = true;
    [Tooltip("The size of the square optimization area.")]
    public Vector3 m_OptimizationBounds = new Vector3(10f, 10f, 10f);
    [Tooltip("Layers to check for player/interactor detection.")]
    public LayerMask m_PlayerLayers = ~0;
    [TagField]
    [Tooltip("Filter optimization trigger to only certain tags. Leave 'Untagged' or empty to allow all tags on the specified layer.")]
    public string m_OptimizationTag = "Player";

    private BoxCollider m_OptimizationTrigger;

    // ─────────────────────────────────────────────────────────────────────────
    // Manual Bone Overrides
    // ─────────────────────────────────────────────────────────────────────────

    [Header("Manual Bone Overrides")]
    public Transform m_HeadBone = null;
    public Transform m_ChestBone = null;
    public Transform m_WaistBone = null;
    public Transform m_LeftHandBone = null;
    public Transform m_RightHandBone = null;
    public Transform m_LeftFootBone = null;
    public Transform m_RightFootBone = null;
    public Transform m_LeftToeBone = null;
    public Transform m_RightToeBone = null;

    // ─────────────────────────────────────────────────────────────────────────
    // External Collision
    // ─────────────────────────────────────────────────────────────────────────

    [Header("External Collision")]
    public bool m_EnableExternalOnAllBones = true;
    public float m_ExternalCollisionRange = 2f;

    // ─────────────────────────────────────────────────────────────────────────
    // Runtime Collections
    // ─────────────────────────────────────────────────────────────────────────

    private Animator m_Animator;
    private LLuxGPUHandBoneDeform m_HandDeform;

    [Header("GPU Collections")]
    public List<LLuxGPUBoneAdvanced> m_BoneSimulations = new List<LLuxGPUBoneAdvanced>();
    public List<LLuxGPUBoneCollider> m_CharacterColliders = new List<LLuxGPUBoneCollider>();
    public List<LLuxGPUBoneWorldCollider> m_WorldColliders = new List<LLuxGPUBoneWorldCollider>();
    public List<LLuxGPUBoneWindCollider> m_WindColliders = new List<LLuxGPUBoneWindCollider>();

    // ─────────────────────────────────────────────────────────────────────────
    // Unity Lifecycle
    // ─────────────────────────────────────────────────────────────────────────

    void Start()
    {
        m_Animator = GetComponent<Animator>();
        m_HandDeform = GetComponent<LLuxGPUHandBoneDeform>();

        if (m_AutoAssign)
        {
            AutoCollectAll();
        }

        SetupOptimizationTrigger();
    }

    private void SetupOptimizationTrigger()
    {
        if (!m_EnableDistanceOptimization) return;

        m_OptimizationTrigger = GetComponent<BoxCollider>();
        if (m_OptimizationTrigger == null)
        {
            m_OptimizationTrigger = gameObject.AddComponent<BoxCollider>();
        }

        m_OptimizationTrigger.isTrigger = true;
        m_OptimizationTrigger.size = m_OptimizationBounds;
        m_OptimizationTrigger.center = Vector3.zero;

        SetSimState(false);
    }

    private int m_InteractorCount = 0;

    private void OnTriggerEnter(Collider other)
    {
        if (!m_EnableDistanceOptimization) return;

        if (((1 << other.gameObject.layer) & m_PlayerLayers) == 0) return;

        if (!string.IsNullOrEmpty(m_OptimizationTag) && m_OptimizationTag != "Untagged")
        {
            if (!other.CompareTag(m_OptimizationTag)) return;
        }

        m_InteractorCount++;
        UpdateSimState();
    }

    private void OnTriggerExit(Collider other)
    {
        if (!m_EnableDistanceOptimization) return;

        if (((1 << other.gameObject.layer) & m_PlayerLayers) == 0) return;

        if (!string.IsNullOrEmpty(m_OptimizationTag) && m_OptimizationTag != "Untagged")
        {
            if (!other.CompareTag(m_OptimizationTag)) return;
        }

        m_InteractorCount = Mathf.Max(0, m_InteractorCount - 1);
        UpdateSimState();
    }

    private void UpdateSimState()
    {
        SetSimState(m_InteractorCount > 0);
    }

    private void SetSimState(bool active)
    {
        foreach (var bone in m_BoneSimulations)
        {
            if (bone != null) bone.enabled = active;
        }
        if (m_HandDeform != null) m_HandDeform.enabled = active;
    }

    void OnValidate()
    {
        if (m_OptimizationTrigger != null)
        {
            m_OptimizationTrigger.size = m_OptimizationBounds;
            m_OptimizationTrigger.enabled = m_EnableDistanceOptimization;
        }
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Auto Collection
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>Auto-collects all bone simulations, colliders, and interactor bone entries from the hierarchy.</summary>
    public void AutoCollectAll()
    {
        m_BoneSimulations.Clear();
        m_CharacterColliders.Clear();

        m_Animator = GetComponent<Animator>();
        m_HandDeform = GetComponent<LLuxGPUHandBoneDeform>();
        LLuxGPUBoneInteractor interactor = GetComponent<LLuxGPUBoneInteractor>();

        m_BoneSimulations.AddRange(GetComponentsInChildren<LLuxGPUBoneAdvanced>(true));
        m_CharacterColliders.AddRange(GetComponentsInChildren<LLuxGPUBoneCollider>(true));

        if (m_EnableDistanceOptimization)
        {
            SetupOptimizationTrigger();
        }

        foreach (var bone in m_BoneSimulations)
        {
            if (bone.m_Root == null) bone.m_Root = bone.transform;
            if (bone.m_GPUColliders == null) bone.m_GPUColliders = new List<LLuxGPUBoneCollider>();
            bone.m_GPUColliders.Clear();
            bone.m_GPUColliders.AddRange(m_CharacterColliders);
        }

        if (m_AssignMode == AssignMode.AutoHumanoid && m_Animator != null && m_Animator.isHuman)
        {
            m_HeadBone = m_Animator.GetBoneTransform(HumanBodyBones.Head);
            m_ChestBone = m_Animator.GetBoneTransform(HumanBodyBones.UpperChest) ?? m_Animator.GetBoneTransform(HumanBodyBones.Chest);
            m_WaistBone = m_Animator.GetBoneTransform(HumanBodyBones.Hips);
            m_LeftHandBone = m_Animator.GetBoneTransform(HumanBodyBones.LeftHand);
            m_RightHandBone = m_Animator.GetBoneTransform(HumanBodyBones.RightHand);
            m_LeftFootBone = m_Animator.GetBoneTransform(HumanBodyBones.LeftFoot);
            m_RightFootBone = m_Animator.GetBoneTransform(HumanBodyBones.RightFoot);
            m_LeftToeBone = m_Animator.GetBoneTransform(HumanBodyBones.LeftToes);
            m_RightToeBone = m_Animator.GetBoneTransform(HumanBodyBones.RightToes);

            if (m_AutoSetupFingers && m_HandDeform != null)
            {
                SetupFingers(m_HandDeform);
            }
        }

        if (interactor != null)
        {
            interactor.m_BoneEntries.Clear();
            AddBoneToInteractor(interactor, m_HeadBone, m_HeadRadius);
            AddBoneToInteractor(interactor, m_ChestBone, m_ChestRadius);
            AddBoneToInteractor(interactor, m_WaistBone, m_WaistRadius);
            AddBoneToInteractor(interactor, m_LeftHandBone, m_HandRadius);
            AddBoneToInteractor(interactor, m_RightHandBone, m_HandRadius);
            AddBoneToInteractor(interactor, m_LeftFootBone, m_FootRadius);
            AddBoneToInteractor(interactor, m_RightFootBone, m_FootRadius);
            AddBoneToInteractor(interactor, m_LeftToeBone, m_ToeRadius);
            AddBoneToInteractor(interactor, m_RightToeBone, m_ToeRadius);

            if (m_AutoSetupFingers)
            {
                AddFingersToInteractor(interactor);
            }
        }
    }

    private void AddFingersToInteractor(LLuxGPUBoneInteractor interactor)
    {
        if (m_Animator == null || !m_Animator.isHuman) return;

        HumanBodyBones[] fingerBones =
        {
            HumanBodyBones.LeftThumbDistal, HumanBodyBones.LeftIndexDistal, HumanBodyBones.LeftMiddleDistal, HumanBodyBones.LeftRingDistal, HumanBodyBones.LeftLittleDistal,
            HumanBodyBones.RightThumbDistal, HumanBodyBones.RightIndexDistal, HumanBodyBones.RightMiddleDistal, HumanBodyBones.RightRingDistal, HumanBodyBones.RightLittleDistal
        };

        foreach (var finger in fingerBones)
        {
            Transform bone = m_Animator.GetBoneTransform(finger);
            if (bone != null) AddBoneToInteractor(interactor, bone, m_FingerRadius);
        }
    }

    private void SetupFingers(LLuxGPUHandBoneDeform handDeform)
    {
        handDeform.m_Fingers.Clear();
        SetupHand(handDeform, "Left");
        SetupHand(handDeform, "Right");
        handDeform.CaptureRestPoses();
    }

    private void SetupHand(LLuxGPUHandBoneDeform handDeform, string side)
    {
        string[] types = { "Thumb", "Index", "Middle", "Ring", "Little" };
        foreach (var type in types)
        {
            var finger = new LLuxGPUHandBoneDeform.FingerChain();
            finger.m_Name = side + " " + type;

            System.Enum.TryParse(side + type + "Proximal", out HumanBodyBones p);
            System.Enum.TryParse(side + type + "Intermediate", out HumanBodyBones i);
            System.Enum.TryParse(side + type + "Distal", out HumanBodyBones d);

            finger.m_Proximal = m_Animator.GetBoneTransform(p);
            finger.m_Intermediate = m_Animator.GetBoneTransform(i);
            finger.m_Distal = m_Animator.GetBoneTransform(d);

            if (finger.m_Distal != null) handDeform.m_Fingers.Add(finger);
        }
    }

    private void AddBoneToInteractor(LLuxGPUBoneInteractor interactor, Transform bone, float radius)
    {
        if (bone == null) return;
        interactor.m_BoneEntries.Add(new LLuxGPUBoneInteractor.BoneEntry
        {
            m_Bone = bone,
            m_Radius = radius,
            m_Offset = Vector3.zero
        });
    }

    /// <summary>Refreshes world collider references. World/wind colliders are tracked dynamically via LLuxGPUBoneRegistry.</summary>
    public void RefreshWorldColliders() { }

    void OnDrawGizmosSelected()
    {
        if (m_EnableDistanceOptimization)
        {
            Gizmos.color = new Color(1f, 0f, 0f, 0.4f);
            Gizmos.matrix = transform.localToWorldMatrix;
            Gizmos.DrawWireCube(Vector3.zero, m_OptimizationBounds);
            Gizmos.color = new Color(1f, 0f, 0f, 0.05f);
            Gizmos.DrawCube(Vector3.zero, m_OptimizationBounds);
        }

        Gizmos.matrix = Matrix4x4.identity;
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Public Properties
    // ─────────────────────────────────────────────────────────────────────────

    public IReadOnlyList<LLuxGPUBoneAdvanced> BoneSimulations => m_BoneSimulations.AsReadOnly();
    public IReadOnlyList<LLuxGPUBoneCollider> CharacterColliders => m_CharacterColliders.AsReadOnly();
    public IReadOnlyList<LLuxGPUBoneWorldCollider> WorldColliders => m_WorldColliders.AsReadOnly();
    public IReadOnlyList<LLuxGPUBoneWindCollider> WindColliders => m_WindColliders.AsReadOnly();
}
