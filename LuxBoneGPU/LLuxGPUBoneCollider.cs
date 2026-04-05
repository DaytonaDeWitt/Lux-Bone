using UnityEngine;

[AddComponentMenu("Lux GPU Bone/LLux GPU Bone Collider")]
public class LLuxGPUBoneCollider : MonoBehaviour
{
    public enum Shape { Sphere, Capsule, Plane }

    public Shape m_Shape = Shape.Sphere;
    public Vector3 m_Center = Vector3.zero;
    public float m_Radius = 0.5f;
    public float m_Height = 2.0f;
    public Vector3 m_Normal = Vector3.up;

    /// <summary>Returns the uniform world scale of this collider's transform.</summary>
    public float GetScale() => Mathf.Abs(transform.lossyScale.x);

    private void OnEnable()  { }
    private void OnDisable() { }

    public bool Collide(ref Vector3 pos, float pr)
    {
        switch (m_Shape)
        {
            case Shape.Sphere: return CollideSphere(ref pos, pr);
            case Shape.Plane: return CollidePlane(ref pos, pr);
            case Shape.Capsule: return CollideCapsule(ref pos, pr);
        }
        return false;
    }

    private bool CollideSphere(ref Vector3 pos, float pr)
    {
        Vector3 origin = transform.TransformPoint(m_Center);
        float radius = m_Radius * GetScale() + pr;
        Vector3 diff = pos - origin;
        float dist = diff.magnitude;
        if (dist < radius && dist > 0.0001f)
        {
            pos = origin + diff.normalized * radius;
            return true;
        }
        return false;
    }

    private bool CollidePlane(ref Vector3 pos, float pr)
    {
        Vector3 normal = transform.TransformDirection(m_Normal).normalized;
        Vector3 origin = transform.TransformPoint(m_Center);
        float dist = Vector3.Dot(pos - origin, normal);
        if (dist < pr)
        {
            pos += normal * (pr - dist);
            return true;
        }
        return false;
    }

    private bool CollideCapsule(ref Vector3 pos, float pr)
    {
        Vector3 origin = transform.TransformPoint(m_Center);
        float radius = m_Radius * GetScale() + pr;
        Vector3 diff = pos - origin;
        float dist = diff.magnitude;
        if (dist < radius && dist > 0.0001f)
        {
            pos = origin + diff.normalized * radius;
            return true;
        }
        return false;
    }

    private void OnDrawGizmosSelected()
    {
        Gizmos.color = new Color(0.4f, 1f, 0.8f, 0.7f);
        Vector3 worldCenter = transform.TransformPoint(m_Center);
        float s = GetScale();

        if (m_Shape == Shape.Sphere)
            Gizmos.DrawWireSphere(worldCenter, m_Radius * s);
        else if (m_Shape == Shape.Plane)
        {
            Gizmos.DrawLine(worldCenter, worldCenter + transform.TransformDirection(m_Normal) * 0.5f);
            Gizmos.DrawWireSphere(worldCenter, 0.05f);
        }
    }
}
