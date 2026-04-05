using UnityEngine;
using System.Collections.Generic;

/// <summary>
/// GPU-accelerated advanced bone simulation (LLuxGPUBoneAdvanced).
/// Main per-bone CPU-side simulation component. Traverses the bone hierarchy building particles,
/// runs Verlet integration, applies colliders and wind, and writes results back to transforms.
/// </summary>
[AddComponentMenu("Lux GPU Bone/LLux GPU Bone Advanced")]
public class LLuxGPUBoneAdvanced : MonoBehaviour
{
    // ─────────────────────────────────────────────────────────────────────────
    // Enums
    // ─────────────────────────────────────────────────────────────────────────

    public enum FreezeAxis { None, X, Y, Z }
    public enum PhysicsPreset { Custom, Cloth, ThickCloth, Swishy, Ear, FurryEar, Tail, Metal, Toon }
    public enum WindMode { None, Static, PlayerVelocity, LocalZones }
    public enum BonePersonality { None, FollowPhysicsPresetRecommendations, Leader, Follower, Repel, Attract }

    // ─────────────────────────────────────────────────────────────────────────
    // Root
    // ─────────────────────────────────────────────────────────────────────────

    [Header("Root")]
    [Tooltip("Root bone transform. Auto-assigns to this GameObject if left null.")]
    public Transform m_Root = null;

    // ─────────────────────────────────────────────────────────────────────────
    // Physics Preset
    // ─────────────────────────────────────────────────────────────────────────

    [Header("Physics Preset")]
    public PhysicsPreset m_Preset = PhysicsPreset.Custom;

    // ─────────────────────────────────────────────────────────────────────────
    // Simulation
    // ─────────────────────────────────────────────────────────────────────────

    [Header("Simulation")]
    public float m_UpdateRate = 60f;
    [Range(0, 1)] public float m_Damping = 0.1f;
    public AnimationCurve m_DampingDistrib = null;
    [Range(0, 1)] public float m_Elasticity = 0.1f;
    public AnimationCurve m_ElasticityDistrib = null;
    [Range(0, 1)] public float m_Stiffness = 0.1f;
    public AnimationCurve m_StiffnessDistrib = null;
    [Range(0, 1)] public float m_Inert = 0f;
    public AnimationCurve m_InertDistrib = null;
    public float m_Radius = 0f;
    public AnimationCurve m_RadiusDistrib = null;

    // ─────────────────────────────────────────────────────────────────────────
    // End Bone
    // ─────────────────────────────────────────────────────────────────────────

    [Header("End Bone")]
    public float m_EndLength = 0f;
    public Vector3 m_EndOffset = Vector3.zero;

    // ─────────────────────────────────────────────────────────────────────────
    // Forces
    // ─────────────────────────────────────────────────────────────────────────

    [Header("Forces")]
    public Vector3 m_Gravity = Vector3.zero;
    public Vector3 m_Force = Vector3.zero;

    // ─────────────────────────────────────────────────────────────────────────
    // Built-In Wind
    // ─────────────────────────────────────────────────────────────────────────

    [Header("Wind (Built-In)")]
    [Tooltip("Built-in wind. Overridden automatically by any LLuxGPUBoneWindCollider zone.")]
    public WindMode m_WindMode = WindMode.None;
    public float m_WindStrength = 1f;
    public AnimationCurve m_WindStrengthDistrib = null;
    public float m_WindFrequency = 0.8f;
    public AnimationCurve m_WindFrequencyDistrib = null;
    public float m_WindTurbulence = 0.3f;
    public AnimationCurve m_WindTurbulenceDistrib = null;
    public Vector3 m_WindDirection = new Vector3(1, 0, 0);
    [Tooltip("Reference Transform for PlayerVelocity wind (e.g. character root).")]
    public Transform m_PlayerTransform = null;

    private Vector3 m_PrevPlayerPos = Vector3.zero;
    private Vector3 m_PlayerVelocity = Vector3.zero;

