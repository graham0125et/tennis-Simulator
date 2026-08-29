using UnityEngine;

public class VelocityWatcher : MonoBehaviour
{
    Rigidbody rb;
    Vector3 lastVel;

    void Awake() { rb = GetComponent<Rigidbody>(); lastVel = Vector3.zero; }

    void FixedUpdate()
    {
        if (rb == null) return;

        if (lastVel.magnitude > 0.001f && rb.linearVelocity.magnitude < 0.001f)
        {
            Debug.LogError($"[VelocityWatcher] Velocity dropped to zero on {gameObject.name}. StackTrace:\n{System.Environment.StackTrace}");
            Component[] comps = GetComponents<Component>();
            string list = "";
            foreach (var c in comps) list += c.GetType().Name + "; ";
            Debug.Log($"[VelocityWatcher] Components: {list}");
        }

        lastVel = rb.linearVelocity;
    }
}
