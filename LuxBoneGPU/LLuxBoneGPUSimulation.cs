using UnityEngine;
using System.Collections.Generic;

/// <summary>
/// Per-bone GPU simulation component that registers with LLuxBoneGPUManager
/// and submits particle data each frame.
/// </summary>
[AddComponentMenu("Lux Bone/LLux Bone GPU")]
public class LLuxBoneGPUSimulation : MonoBehaviour
{
    // ── Enums ─────────────────────────────────────────────────────────────────
    public enum FreezeAxis { None, X, Y, Z }
    public enum PhysicsPreset { Custom, Hair, Ear, Flap, Clothing, Metal, Toon }

    // ── Root ──────────────────────────────────────────────────────────────────
    [Header("Root")]
    [Tooltip("Root bone transform. Auto-assigns to this GameObject if left null.")]
    public Transform m_Root = null;

    // ── Physics Preset ────────────────────────────────────────────────────────
    [Header("Physics")]
    public PhysicsPreset m_Preset = PhysicsPreset.Custom;
    public float m_UpdateRate = 60f;

    [Range(0, 1)] public float m_Damping = 0.1f;
    [Range(0, 1)] public float m_Elasticity = 0.1f;
    [Range(0, 1)] public float m_Stiffness = 0.1f;
    [Range(0, 1)] public float m_Inert = 0f;
    public float m_Radius = 0f;

    // ── Forces ────────────────────────────────────────────────────────────────
    [Header("Forces")]
    public Vector3 m_Gravity = Vector3.zero;
    public Vector3 m_Force = Vector3.zero;

    // ── Wind ──────────────────────────────────────────────────────────────────
    [Header("Wind")]
    public float m_WindStrength = 1f;
    public float m_WindFrequency = 0.8f;
    public float m_WindTurbulence = 0.3f;

    // ── Constraints ───────────────────────────────────────────────────────────
    [Header("Constraints")]
    public FreezeAxis m_FreezeAxis = FreezeAxis.None;

    [Header("End Bone")]
    public float m_EndLength = 0f;
    public Vector3 m_EndOffset = Vector3.zero;

    // ── Collisions ────────────────────────────────────────────────────────────
    [Header("Colliders")]
    [Tooltip("Auto-scans hierarchy for LuxBoneCollider components.")]
    public bool m_AutoCollect = true;
    public List<LuxBoneCollider> m_Colliders = null;

    [Header("World Colliders")]
    public bool m_AutoCollectWorldColliders = true;

    [Header("External Collisions")]
    public bool m_EnableExternalCollisions = false;
    public float m_ExternalCollisionRange = 2f;

    // ── Distance Disable ──────────────────────────────────────────────────────
    [Header("Distance Disable")]
    public bool m_DistantDisable = false;
    public Transform m_ReferenceObject = null;
    public float m_DistanceToObject = 20f;

    // ── Internal: Particles ───────────────────────────────────────────────────
    struct GpuParticle
    {
        public Vector3 position;
        public Vector3 prevPosition;
        public Vector3 force;
        public float damping;
        public float elasticity;
        public float stiffness;
        public float inert;
        public float radius;
        public Vector4 restPosition;
        public int parentIndex;
        public int transformIndex;
        public float pad0;
        public float pad1;
    }

    struct GpuCollider
    {
        public Vector3 center;
        public float radius;
        public int type;
        public int bound;
        public Vector2 pad;
    }

    private List<GpuParticle> m_Particles = new List<GpuParticle>();
    private List<Vector4> m_Constraints = new List<Vector4>();
    private List<Transform> m_TransformReferences = new List<Transform>();

    private Vector3 m_ObjectMove;
    private Vector3 m_ObjectPrevPosition;
    private float m_ObjectScale = 1f;
    private float m_Weight = 1f;
    private bool m_DistantDisabled = false;

    private LLuxBoneGPUManager m_GPUManager;
    private List<LuxWorldCollider> m_WorldColliders = new List<LuxWorldCollider>();
    private List<LuxBoneInteractor> m_NearbyInteractors = new List<LuxBoneInteractor>();

    // ── Lifecycle ─────────────────────────────────────────────────────────────
    void OnEnable()
    {
        m_GPUManager = LLuxBoneGPUManager.Instance;
        m_GPUManager.RegisterSimulation(this);
    }

