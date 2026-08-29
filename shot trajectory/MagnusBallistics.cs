using UnityEngine;

public class MagnusBallistics
{
    // Physical constants
    public float airDensity = 1.2f;        // kg/m3
    public float ballRadius = 0.033f;      // m
    public float mass = 0.057f;            // kg

    // Magnus tuning. Spin is now expected in radians/second.
    public float magnusCoefficient = 0.2f;
    public float spinDecayRate = 0.03f;    // per second. Small spin loss used by both solver prediction and live flight.
    public float maxSpinRatio = 1.2f;

    // Spin-dependent drag tuning. This scales by spin ratio, not raw rad/s.
    public float baseCd = 0.55f;
    public float spinDragCoefficient = 0.15f;

    private float area;

    public MagnusBallistics()
    {
        area = Mathf.PI * ballRadius * ballRadius;
    }

    public Vector3 ApplySpinDecay(Vector3 spinRadPerSecond, float dt)
    {
        float decay = Mathf.Exp(-spinDecayRate * dt);
        return spinRadPerSecond * decay;
    }

    public float SpinRatio(Vector3 velocity, Vector3 spinRadPerSecond)
    {
        float speed = velocity.magnitude;
        if (speed < 0.01f || spinRadPerSecond.sqrMagnitude < 0.0001f)
            return 0f;

        float ratio = ballRadius * spinRadPerSecond.magnitude / speed;
        return Mathf.Clamp(ratio, 0f, Mathf.Max(0f, maxSpinRatio));
    }

    public Vector3 MagnusAcceleration(Vector3 velocity, Vector3 spinRadPerSecond)
    {
        float speed = velocity.magnitude;
        if (speed < 0.01f || spinRadPerSecond.sqrMagnitude < 0.0001f)
            return Vector3.zero;

        Vector3 liftDir = Vector3.Cross(spinRadPerSecond, velocity).normalized;
        float spinRatio = SpinRatio(velocity, spinRadPerSecond);
        float accelerationScale = 0.5f * airDensity * area * speed * speed / mass;
        float liftCoefficient = magnusCoefficient * spinRatio;

        return liftDir * accelerationScale * liftCoefficient;
    }

    public Vector3 DragAcceleration(Vector3 velocity, Vector3 spinRadPerSecond)
    {
        float speed = velocity.magnitude;
        if (speed < 0.01f)
            return Vector3.zero;

        float spinRatio = SpinRatio(velocity, spinRadPerSecond);
        float cdEffective = baseCd + spinDragCoefficient * spinRatio;
        float k = 0.5f * airDensity * cdEffective * area / mass;

        return -k * speed * velocity;
    }
}