    // ─────────────────────────────────────────────────────────────────────────
    // Bone Personality
    // ─────────────────────────────────────────────────────────────────────────

    [Header("Bone Personality")]
    public BonePersonality m_Personality = BonePersonality.None;
    public string m_PersonalityGroup = "";
    [Range(0, 5)] public float m_PersonalityStrength = 1f;
    [Range(0, 5)] public float m_PersonalityRadius = 0.5f;

    private static readonly List<LLuxGPUBoneAdvanced> s_AllBones = new List<LLuxGPUBoneAdvanced>();

    // ─────────────────────────────────────────────────────────────────────────
    // Predictive Stabilization
    // ─────────────────────────────────────────────────────────────────────────

    [Header("Predictive Stabilization")]
    public bool m_PredictiveStabilization = true;
    [Range(0, 1)] public float m_StabilizationStrength = 0.4f;
    [Range(1, 20)] public int m_StabilizationFrames = 4;

    private readonly Queue<Vector3> m_VelocityHistory = new Queue<Vector3>();

    // ─────────────────────────────────────────────────────────────────────────
    // Character Colliders
    // ─────────────────────────────────────────────────────────────────────────

    [Header("Character Colliders")]
    [Tooltip("Auto-scans this character's hierarchy for LLuxGPUBoneCollider on Start.")]
    public bool m_AutoCollect = true;
    public List<LLuxGPUBoneCollider> m_Colliders = null;
    public List<Transform> m_Exclusions = null;

    // ─────────────────────────────────────────────────────────────────────────
    // External Collisions
    // ─────────────────────────────────────────────────────────────────────────

    [Header("External Collisions")]
    [Tooltip("Allow other characters' LuxBoneInteractor bones to push these particles.")]
    public bool m_EnableExternalCollisions = false;
    [Tooltip("World-space search radius for nearby interactors.")]
    public float m_ExternalCollisionRange = 2f;

    // m_NearbyInteractors stays typed as LuxBoneInteractor — uses legacy LuxBoneRegistry bridge
    private readonly List<LuxBoneInteractor> m_NearbyInteractors = new List<LuxBoneInteractor>();

    // ─────────────────────────────────────────────────────────────────────────
    // Constraints
    // ─────────────────────────────────────────────────────────────────────────

    [Header("Constraints")]
    public FreezeAxis m_FreezeAxis = FreezeAxis.None;

    // ─────────────────────────────────────────────────────────────────────────
    // Distance Disable
    // ─────────────────────────────────────────────────────────────────────────

    [Header("Distance Disable")]
    public bool m_DistantDisable = false;
    public Transform m_ReferenceObject = null;
    public float m_DistanceToObject = 20f;

    // ─────────────────────────────────────────────────────────────────────────
    // GPU Collections
    // ─────────────────────────────────────────────────────────────────────────

    [Header("GPU Collections")]
    public List<LLuxGPUBoneAdvanced> m_ChainBones = new List<LLuxGPUBoneAdvanced>();
    public List<LLuxGPUBoneCollider> m_GPUColliders = new List<LLuxGPUBoneCollider>();

    // ─────────────────────────────────────────────────────────────────────────
    // Internal
    // ─────────────────────────────────────────────────────────────────────────

    private Vector3 m_LocalGravity = Vector3.zero;
    private Vector3 m_ObjectMove = Vector3.zero;
    private Vector3 m_ObjectPrevPosition = Vector3.zero;
    private float m_BoneTotalLength = 0f;
    private float m_ObjectScale = 1f;
    private float m_Time = 0f;
    private float m_Weight = 1f;
    private bool m_DistantDisabled = false;

    private LuxWindCollider m_ActiveWindZone = null;

    // ─────────────────────────────────────────────────────────────────────────
    // Particle Structure
    // ─────────────────────────────────────────────────────────────────────────

