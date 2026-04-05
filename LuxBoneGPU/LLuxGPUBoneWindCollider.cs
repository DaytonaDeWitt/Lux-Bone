using UnityEngine;

[AddComponentMenu("Lux GPU Bone/LLux GPU Bone Wind Collider")]
public class LLuxGPUBoneWindCollider : MonoBehaviour
{
    public float m_Strength = 1.0f;
    public float m_Frequency = 1.0f;
    public Vector3 m_Direction = Vector3.forward;
    public float m_Radius = 5.0f;
    [Header("Gizmos")]
    public bool m_DrawGizmos = true;

    private void OnEnable() => LLuxGPUBoneRegistry.RegisterWindCollider(this);
    private void OnDisable() => LLuxGPUBoneRegistry.UnregisterWindCollider(this);

    private void OnDrawGizmos()
    {
        if (!m_DrawGizmos) return;
        Gizmos.color = new Color(1f, 0.8f, 0.2f, 0.4f);
        Gizmos.DrawWireSphere(transform.position, m_Radius);
        Gizmos.DrawLine(transform.position, transform.position + transform.TransformDirection(m_Direction).normalized * 2f);
    }
}
