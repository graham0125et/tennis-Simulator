using UnityEngine;
/*
    DragBallistics models the aerodynamic behaviour of a tennis ball by computing
    the acceleration caused by quadratic air resistance. The class stores physical
    constants such as air density, drag coefficient, ball radius, and mass, all of
    which determine how strongly drag acts on the ball during flight. From these
    values it derives the ball’s cross‑sectional area and a combined drag constant
    k, which represents the term (0.5 * airDensity * Cd * area / mass) found in the
    standard drag equation. This constant allows the simulation to compute drag
    acceleration efficiently without recalculating the same factors every frame.

    Quadratic drag is proportional to both the speed of the object and the velocity
    vector itself, producing a force that always opposes motion and grows rapidly
    with increasing speed. The DragAcceleration method takes a 2D velocity vector,
    measures its magnitude, and returns the corresponding drag acceleration using
    the formula a = -k * v * velocity. This produces a physically grounded result
    suitable for integrating into a full ballistic simulation. Gravity is included
    as a reference constant but is not applied inside this class, allowing external
    systems to combine drag and gravity as needed when updating velocity and
    position. Overall, this class provides a compact, efficient foundation for
    realistic tennis‑ball flight modelling in Unity.
*/
public class DragBallistics
{
    // Physical constants
    public float gravity = -9.81f;      // Downward acceleration
    public float airDensity = 1.2f;     // kg/m³
    public float Cd = 0.55f;            // Drag coefficient for tennis ball
    public float radius = 0.033f;       // m
    public float mass = 0.057f;         // kg

    // Derived values
    public float area;                  // πr²
    public float k;                     // 0.5 * rho * Cd * A / m

    public DragBallistics()
    {
        area = Mathf.PI * radius * radius;
        k = 0.5f * airDensity * Cd * area / mass;
    }

    // Quadratic drag acceleration for a given velocity
    public Vector2 DragAcceleration(Vector2 velocity)
    {
        float speed = velocity.magnitude;
        return -k * speed * velocity;
    }
}