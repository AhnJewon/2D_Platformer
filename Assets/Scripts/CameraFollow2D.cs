using UnityEngine;

// Smoothly follows a target on X (and loosely on Y) for a side-scroller.
public class CameraFollow2D : MonoBehaviour
{
    public Transform target;
    public Vector2 offset = new Vector2(0f, 1.6f);
    public float smoothX = 0.12f, smoothY = 0.25f;
    public float lookAhead = 1.2f;   // shift towards the facing / moving direction
    Vector3 vel, basePos;
    float ahead;
    Rigidbody2D rb;

    // screen shake (shots, heavy hits): a random offset on top of the follow position, fading over `time`
    static float shakeAmp, shakeLeft, shakeTime;
    public static void Shake(float amplitude, float time)
    {
        float now = shakeTime > 0f ? shakeAmp * shakeLeft / shakeTime : 0f;
        if (amplitude >= now) { shakeAmp = amplitude; shakeTime = shakeLeft = time; }
    }

    void Start() { if (target) rb = target.GetComponent<Rigidbody2D>(); basePos = transform.position; }

    void LateUpdate()
    {
        if (!target) return;
        if (rb && Mathf.Abs(rb.linearVelocity.x) > 0.5f) ahead = Mathf.Lerp(ahead, Mathf.Sign(rb.linearVelocity.x) * lookAhead, Time.deltaTime * 3f);
        var p = basePos;
        float x = Mathf.SmoothDamp(p.x, target.position.x + offset.x + ahead, ref vel.x, smoothX);
        float y = Mathf.SmoothDamp(p.y, target.position.y + offset.y, ref vel.y, smoothY);
        basePos = new Vector3(x, y, transform.position.z);
        Vector3 shake = Vector3.zero;
        if (shakeLeft > 0f)
        {
            shakeLeft -= Time.unscaledDeltaTime;
            shake = (Vector3)(Random.insideUnitCircle * shakeAmp * Mathf.Clamp01(shakeLeft / Mathf.Max(shakeTime, 1e-3f)));
        }
        transform.position = basePos + shake;
    }
}