    void OnDisable()
    {
        if (m_GPUManager != null)
            m_GPUManager.UnregisterSimulation(this);
    }

    void Start()
    {
        if (m_Root == null) m_Root = transform;

        if (m_AutoCollect && m_Colliders == null)
        {
            m_Colliders = new List<LuxBoneCollider>();
            foreach (var c in transform.root.GetComponentsInChildren<LuxBoneCollider>())
                if (!m_Colliders.Contains(c))
                    m_Colliders.Add(c);
        }

        SetupParticles();
        RefreshWorldColliders();
    }

    void Update()
    {
        if (m_Weight > 0 && !(m_DistantDisable && m_DistantDisabled))
            InitTransforms();
    }

    void LateUpdate()
    {
        if (m_DistantDisable) CheckDistance();

        if (m_Weight > 0 && !(m_DistantDisable && m_DistantDisabled))
            SimulateOnGPU();
    }

    // ── Particle Setup ────────────────────────────────────────────────────────
    void SetupParticles()
    {
        m_Particles.Clear();
        m_TransformReferences.Clear();
        m_Constraints.Clear();

        AppendParticles(m_Root, -1, 0f);
        ResetParticlesPosition();
    }

    void AppendParticles(Transform b, int parentIndex, float boneLength)
    {
        GpuParticle p = new GpuParticle
        {
            parentIndex = parentIndex,
            transformIndex = b != null ? m_TransformReferences.Count : -1,
            damping = m_Damping,
            elasticity = m_Elasticity,
            stiffness = m_Stiffness,
            inert = m_Inert,
            radius = m_Radius,
            position = b != null ? b.position : Vector3.zero,
            prevPosition = b != null ? b.position : Vector3.zero,
            restPosition = Vector4.zero
        };

        if (b != null)
            m_TransformReferences.Add(b);

        int index = m_Particles.Count;
        m_Particles.Add(p);

        m_Constraints.Add(Vector4.zero);

        if (b != null)
        {
            for (int i = 0; i < b.childCount; ++i)
                AppendParticles(b.GetChild(i), index, boneLength);

            if (b.childCount == 0 && (m_EndLength > 0 || m_EndOffset != Vector3.zero))
                AppendParticles(null, index, boneLength);
        }
    }

    void InitTransforms()
    {
        for (int i = 0; i < m_TransformReferences.Count; i++)
        {
            Transform t = m_TransformReferences[i];
            if (t != null && i < m_Particles.Count)
            {
                GpuParticle p = m_Particles[i];
            }
        }
    }

    void ResetParticlesPosition()
    {
        for (int i = 0; i < m_Particles.Count; ++i)
        {
            GpuParticle p = m_Particles[i];
            if (p.transformIndex >= 0 && p.transformIndex < m_TransformReferences.Count)
            {
                Transform t = m_TransformReferences[p.transformIndex];
                if (t != null)
                {
                    p.position = t.position;
                    p.prevPosition = t.position;
                }
            }
            else if (p.parentIndex >= 0)
            {
                Transform parent = m_TransformReferences[p.parentIndex];
                if (parent != null)
                {
                    p.position = parent.TransformPoint(p.restPosition);
                    p.prevPosition = p.position;
                }
            }
            m_Particles[i] = p;
        }

        m_ObjectPrevPosition = transform.position;
    }

    // ── GPU Simulation ────────────────────────────────────────────────────────
    void SimulateOnGPU()
    {
        m_ObjectMove = transform.position - m_ObjectPrevPosition;
        m_ObjectPrevPosition = transform.position;
        m_ObjectScale = Mathf.Abs(transform.lossyScale.x);

        UpdateConstraints();

        GpuCollider[] colliders = BuildColliderArray();

        if (m_AutoCollectWorldColliders)
            RefreshWorldColliders();

        int freezeAxis = (int)m_FreezeAxis;
        m_GPUManager.SimulateStep(
            m_Gravity,
            m_Force,
            m_ObjectMove,
            Time.deltaTime,
            m_ObjectScale,
            m_Weight,
            ConvertParticles(m_Particles),
            m_Particles.Count,
            ConvertColliders(colliders),
            colliders.Length,
            new LLuxBoneGPUManager.WindZoneData[0],
            0,
            m_Constraints.ToArray(),
            freezeAxis
        );

        ApplyParticlesToTransforms();
    }