    class Particle
    {
        public Transform m_Transform = null;
        public int m_ParentIndex = -1;
        public float m_Damping = 0;
        public float m_Elasticity = 0;
        public float m_Stiffness = 0;
        public float m_Inert = 0;
        public float m_Radius = 0;
        public float m_WindStrength = 1;
        public float m_WindFrequency = 1;
        public float m_WindTurbulence = 1;
        public float m_BoneLength = 0;
        public Vector3 m_Position = Vector3.zero;
        public Vector3 m_PrevPosition = Vector3.zero;
        public Vector3 m_EndOffset = Vector3.zero;
        public Vector3 m_InitLocalPosition = Vector3.zero;
        public Quaternion m_InitLocalRotation = Quaternion.identity;
    }

    private readonly List<Particle> m_Particles = new List<Particle>();

    // ─────────────────────────────────────────────────────────────────────────
    // Preset Table
    // ─────────────────────────────────────────────────────────────────────────

    private static readonly Dictionary<PhysicsPreset, float[]> k_Presets =
        new Dictionary<PhysicsPreset, float[]>
        {
            { PhysicsPreset.Cloth,      new[] { 0.25f, 0.05f, 0.40f, 0.05f } },
            { PhysicsPreset.ThickCloth, new[] { 0.35f, 0.02f, 0.65f, 0.02f } },
            { PhysicsPreset.Swishy,     new[] { 0.10f, 0.12f, 0.05f, 0.15f } },
            { PhysicsPreset.Ear,        new[] { 0.15f, 0.15f, 0.35f, 0.05f } },
            { PhysicsPreset.FurryEar,   new[] { 0.20f, 0.25f, 0.20f, 0.10f } },
            { PhysicsPreset.Tail,       new[] { 0.10f, 0.08f, 0.15f, 0.25f } },
            { PhysicsPreset.Metal,      new[] { 0.05f, 0.00f, 0.98f, 0.00f } },
            { PhysicsPreset.Toon,       new[] { 0.15f, 0.35f, 0.05f, 0.05f } },
        };

    // ─────────────────────────────────────────────────────────────────────────
    // Unity Lifecycle
    // ─────────────────────────────────────────────────────────────────────────

    void Start()
    {
        if (m_Root == null)
            m_Root = transform;

        ApplyPreset();
        ApplyPersonalityPreset();
        SetupParticles();
        s_AllBones.Add(this);

        m_ObjectPrevPosition = transform.position;
        m_Time = 0f;
    }

    void Update()
    {
        if (!Application.isPlaying)
        {
            ApplyPreset();
            ApplyPersonalityPreset();
        }

        if (m_Weight > 0 && !(m_DistantDisable && m_DistantDisabled))
            InitTransforms();
    }

    void ApplyPersonalityPreset()
    {
        if (m_Personality != BonePersonality.FollowPhysicsPresetRecommendations) return;

        LLuxGPUBoneAdvanced chainRoot = this;
        Transform current = transform.parent;
        while (current != null)
        {
            LLuxGPUBoneAdvanced pBone = current.GetComponent<LLuxGPUBoneAdvanced>();
            if (pBone != null)
            {
                chainRoot = pBone;
            }
            else
            {
                break;
            }
            current = current.parent;
        }

        m_PersonalityGroup = chainRoot.name + "_Group";

        switch (m_Preset)
        {
            case PhysicsPreset.Cloth:
            case PhysicsPreset.ThickCloth:
                m_Personality = BonePersonality.Repel;
                m_PersonalityRadius = 0.2f;
                m_PersonalityStrength = 0.5f;
                break;
            case PhysicsPreset.Swishy:
                m_Personality = BonePersonality.Attract;
                m_PersonalityRadius = 0.4f;
                m_PersonalityStrength = 0.8f;
                break;
            case PhysicsPreset.Tail:
                m_Personality = BonePersonality.Leader;
                m_PersonalityRadius = 0.5f;
                m_PersonalityStrength = 1.0f;
                break;
            case PhysicsPreset.FurryEar:
            case PhysicsPreset.Ear:
                m_Personality = BonePersonality.Repel;
                m_PersonalityRadius = 0.1f;
                m_PersonalityStrength = 0.2f;
                break;
            case PhysicsPreset.Toon:
                m_Personality = BonePersonality.None;
                m_PersonalityRadius = 0.6f;
                m_PersonalityStrength = 2.0f;
                break;
            default:
                m_Personality = BonePersonality.None;
                break;
        }
    }

