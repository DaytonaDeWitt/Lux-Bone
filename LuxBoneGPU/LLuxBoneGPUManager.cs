using UnityEngine;
using System.Collections.Generic;

/// <summary>
/// Full GPU buffer manager with RegisterSimulation/UnregisterSimulation API,
/// audio-reactive wind support, and a 4-kernel dispatch.
/// Separate from LLuxGPUBoneManager — both existed as separate scripts in the original.
/// </summary>
public class LLuxBoneGPUManager : MonoBehaviour
{
    // ── Singleton ─────────────────────────────────────────────────────────────
    private static LLuxBoneGPUManager s_Instance;

    public static LLuxBoneGPUManager Instance
    {
        get
        {
            if (s_Instance == null)
            {
                s_Instance = FindObjectOfType<LLuxBoneGPUManager>();
                if (s_Instance == null)
                {
                    GameObject go = new GameObject("LLuxBoneGPUManager");
                    s_Instance = go.AddComponent<LLuxBoneGPUManager>();
                }
            }
            return s_Instance;
        }
    }

    // ── Structures matching compute shader ─────────────────────────────────────
    public struct Particle
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

    public struct ColliderData
    {
        public Vector3 center;
        public float radius;
        public int type;
        public int bound;
        public Vector2 pad;
    }

    public struct WindZoneData
    {
        public Vector3 position;
        public float radius;
        public Vector3 windDirection;
        public float windStrength;
        public float windFrequency;
        public float windTurbulence;
        public float blendWeight;
        public int shapeType;
        public Vector3 boxSize;
        public float softEdgeInfluence;
        public int priority;
        public float pad;
    }

    struct AudioReactiveData
    {
        public float bassFrequency;
        public float midFrequency;
        public float trebleFrequency;
        public float overallAmplitude;
        public float audioInfluence;
    }

    // ── Fields ────────────────────────────────────────────────────────────────
    [SerializeField]
    private ComputeShader m_ComputeShader;

    private ComputeBuffer m_ParticleBuffer;
    private ComputeBuffer m_ColliderBuffer;
    private ComputeBuffer m_WindZoneBuffer;
    private ComputeBuffer m_AudioDataBuffer;
    private ComputeBuffer m_ConstraintBuffer;

    private List<LLuxBoneGPUSimulation> m_ActiveSimulations = new List<LLuxBoneGPUSimulation>();

    private int m_UpdateParticles1Kernel;
    private int m_UpdateParticles2Kernel;
    private int m_ApplyCollisionsKernel;
    private int m_AudioReactiveKernel;

    private bool m_Initialized = false;
    private int m_MaxParticles = 10000;
    private int m_MaxColliders = 1000;
    private int m_MaxWindZones = 100;

    // ── Properties ────────────────────────────────────────────────────────────
    public int MaxParticles => m_MaxParticles;
    public int MaxColliders => m_MaxColliders;
    public int MaxWindZones => m_MaxWindZones;

    // ── Lifecycle ─────────────────────────────────────────────────────────────
    void OnEnable()
    {
        if (s_Instance == null) s_Instance = this;
    }

    void OnDisable()
    {
        ReleaseBuffers();
    }

    /// <summary>Initialize GPU buffers and kernels. Called once at startup.</summary>
    public void Initialize(ComputeShader computeShader)
    {
        if (m_Initialized) return;

        m_ComputeShader = computeShader;
        if (m_ComputeShader == null)
        {
            Debug.LogError("LLuxBoneGPUManager: No compute shader assigned!");
            return;
        }

        m_UpdateParticles1Kernel = m_ComputeShader.FindKernel("UpdateParticles1");
        m_UpdateParticles2Kernel = m_ComputeShader.FindKernel("UpdateParticles2");
        m_ApplyCollisionsKernel = m_ComputeShader.FindKernel("ApplyCollisions");
        m_AudioReactiveKernel = m_ComputeShader.FindKernel("AudioReactive");

        m_ParticleBuffer = new ComputeBuffer(m_MaxParticles, sizeof(float) * 16 + sizeof(int) * 2 + sizeof(float) * 2);
        m_ColliderBuffer = new ComputeBuffer(m_MaxColliders, sizeof(float) * 3 + sizeof(float) + sizeof(int) * 2 + sizeof(float) * 2);
        m_WindZoneBuffer = new ComputeBuffer(m_MaxWindZones, sizeof(float) * 3 + sizeof(float) + sizeof(float) * 3 + sizeof(float) * 4 + sizeof(int) * 2 + sizeof(float));
        m_AudioDataBuffer = new ComputeBuffer(1, sizeof(float) * 5);
        m_ConstraintBuffer = new ComputeBuffer(m_MaxParticles, sizeof(float) * 4);

        m_Initialized = true;
    }

    /// <summary>Register a bone simulation to be processed by the GPU.</summary>
    public void RegisterSimulation(LLuxBoneGPUSimulation simulation)
    {
        if (!m_ActiveSimulations.Contains(simulation))
            m_ActiveSimulations.Add(simulation);
    }

