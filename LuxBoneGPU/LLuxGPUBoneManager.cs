using UnityEngine;
using System.Collections.Generic;

/// <summary>
/// GPU compute buffer manager for the LLuxGPUBone system.
/// Manages all GPU simulation buffers and dispatches the three simulation kernels each frame.
/// </summary>
public class LLuxGPUBoneManager : MonoBehaviour
{
    public struct GPUParticle
    {
        public Vector3 position;
        public Vector3 prevPosition;
        public Vector3 restLocalPosition;
        public float damping;
        public float elasticity;
        public float stiffness;
        public float inert;
        public float radius;
        public float boneLength;
        public int parentIndex;
        public int transformIndex;
        public int padding;
    }

    public struct GPUCollider
    {
        public Vector3 center;
        public float radius;
        public Vector3 normal;
        public int type;
    }

    public struct GPUWorldCollider
    {
        public Vector3 center;
        public Vector3 scale;
        public Vector3 normal;
        public int type;
        public int padding;
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Singleton
    // ─────────────────────────────────────────────────────────────────────────

    private static LLuxGPUBoneManager s_Instance;

    public static LLuxGPUBoneManager Instance
    {
        get
        {
            if (s_Instance == null)
            {
                s_Instance = FindObjectOfType<LLuxGPUBoneManager>();
                if (s_Instance == null)
                {
                    GameObject go = new GameObject("LLuxGPUBoneManager");
                    s_Instance = go.AddComponent<LLuxGPUBoneManager>();
                }
            }
            return s_Instance;
        }
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Fields
    // ─────────────────────────────────────────────────────────────────────────

    [SerializeField]
    private ComputeShader m_ComputeShader;

    private ComputeBuffer m_ParticleBuffer;
    private ComputeBuffer m_ColliderBuffer;
    private ComputeBuffer m_WorldColliderBuffer;
    private ComputeBuffer m_SimParamsBuffer;

    private int m_UpdateParticles1Kernel;
    private int m_UpdateParticles2Kernel;
    private int m_ApplyCollisionsKernel;

    private bool m_Initialized = false;
    private const int MAX_PARTICLES = 10000;
    private const int MAX_COLLIDERS = 500;
    private const int MAX_WORLD_COLLIDERS = 100;

    // ─────────────────────────────────────────────────────────────────────────
    // Lifecycle
    // ─────────────────────────────────────────────────────────────────────────

    void OnEnable()
    {
        if (s_Instance == null)
            s_Instance = this;
    }

    private LLuxGPUBoneManager.GPUWorldCollider[] m_CachedWorldColliders = new LLuxGPUBoneManager.GPUWorldCollider[MAX_WORLD_COLLIDERS];
    private int m_WorldCount = 0;

    void Update()
    {
        m_WorldCount = 0;

        // Collect from new L-prefixed V2 Registry
        var worldsV2 = LLuxGPUBoneRegistry.WorldColliders;
        for (int i = 0; i < worldsV2.Count && m_WorldCount < MAX_WORLD_COLLIDERS; i++)
        {
            var w = worldsV2[i];
            if (w == null || !w.enabled) continue;

            m_CachedWorldColliders[m_WorldCount] = new LLuxGPUBoneManager.GPUWorldCollider
            {
                center = w.transform.TransformPoint(w.m_Center),
                scale = w.m_Shape == LLuxGPUBoneWorldCollider.Shape.Box ? w.m_Size : new Vector3(w.m_Size.x, 0, 0),
                normal = w.transform.TransformDirection(w.m_Normal),
                type = (int)w.m_Shape
            };
            m_WorldCount++;
        }

        // Collect from legacy Registry for compatibility
        var worldsLegacy = LuxBoneRegistry.WorldColliders;
        for (int i = 0; i < worldsLegacy.Count && m_WorldCount < MAX_WORLD_COLLIDERS; i++)
        {
            var w = worldsLegacy[i];
            if (w == null || !w.enabled) continue;

            m_CachedWorldColliders[m_WorldCount] = new LLuxGPUBoneManager.GPUWorldCollider
            {
                center = w.transform.TransformPoint(w.m_Center),
                scale = w.m_Shape == LuxWorldCollider.WorldColliderShape.Box ? w.m_BoxSize : new Vector3(w.m_PlaneSize.x, w.m_PlaneSize.y, 0),
                normal = w.transform.TransformDirection(Vector3.up),
                type = (int)w.m_Shape
            };
            m_WorldCount++;
        }
    }

    /// <summary>
    /// Dispatches the three simulation kernels for a bone chain's particle set.
    /// </summary>
    public void SimulateStep(
        Vector3 gravity,
        Vector3 force,
        Vector3 objectMove,
        float deltaTime,
        float objectScale,
        float weight,
        GPUParticle[] particles,
        int particleCount,
        GPUCollider[] colliders,
        int colliderCount,
        int freezeAxis)
    {
        if (!m_Initialized || m_ComputeShader == null)
            return;

        if (particleCount > 0 && particleCount <= MAX_PARTICLES)
            m_ParticleBuffer.SetData(particles, 0, 0, particleCount);

        if (colliderCount > 0 && colliderCount <= MAX_COLLIDERS)
            m_ColliderBuffer.SetData(colliders, 0, 0, colliderCount);

        if (m_WorldCount > 0)
            m_WorldColliderBuffer.SetData(m_CachedWorldColliders, 0, 0, m_WorldCount);

        m_ComputeShader.SetVector("gravity", gravity);
        m_ComputeShader.SetFloat("deltaTime", deltaTime);
        m_ComputeShader.SetVector("force", force);
        m_ComputeShader.SetFloat("objectScale", objectScale);
        m_ComputeShader.SetVector("objectMove", objectMove);
        m_ComputeShader.SetFloat("weight", weight);
        m_ComputeShader.SetInt("particleCount", particleCount);
        m_ComputeShader.SetInt("colliderCount", colliderCount);
        m_ComputeShader.SetInt("worldColliderCount", m_WorldCount);
        m_ComputeShader.SetInt("freezeAxis", freezeAxis);

        int threadGroups = Mathf.CeilToInt(particleCount / 256f);

        m_ComputeShader.SetBuffer(m_UpdateParticles1Kernel, "particles", m_ParticleBuffer);
        m_ComputeShader.Dispatch(m_UpdateParticles1Kernel, threadGroups, 1, 1);

        m_ComputeShader.SetBuffer(m_UpdateParticles2Kernel, "particles", m_ParticleBuffer);
        m_ComputeShader.Dispatch(m_UpdateParticles2Kernel, threadGroups, 1, 1);

        m_ComputeShader.SetBuffer(m_ApplyCollisionsKernel, "particles", m_ParticleBuffer);
        m_ComputeShader.SetBuffer(m_ApplyCollisionsKernel, "colliders", m_ColliderBuffer);
        m_ComputeShader.SetBuffer(m_ApplyCollisionsKernel, "worldColliders", m_WorldColliderBuffer);
        m_ComputeShader.Dispatch(m_ApplyCollisionsKernel, threadGroups, 1, 1);

        if (particleCount > 0 && particleCount <= MAX_PARTICLES)
            m_ParticleBuffer.GetData(particles, 0, 0, particleCount);
    }

    private void ReleaseBuffers()
    {
        m_ParticleBuffer?.Release();
        m_ColliderBuffer?.Release();
        m_WorldColliderBuffer?.Release();
        m_SimParamsBuffer?.Release();

        m_Initialized = false;
    }
}
