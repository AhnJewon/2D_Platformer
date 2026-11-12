using UnityEngine;

// Smoothly follows a target on X (and loosely on Y) for a side-scroller.
public class CameraFollow2D : MonoBehaviour
{
    public Transform target;
    public Vector2 offset = new Vector2(0f, 1.6f);
    public float smoothX = 0.12f, smoothY = 0.25f;
    public float lookAhead = 1.2f;   // shift towards the facing / moving direction
    Vector3 vel;
    float ahead;
    Rigidbody2D rb;

    void Start() { if (target) rb = target.GetComponent<Rigidbody2D>(); }

    void LateUpdate()
    {
        if (!target) return;
        if (rb && Mathf.Abs(rb.linearVelocity.x) > 0.5f) ahead = Mathf.Lerp(ahead, Mathf.Sign(rb.linearVelocity.x) * lookAhead, Time.deltaTime * 3f);
        var p = transform.position;
        float x = Mathf.SmoothDamp(p.x, target.position.x + offset.x + ahead, ref vel.x, smoothX);
        float y = Mathf.SmoothDamp(p.y, target.position.y + offset.y, ref vel.y, smoothY);
        transform.position = new Vector3(x, y, p.z);
    }
}