    /// <summary>Unregister a bone simulation.</summary>
    public void UnregisterSimulation(LLuxBoneGPUSimulation simulation)
    {
        m_ActiveSimulations.Remove(simulation);
    }

    /// <summary>Execute a full simulation step on the GPU.</summary>
    public void SimulateStep(
        Vector3 gravity, Vector3 force, Vector3 objectMove,
        float deltaTime, float objectScale, float weight,
        Particle[] particles, int particleCount,
        ColliderData[] colliders, int colliderCount,
        WindZoneData[] windZones, int windZoneCount,
        Vector4[] constraints, int freezeAxis)
    {
        if (!m_Initialized || m_ComputeShader == null)
            return;

        if (particleCount > 0)
        {
            m_ParticleBuffer.SetData(particles, 0, 0, particleCount);
            m_ConstraintBuffer.SetData(constraints, 0, 0, particleCount);
        }

        if (colliderCount > 0)
            m_ColliderBuffer.SetData(colliders, 0, 0, colliderCount);

        if (windZoneCount > 0)
            m_WindZoneBuffer.SetData(windZones, 0, 0, windZoneCount);

        ComputeBuffer simulationParams = new ComputeBuffer(1, sizeof(float) * 3 + sizeof(float) + sizeof(float) * 3 + sizeof(float) + sizeof(float) * 3 + sizeof(float) + sizeof(int) * 4);
        Vector4[] simParams = new Vector4[4];
        simParams[0] = new Vector4(gravity.x, gravity.y, gravity.z, deltaTime);
        simParams[1] = new Vector4(force.x, force.y, force.z, objectScale);
        simParams[2] = new Vector4(objectMove.x, objectMove.y, objectMove.z, weight);
        simParams[3] = new Vector4(particleCount, colliderCount, windZoneCount, freezeAxis);
        simulationParams.SetData(simParams);

        m_ComputeShader.SetBuffer(m_UpdateParticles1Kernel, "particles", m_ParticleBuffer);
        m_ComputeShader.SetBuffer(m_UpdateParticles1Kernel, "windZones", m_WindZoneBuffer);

        int simParamsByteSize = sizeof(float) * 3 + sizeof(float) + sizeof(float) * 3 + sizeof(float) + sizeof(float) * 3 + sizeof(float) + sizeof(int);
        m_ComputeShader.SetConstantBuffer("SimulationParams", simulationParams, 0, simParamsByteSize);

        int threadGroups = Mathf.CeilToInt(particleCount / 256f);

        m_ComputeShader.Dispatch(m_UpdateParticles1Kernel, threadGroups, 1, 1);

        m_ComputeShader.SetBuffer(m_UpdateParticles2Kernel, "particles", m_ParticleBuffer);
        m_ComputeShader.SetBuffer(m_UpdateParticles2Kernel, "constraints", m_ConstraintBuffer);
        m_ComputeShader.Dispatch(m_UpdateParticles2Kernel, threadGroups, 1, 1);

        m_ComputeShader.SetBuffer(m_ApplyCollisionsKernel, "particles", m_ParticleBuffer);
        m_ComputeShader.SetBuffer(m_ApplyCollisionsKernel, "colliders", m_ColliderBuffer);
        m_ComputeShader.Dispatch(m_ApplyCollisionsKernel, threadGroups, 1, 1);

        m_ParticleBuffer.GetData(particles, 0, 0, particleCount);

        simulationParams.Release();
    }

    /// <summary>Apply audio-reactive modulation to wind zones.</summary>
    public void ApplyAudioReactivity(
        float bass, float mid, float treble, float amplitude,
        Vector4[] windZones, int windZoneCount)
    {
        if (!m_Initialized || windZoneCount <= 0)
            return;

        AudioReactiveData audioData = new AudioReactiveData
        {
            bassFrequency = bass,
            midFrequency = mid,
            trebleFrequency = treble,
            overallAmplitude = amplitude,
            audioInfluence = 1.0f
        };

        m_AudioDataBuffer.SetData(new AudioReactiveData[] { audioData });

        m_ComputeShader.SetBuffer(m_AudioReactiveKernel, "windZones", m_WindZoneBuffer);
        m_ComputeShader.SetBuffer(m_AudioReactiveKernel, "audioData", m_AudioDataBuffer);

        int threadGroups = Mathf.CeilToInt(windZoneCount / 256f);
        m_ComputeShader.Dispatch(m_AudioReactiveKernel, threadGroups, 1, 1);
    }

    private void ReleaseBuffers()
    {
        m_ParticleBuffer?.Release();
        m_ColliderBuffer?.Release();
        m_WindZoneBuffer?.Release();
        m_AudioDataBuffer?.Release();
        m_ConstraintBuffer?.Release();

        m_Initialized = false;
    }
}