    void UpdateConstraints()
    {
        for (int i = 0; i < m_Particles.Count; ++i)
        {
            GpuParticle p = m_Particles[i];
            if (p.parentIndex >= 0)
            {
                GpuParticle p0 = m_Particles[p.parentIndex];
                Transform t0 = p0.transformIndex >= 0
                    ? m_TransformReferences[p0.transformIndex]
                    : null;
                Transform t = p.transformIndex >= 0
                    ? m_TransformReferences[p.transformIndex]
                    : null;

                Vector3 restPos = Vector3.zero;
                float restLen = 0f;

                if (t != null && t0 != null)
                {
                    restPos = t0.position + (t.position - t0.position);
                    restLen = (t0.position - t.position).magnitude;
                }
                else if (p.parentIndex >= 0)
                {
                    restLen = Vector3.Distance(p0.position, p.position);
                }

                m_Constraints[i] = new Vector4(restPos.x, restPos.y, restPos.z, restLen);
            }
        }
    }

    GpuCollider[] BuildColliderArray()
    {
        List<GpuCollider> colliders = new List<GpuCollider>();

        if (m_Colliders != null)
        {
            foreach (var c in m_Colliders)
            {
                if (c == null || !c.enabled) continue;

                GpuCollider gc = new GpuCollider
                {
                    center = c.transform.position,
                    radius = c.m_Radius * m_ObjectScale,
                    type = (int)c.m_Shape,
                    bound = (int)c.m_Bound
                };
                colliders.Add(gc);
            }
        }

        foreach (var wc in m_WorldColliders)
        {
            if (wc == null || !wc.enabled) continue;

            GpuCollider gc = new GpuCollider
            {
                center = wc.transform.position,
                radius = wc.m_BoxSize.magnitude * 0.5f,
                type = (int)wc.m_Shape,
                bound = 0
            };
            colliders.Add(gc);
        }

        return colliders.ToArray();
    }

    void ApplyParticlesToTransforms()
    {
        for (int i = 0; i < m_Particles.Count; ++i)
        {
            GpuParticle p = m_Particles[i];
            if (p.transformIndex >= 0 && p.transformIndex < m_TransformReferences.Count)
            {
                Transform t = m_TransformReferences[p.transformIndex];
                if (t != null)
                    t.position = p.position;
            }
        }
    }

    void RefreshWorldColliders()
    {
        m_WorldColliders.Clear();
        m_WorldColliders.AddRange(LuxBoneRegistry.WorldColliders);
    }

    void CheckDistance()
    {
        Transform rt = m_ReferenceObject;
        if (rt == null && Camera.main != null) rt = Camera.main.transform;
        if (rt == null) return;

        float d = (rt.position - transform.position).sqrMagnitude;
        bool disable = d > m_DistanceToObject * m_DistanceToObject;

        if (disable != m_DistantDisabled)
        {
            if (!disable) ResetParticlesPosition();
            m_DistantDisabled = disable;
        }
    }

    // ── Utility ───────────────────────────────────────────────────────────────
    LLuxBoneGPUManager.Particle[] ConvertParticles(List<GpuParticle> gpuParticles)
    {
        var converted = new LLuxBoneGPUManager.Particle[gpuParticles.Count];
        return converted;
    }

    /// <summary>Converts local GpuCollider array to the GPU manager's ColliderData format.</summary>
    LLuxBoneGPUManager.ColliderData[] ConvertColliders(GpuCollider[] gpuColliders)
    {
        var converted = new LLuxBoneGPUManager.ColliderData[gpuColliders.Length];
        for (int i = 0; i < gpuColliders.Length; i++)
        {
            converted[i] = new LLuxBoneGPUManager.ColliderData
            {
                center = gpuColliders[i].center,
                radius = gpuColliders[i].radius,
                type   = gpuColliders[i].type,
                bound  = gpuColliders[i].bound,
                pad    = gpuColliders[i].pad
            };
        }
        return converted;
    }

    /// <summary>Sets the simulation weight (0 = frozen, 1 = full simulation).</summary>
    public void SetWeight(float w)
    {
        if (m_Weight == w) return;
        if (w == 0) InitTransforms();
        else if (m_Weight == 0) ResetParticlesPosition();
        m_Weight = w;
    }

    /// <summary>Returns the current simulation weight.</summary>
    public float GetWeight() => m_Weight;
}
