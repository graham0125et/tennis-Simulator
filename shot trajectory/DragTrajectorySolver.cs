/*
    DragTrajectorySolver simulates a tennis ball’s flight in 2D (horizontal X and vertical Y).
    It uses RK4 integration — a high‑accuracy numerical method — to step the ball forward
    through time. The solver models two forces:

        • Gravity: a constant downward acceleration.
        • Quadratic drag: air resistance proportional to speed², always opposing motion.

    The maths is simple at its core:
        Position changes because of velocity:
            dx/dt = vx
            dy/dt = vy

        Velocity changes because of acceleration:
            dv/dt = a

        Acceleration comes from:
            a = gravity + drag

    RK4 samples the motion four times per timestep and blends the results, giving a smooth,
    stable trajectory that matches real tennis ball behaviour. The solver can compute:
        • height at a specific X position
        • the ball’s height when crossing the net
        • where the ball lands
        • whether the ball ever hits the ground

    Everything is deterministic and uses fixed timesteps so gameplay and aiming previews
    remain consistent.
*/

/*
    NEW (MAGNUS MODEL):

    This solver has been extended to include full aerodynamic spin effects:

        • Magnus lift (topspin dip, backspin float)
        • Magnus sideways curve (slice, kick serve)
        • Spin‑dependent drag (spinning balls experience more drag)
        • Spin decay (spin reduces over time due to air viscosity + fuzz shear)

    These forces are computed using a MagnusBallistics module and integrated into the
    RK4 solver. The Magnus force is proportional to (ω × v), producing lift and curve
    perpendicular to velocity. Spin decay is exponential and applied every timestep.

    The solver remains 2D for trajectory output, but internally uses a 3D velocity
    and spin vector to compute Magnus forces. Only the X/Y components are returned.
*/

using UnityEngine;

public struct ShotResult
{
    public float yAtNet;   // Height when the ball crosses the net’s X position
    public float xLand;    // Horizontal landing position (interpolated)
    public bool hitGround; // True if the ball touches the ground during simulation
}

public class DragTrajectorySolver
{
    public DragBallistics phys;          // Contains drag constant k and gravity value
    public MagnusBallistics magnus;      // NEW: spin + Magnus physics

    // Fixed timestep for integration (smaller = more accurate, more CPU)
    private const float dt = 0.01f;

    // Safety cutoff: if the ball falls too far below the court, stop simulation
    private const float minY = -5f;

    // Prevents infinite loops if something goes wrong
    private const int maxIterations = 20000;

    // Use the Physics Value from the DragBallistics and MagnusBallistics
    public DragTrajectorySolver(DragBallistics phys, MagnusBallistics magnus)
    {
        this.phys = phys;
        this.magnus = magnus;
    }

    // Computes acceleration from drag + gravity + Magnus
    // Drag formula: a_drag = -k * |v| * v
    // Gravity: a_gravity = (0, g)
    //
    // NEW:
    // Magnus force: a_magnus = S * (ω × v)
    // Spin‑dependent drag: Cd increases with |ω|
    private Vector2 Accel(Vector2 vel2D, Vector3 spin)
    {
        // Convert 2D velocity to 3D for Magnus
        Vector3 vel3 = new Vector3(vel2D.x, vel2D.y, 0f);

        // Spin‑dependent drag
        Vector3 drag3 = magnus.DragAcceleration(vel3, spin);

        // Magnus lift + sideways curve
        Vector3 magnus3 = magnus.MagnusAcceleration(vel3, spin);

        // Gravity
        Vector3 gravity3 = new Vector3(0f, phys.gravity, 0f);

        // Combine
        Vector3 a3 = drag3 + magnus3 + gravity3;

        return new Vector2(a3.x, a3.y);
    }

    // Performs one RK4 integration step:
    // RK4 samples the physics four times and blends them for accuracy.
    // Spin decay is applied once per step using the shared Magnus tuning.
    private void RK4Step(ref Vector2 pos, ref Vector2 vel, ref Vector3 spin, float stepDt)
    {
        spin = magnus.ApplySpinDecay(spin, stepDt);

        // Stage 1
        Vector2 k1_v = Accel(vel, spin);
        Vector2 k1_p = vel;

        // Stage 2
        Vector2 v2 = vel + 0.5f * stepDt * k1_v;
        Vector2 k2_v = Accel(v2, spin);
        Vector2 k2_p = v2;

        // Stage 3
        Vector2 v3 = vel + 0.5f * stepDt * k2_v;
        Vector2 k3_v = Accel(v3, spin);
        Vector2 k3_p = v3;

        // Stage 4
        Vector2 v4 = vel + stepDt * k3_v;
        Vector2 k4_v = Accel(v4, spin);
        Vector2 k4_p = v4;

        // Weighted blend
        vel += (stepDt / 6f) * (k1_v + 2f * k2_v + 2f * k3_v + k4_v);
        pos += (stepDt / 6f) * (k1_p + 2f * k2_p + 2f * k3_p + k4_p);
    }

    // Returns the ball’s height when it reaches a specific X position
    public float GetHeightAtX(Vector2 startPos, float v0, float theta, float targetX, Vector3 spin)
    {
        Vector2 pos = startPos;
        Vector2 vel = new Vector2(
            v0 * Mathf.Cos(theta),
            v0 * Mathf.Sin(theta)
        );

        int iterations = 0;

        while (pos.x < targetX && iterations < maxIterations)
        {
            float prevX = pos.x;
            float prevY = pos.y;

            RK4Step(ref pos, ref vel, ref spin, dt);

            if (pos.y < minY)
                return -999f;

            if (prevX <= targetX && pos.x >= targetX)
            {
                float t = (targetX - prevX) / (pos.x - prevX + Mathf.Epsilon);
                return Mathf.Lerp(prevY, pos.y, t);
            }

            iterations++;
        }

        return pos.y;
    }

    // Simulates the entire shot and returns net height + landing position
    public ShotResult SimulateShot(
        Vector2 startPos,
        float v0,
        float theta,
        float netX,
        Vector3 spin,      // NEW: full spin vector
        float groundY = 0f
    )
    {
        Vector2 pos = startPos;
        Vector2 vel = new Vector2(
            v0 * Mathf.Cos(theta),
            v0 * Mathf.Sin(theta)
        );

        ShotResult result = new ShotResult
        {

            yAtNet = float.NaN,
            xLand = float.NaN,
            hitGround = false
        };

        int iterations = 0;
        bool netCrossed = false;

        while (iterations < maxIterations)
        {
            float prevX = pos.x;
            float prevY = pos.y;

            RK4Step(ref pos, ref vel, ref spin, dt);

            if (!netCrossed && prevX <= netX && pos.x >= netX)
            {
                float t = (netX - prevX) / (pos.x - prevX + Mathf.Epsilon);
                result.yAtNet = Mathf.Lerp(prevY, pos.y, t);
                netCrossed = true;
            }

            if (!result.hitGround && prevY >= groundY && pos.y <= groundY)
            {
                float t = (groundY - prevY) / (pos.y - prevY + Mathf.Epsilon);
                result.xLand = Mathf.Lerp(prevX, pos.x, t);
                result.hitGround = true;
                break;
            }

            if (pos.y < minY)
                break;

            iterations++;
        }

        return result;
    }
}
