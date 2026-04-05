using UnityEngine;

public class LLuxGPUDeformer : MonoBehaviour
{
    public ComputeShader compute;
    public MeshFilter meshFilter;

    private ComputeBuffer particleBuffer;
    private ParticleCPU[] particlesCPU;

    private int kernel1;
    private int kernel2;
    private int kernelCollision;
    private int kernelAudio;

    [System.Serializable]
    struct ParticleCPU
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

    void Start()
    {
        kernel1 = compute.FindKernel("UpdateParticles1");
        kernel2 = compute.FindKernel("UpdateParticles2");
        kernelCollision = compute.FindKernel("ApplyCollisions");
        kernelAudio = compute.FindKernel("AudioReactive");

        Mesh mesh = meshFilter.mesh;
        int count = mesh.vertexCount;

        particlesCPU = new ParticleCPU[count];

        Vector3[] verts = mesh.vertices;

        for (int i = 0; i < count; i++)
        {
            particlesCPU[i] = new ParticleCPU
            {
                position = verts[i],
                prevPosition = verts[i],
                restPosition = verts[i],
                damping = 0.98f,
                elasticity = 0.2f,
                stiffness = 0.5f,
                inert = 1f,
                radius = 0.01f,
                parentIndex = i == 0 ? -1 : i - 1,
                transformIndex = 0
            };
        }

        int stride = System.Runtime.InteropServices.Marshal.SizeOf(typeof(ParticleCPU));
        particleBuffer = new ComputeBuffer(count, stride);
        particleBuffer.SetData(particlesCPU);

        compute.SetBuffer(kernel1, "particles", particleBuffer);
        compute.SetBuffer(kernel2, "particles", particleBuffer);
        compute.SetBuffer(kernelCollision, "particles", particleBuffer);

        compute.SetInt("particleCount", count);
    }

    void Update()
    {
        int threadGroups = Mathf.CeilToInt(particlesCPU.Length / 256f);

        compute.Dispatch(kernel1, threadGroups, 1, 1);
        compute.Dispatch(kernel2, threadGroups, 1, 1);
        compute.Dispatch(kernelCollision, threadGroups, 1, 1);
        compute.Dispatch(kernelAudio, Mathf.CeilToInt(1), 1, 1);

        particleBuffer.GetData(particlesCPU);

        Mesh mesh = meshFilter.mesh;
        Vector3[] vertices = mesh.vertices;

        for (int i = 0; i < vertices.Length; i++)
        {
            vertices[i] = particlesCPU[i].position;
        }

        mesh.vertices = vertices;
        mesh.RecalculateBounds();
    }

    void OnDestroy()
    {
        if (particleBuffer != null)
            particleBuffer.Release();
    }
}