    public Vector3 m_Position { get; private set; }

    void LateUpdate()
    {
        if (m_DistantDisable)
            CheckDistance();

        if (m_Weight > 0 && !(m_DistantDisable && m_DistantDisabled))
        {
            UpdateDynamicBones(Time.deltaTime);
            if (m_Particles.Count > 0)
                m_Position = m_Particles[0].m_Position;
        }
    }

    void OnEnable()
    {
        ResetParticlesPosition();
    }

    void OnDisable()
    {
        InitTransforms();
    }

    void OnDestroy()
    {
        s_AllBones.Remove(this);
    }

    void OnValidate()
    {
        m_UpdateRate = Mathf.Max(m_UpdateRate, 0);
        m_Damping = Mathf.Clamp01(m_Damping);
        m_Elasticity = Mathf.Clamp01(m_Elasticity);
        m_Stiffness = Mathf.Clamp01(m_Stiffness);
        m_Inert = Mathf.Clamp01(m_Inert);
        m_Radius = Mathf.Max(m_Radius, 0);

        ApplyPreset();
        ApplyPersonalityPreset();

        if (Application.isEditor && Application.isPlaying)
        {
            InitTransforms();
            SetupParticles();
        }
    }

    void OnDrawGizmosSelected()
    {
        if (!enabled || m_Root == null) return;
        if (Application.isEditor && !Application.isPlaying && transform.hasChanged)
        {
            InitTransforms();
            SetupParticles();
        }

        Gizmos.color = new Color(0.6f, 1f, 0.6f, 0.9f);
        for (int i = 0; i < m_Particles.Count; ++i)
        {
            Particle p = m_Particles[i];
            if (p.m_ParentIndex >= 0)
                Gizmos.DrawLine(p.m_Position, m_Particles[p.m_ParentIndex].m_Position);
            if (p.m_Radius > 0)
                Gizmos.DrawWireSphere(p.m_Position, p.m_Radius * m_ObjectScale);
        }
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Public API
    // ─────────────────────────────────────────────────────────────────────────

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

    // ─────────────────────────────────────────────────────────────────────────
    // Distance Check
    // ─────────────────────────────────────────────────────────────────────────

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

    // ─────────────────────────────────────────────────────────────────────────
    // Preset Application
    // ─────────────────────────────────────────────────────────────────────────

    void ApplyPreset()
    {
        if (m_Preset == PhysicsPreset.Custom) return;
        if (k_Presets.TryGetValue(m_Preset, out var values))
        {
            m_Damping = values[0];
            m_Elasticity = values[1];
            m_Stiffness = values[2];
            m_Inert = values[3];
        }
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Particle Setup
    // ─────────────────────────────────────────────────────────────────────────

    void SetupParticles()
    {
        m_Particles.Clear();
        AppendParticles(m_Root, -1, 0f);
        ResetParticlesPosition();
    }

    void AppendParticles(Transform b, int parentIndex, float boneLength)
    {
        Particle p = new Particle
        {
            m_Transform = b,
            m_ParentIndex = parentIndex,
            m_Damping = m_Damping,
            m_Elasticity = m_Elasticity,
            m_Stiffness = m_Stiffness,
            m_Inert = m_InertDistrib != null ? m_InertDistrib.Evaluate(boneLength) * m_Inert : m_Inert,
            m_Radius = m_RadiusDistrib != null ? m_RadiusDistrib.Evaluate(boneLength) * m_Radius : m_Radius,
            m_WindStrength = m_WindStrengthDistrib != null ? m_WindStrengthDistrib.Evaluate(boneLength) * m_WindStrength : m_WindStrength,
            m_WindFrequency = m_WindFrequencyDistrib != null ? m_WindFrequencyDistrib.Evaluate(boneLength) * m_WindFrequency : m_WindFrequency,
            m_WindTurbulence = m_WindTurbulenceDistrib != null ? m_WindTurbulenceDistrib.Evaluate(boneLength) * m_WindTurbulence : m_WindTurbulence,
            m_Position = b != null ? b.position : Vector3.zero,
            m_PrevPosition = b != null ? b.position : Vector3.zero,
            m_InitLocalPosition = b != null ? b.localPosition : Vector3.zero,
            m_InitLocalRotation = b != null ? b.localRotation : Quaternion.identity,
        };

        int index = m_Particles.Count;
        m_Particles.Add(p);

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
        for (int i = 0; i < m_Particles.Count; ++i)
        {
            Particle p = m_Particles[i];
            if (p.m_Transform == null) continue;
            p.m_Transform.localPosition = p.m_InitLocalPosition;
            p.m_Transform.localRotation = p.m_InitLocalRotation;
        }
    }

    void ResetParticlesPosition()
    {
        for (int i = 0; i < m_Particles.Count; ++i)
        {
            Particle p = m_Particles[i];
            if (p.m_Transform != null)
            {
                p.m_Position = p.m_PrevPosition = p.m_Transform.position;
            }
            else if (p.m_ParentIndex >= 0)
            {
                Transform pb = m_Particles[p.m_ParentIndex].m_Transform;
                p.m_Position = p.m_PrevPosition = pb.TransformPoint(p.m_EndOffset);
            }
        }
        m_ObjectPrevPosition = transform.position;
        m_VelocityHistory.Clear();
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Simulation
    // ─────────────────────────────────────────────────────────────────────────

    void UpdateDynamicBones(float deltaTime)
    {
        if (m_UpdateRate <= 0) return;

        float fixedDeltaTime = 1f / m_UpdateRate;
        m_ObjectScale = Mathf.Abs(transform.lossyScale.x);

        Vector3 move = transform.position - m_ObjectPrevPosition;
        m_ObjectPrevPosition = transform.position;
        m_ObjectMove = move;

        if (m_EnableExternalCollisions)
        {
            LuxBoneRegistry.GetNearbyInteractors(gameObject, m_Root.position, m_ExternalCollisionRange, m_NearbyInteractors);
        }

        m_LocalGravity = m_Root.TransformDirection(m_Gravity);
        m_Time += deltaTime;

        if (m_Time > fixedDeltaTime * 5) m_Time = fixedDeltaTime * 5;

        while (m_Time >= fixedDeltaTime)
        {
            UpdateParticles1(fixedDeltaTime);
            UpdateParticles2();
            m_Time -= fixedDeltaTime;
        }

        ApplyParticlesToTransforms();
    }

    void UpdateParticles1(float fixedDeltaTime)
    {
        Vector3 gravityForce = m_Gravity;
        Vector3 fdir = m_Gravity.normalized;
        Vector3 rf = m_Root.TransformDirection(m_LocalGravity);
        Vector3 pf = fdir * Mathf.Max(Vector3.Dot(rf, fdir), 0);
        gravityForce -= pf;
        gravityForce = (gravityForce + m_Force) * m_ObjectScale;

        if (m_WindMode == WindMode.PlayerVelocity && m_PlayerTransform != null)
        {
            m_PlayerVelocity = (m_PlayerTransform.position - m_PrevPlayerPos) / fixedDeltaTime;
            m_PrevPlayerPos = m_PlayerTransform.position;
        }

        for (int i = 0; i < m_Particles.Count; ++i)
        {
            Particle p = m_Particles[i];
            if (p.m_ParentIndex >= 0)
            {
                Vector3 v = p.m_Position - p.m_PrevPosition;
                Vector3 rmove = m_ObjectMove * p.m_Inert;
                p.m_PrevPosition = p.m_Position + rmove;
                Vector3 windForce = GetWindForce(p);
                p.m_Position += v * (1 - p.m_Damping) + (gravityForce + windForce * 15f) * fixedDeltaTime + rmove + GetPersonalityForce(p.m_Position);
            }
            else
            {
                p.m_PrevPosition = p.m_Position;
                p.m_Position = p.m_Transform.position;
            }
        }
    }

    void UpdateParticles2()
    {
        Plane movePlane = new Plane();

        for (int i = 1; i < m_Particles.Count; ++i)
        {
            Particle p = m_Particles[i];
            Particle p0 = m_Particles[p.m_ParentIndex];

            float restLen = p.m_Transform != null
                ? (p0.m_Transform.position - p.m_Transform.position).magnitude
                : p0.m_Transform.localToWorldMatrix.MultiplyVector(p.m_EndOffset).magnitude;

            float stiffness = Mathf.Lerp(1f, p.m_Stiffness, m_Weight);
            if (stiffness > 0 || p.m_Elasticity > 0)
            {
                Matrix4x4 m0 = p0.m_Transform.localToWorldMatrix;
                m0.SetColumn(3, p0.m_Position);
                Vector3 restPos = p.m_Transform != null
                    ? m0.MultiplyPoint3x4(p.m_Transform.localPosition)
                    : m0.MultiplyPoint3x4(p.m_EndOffset);
                Vector3 d = restPos - p.m_Position;
                p.m_Position += d * p.m_Elasticity;
                if (stiffness > 0)
                {
                    d = restPos - p.m_Position;
                    float len = d.magnitude;
                    float maxlen = restLen * (1 - stiffness) * 2;
                    if (len > maxlen) p.m_Position += d * ((len - maxlen) / len);
                }
            }

            float pr = p.m_Radius * m_ObjectScale;

            // Character colliders (L-prefixed)
            if (m_Colliders != null)
                for (int j = 0; j < m_Colliders.Count; ++j)
                {
                    var c = m_Colliders[j];
                    if (c != null && c.enabled) c.Collide(ref p.m_Position, pr);
                }

            // World colliders (new L-prefixed system)
            var v2Worlds = LLuxGPUBoneRegistry.WorldColliders;
            for (int j = 0; j < v2Worlds.Count; ++j)
            {
                var c = v2Worlds[j];
                if (c != null && c.enabled) c.Collide(ref p.m_Position, pr);
            }

            // World colliders (legacy system compatibility)
            var legacyWorlds = LuxBoneRegistry.WorldColliders;
            for (int j = 0; j < legacyWorlds.Count; ++j)
            {
                var c = legacyWorlds[j];
                if (c != null && c.enabled) c.Collide(ref p.m_Position, pr);
            }

            // External interactors (legacy bridge stays unchanged)
            if (m_EnableExternalCollisions)
                for (int j = 0; j < m_NearbyInteractors.Count; ++j)
                {
                    var c = m_NearbyInteractors[j];
                    if (c != null && c.enabled) c.Collide(ref p.m_Position, pr, gameObject);
                }

            if (m_FreezeAxis != FreezeAxis.None)
            {
                switch (m_FreezeAxis)
                {
                    case FreezeAxis.X:
                        movePlane.SetNormalAndPosition(p0.m_Transform.right, p0.m_Position);
                        break;
                    case FreezeAxis.Y:
                        movePlane.SetNormalAndPosition(p0.m_Transform.up, p0.m_Position);
                        break;
                    case FreezeAxis.Z:
                        movePlane.SetNormalAndPosition(p0.m_Transform.forward, p0.m_Position);
                        break;
                }
                p.m_Position -= movePlane.normal * movePlane.GetDistanceToPoint(p.m_Position);
            }

            Vector3 dd = p0.m_Position - p.m_Position;
            float leng = dd.magnitude;
            if (leng > 0) p.m_Position += dd * ((leng - restLen) / leng);
        }
    }

    void ApplyParticlesToTransforms()
    {
        for (int i = 1; i < m_Particles.Count; ++i)
        {
            Particle p = m_Particles[i];
            Particle p0 = m_Particles[p.m_ParentIndex];

            if (p0.m_Transform.childCount <= 1)
            {
                Vector3 v = p.m_Transform != null ? p.m_Transform.localPosition : p.m_EndOffset;
                Quaternion rot = Quaternion.FromToRotation(
                    p0.m_Transform.TransformDirection(v), p.m_Position - p0.m_Position);
                p0.m_Transform.rotation = rot * p0.m_Transform.rotation;
            }

            if (p.m_Transform != null) p.m_Transform.position = p.m_Position;
        }
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Force Calculations
    // ─────────────────────────────────────────────────────────────────────────

    Vector3 GetWindForce(Particle p)
    {
        Vector3 totalWind = Vector3.zero;

        // Local GPU zones (L-prefixed registry)
        var zones = LLuxGPUBoneRegistry.WindColliders;
        for (int i = 0; i < zones.Count; i++)
        {
            var z = zones[i];
            if (z == null || !z.enabled) continue;

            float dist = Vector3.Distance(p.m_Position, z.transform.position);
            if (dist < z.m_Radius)
            {
                float t = Time.time * z.m_Frequency;
                float noise = (Mathf.Sin(t) * 0.5f + Mathf.Cos(t * 2.1f) * 0.3f) * 0.3f;
                float strength = (1f - dist / z.m_Radius) * z.m_Strength * p.m_WindStrength * (1f + noise);
                totalWind += z.transform.TransformDirection(z.m_Direction).normalized * strength;
            }
        }

        // Global wind zones (legacy support)
        m_ActiveWindZone = LuxBoneRegistry.GetDominantWindZone(p.m_Position);
        if (m_ActiveWindZone != null)
        {
            totalWind += m_ActiveWindZone.EvaluateWindForce(p.m_Position, m_ObjectScale) * p.m_WindStrength;
        }

        if (m_WindMode == WindMode.PlayerVelocity && m_PlayerTransform != null)
        {
            totalWind += m_PlayerVelocity * 0.1f * p.m_WindStrength;
        }
        else if (m_WindMode == WindMode.Static)
        {
            float t_static = Time.time * m_WindFrequency * p.m_WindFrequency;
            float noise_static = (Mathf.Sin(t_static) * 0.5f + Mathf.Cos(t_static * 2.1f) * m_WindTurbulence * p.m_WindTurbulence) * m_WindTurbulence * p.m_WindTurbulence;
            totalWind += m_WindDirection.normalized * m_WindStrength * p.m_WindStrength * (1f + noise_static) * m_ObjectScale;
        }

        return totalWind * m_ObjectScale;
    }

    Vector3 GetPersonalityForce(Vector3 position)
    {
        if (m_Personality == BonePersonality.None) return Vector3.zero;

        Vector3 force = Vector3.zero;
        foreach (var bone in s_AllBones)
        {
            if (bone == this || bone.m_Personality == BonePersonality.None) continue;
            if (!string.IsNullOrEmpty(m_PersonalityGroup) && bone.m_PersonalityGroup != m_PersonalityGroup)
                continue;

            float dist = Vector3.Distance(position, bone.transform.position);
            if (dist > m_PersonalityRadius) continue;

            Vector3 dir = (position - bone.transform.position).normalized;
            float strength = (1f - dist / m_PersonalityRadius) * m_PersonalityStrength;

            if (m_Personality == BonePersonality.Attract)
                force -= dir * strength;
            else if (m_Personality == BonePersonality.Repel)
                force += dir * strength;
            else if (m_Personality == BonePersonality.Leader && bone.m_Personality == BonePersonality.Follower)
                force += (bone.m_Position - position).normalized * strength;
            else if (m_Personality == BonePersonality.Follower && bone.m_Personality == BonePersonality.Leader)
                force += (bone.m_Position - position).normalized * strength;
        }

        return force;
    }
}
