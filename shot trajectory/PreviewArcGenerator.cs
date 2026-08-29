/*
    PreviewArcGenerator is responsible for drawing a real‑time, physics‑accurate
    trajectory preview using the same RK4 integrator that drives the gameplay
    solver. It samples the ball’s 2D flight at fixed time intervals, converts the
    horizontal displacement into a 3D world‑space direction based on the reticle,
    and feeds those points into a LineRenderer. This ensures the preview arc
    visually matches the solver’s predicted shot path under drag, gravity, and
    launch parameters.

    The component is intentionally stateless: it does not compute solutions or
    store shot data. Instead, it relies entirely on the DragTrajectorySolver for
    physics and focuses solely on rendering. If any required references are
    missing, or if the preview is disabled, or if the simulated shot becomes
    invalid (e.g., the ball hits the ground early), the arc is cleared to avoid
    misleading feedback. The structure keeps the update flow predictable and
    ensures the preview always reflects the current aiming direction and physics.
*/

using UnityEngine;

public class PreviewArcGenerator : MonoBehaviour
{
    [Header("Rendering")]
    public LineRenderer line;
    // The LineRenderer that draws the predicted trajectory in 3D space.
    // Each point in the line corresponds to a simulated RK4 step of the ball’s flight.

    public int resolution = 60;
    // Maximum number of sample points to draw along the arc.
    // Higher = smoother arc, but slightly more CPU cost.

    public bool enabledInGame = true;
    // Allows the preview to be toggled on/off without disabling the component.
    // Useful for gameplay states where aiming preview should be hidden.

    [Header("Aiming references")]
    public Transform contactPoint;
    // The 3D world position where the ball will be struck.
    // This is the origin of the trajectory in world space.

    public Transform reticle;
    // The player’s aiming target.
    // The arc is projected horizontally toward this point.

    [HideInInspector] public DragTrajectorySolver traj;
    // The RK4 physics solver shared with gameplay.
    // Hidden in Inspector because it is assigned at runtime.

    [Header("Performance")]
    public float uncachedDrawInterval = 0.033f;

    // Throttle only the legacy uncached DrawArc path.
    // Cached draws are already throttled by AimingController.
    private float nextArcTime = 0f;

    public int MaxPointCount => Mathf.Max(2, resolution);

    // Assigns the solver instance used for RK4 integration
    public void Initialise(DragTrajectorySolver solver)
    {
        // Called by AimingController so the preview uses the same physics as gameplay.
        traj = solver;
    }

    // Removes all points from the LineRenderer
    public void ClearArc()
    {
        // Setting positionCount to 0 hides the arc completely.
        if (line != null)
            line.positionCount = 0;
    }

    // Legacy wrapper: draws a full RK4-integrated trajectory arc in world space.
    public void DrawArc(Vector2 startPos2D, float v0, float theta, Vector3 spin)
    {
        Vector3 start3D = contactPoint != null
            ? contactPoint.position
            : new Vector3(startPos2D.x, startPos2D.y, 0f);
        Vector3 target3D = reticle != null
            ? reticle.position
            : start3D + Vector3.right;

        DrawArc(start3D, startPos2D, target3D, v0, theta, spin);
    }

    public void DrawArc(Vector3 start3D, Vector2 startPos2D, Vector3 target3D, float v0, float theta, Vector3 spin)
    {
        // Throttle uncached drawing to avoid FPS spikes.
        // This reduces CPU load but still updates often enough to feel responsive.
        if (Time.time < nextArcTime)
            return;

        nextArcTime = Time.time + Mathf.Max(0.001f, uncachedDrawInterval);

        if (!IsSetupValid())
        {
            ClearArc();
            return;
        }

        Vector3[] points = new Vector3[MaxPointCount];
        int pointCount = BuildArcPoints(start3D, startPos2D, target3D, v0, theta, spin, points);
        DrawCachedArc(points, pointCount);
    }

    public int BuildArcPoints(
        Vector3 start3D,
        Vector2 startPos2D,
        Vector3 target3D,
        float v0,
        float theta,
        Vector3 spin,
        Vector3[] points)
    {
        if (!IsSetupValid() || points == null || points.Length == 0)
            return 0;

        // Compute the horizontal direction from contact point to reticle.
        // This direction is used to project the 2D simulation into 3D space.
        Vector3 flatDir = target3D - start3D;
        flatDir.y = 0f; // Remove vertical component so direction stays on the ground plane.

        // If the reticle is extremely close, direction becomes undefined.
        if (flatDir.sqrMagnitude < 0.0001f)
        {
            return 0;
        }

        flatDir.Normalize(); // Convert to unit vector for consistent scaling.

        // Dynamic resolution based on shot distance:
        // Short shots = fewer points, long shots = more points.
        float dist = Vector3.Distance(
            new Vector3(start3D.x, 0f, start3D.z),
            new Vector3(target3D.x, 0f, target3D.z)
        );
        int dynamicRes = Mathf.Clamp(Mathf.RoundToInt(dist * 3f), 2, points.Length);

        // Initial 2D state for RK4 integration
        // pos2D = (x, y), vel2D = (vx, vy)
        Vector2 pos2D = startPos2D;
        Vector2 vel2D = new Vector2(
            v0 * Mathf.Cos(theta), // Horizontal velocity component
            v0 * Mathf.Sin(theta)  // Vertical velocity component
        );

        // Cheaper dt for preview only (gameplay uses accurate dt)
        float dt = 0.03f;
        Vector3 spinState = spin;
        int pointCount = 0;

        for (int i = 0; i < dynamicRes; i++)
        {
            // Horizontal distance travelled in 2D
            float horizontalDist = pos2D.x - startPos2D.x;

            // Convert 2D horizontal displacement into a 3D world position:
            //   - Move along flatDir for horizontal distance
            //   - Use pos2D.y for vertical height
            Vector3 pos3D = start3D + flatDir * horizontalDist;
            pos3D.y = pos2D.y;

            points[i] = pos3D;
            pointCount = i + 1;

            // Advance the 2D physics simulation using RK4
            RK4Step(ref pos2D, ref vel2D, ref spinState, dt);


            // If physics becomes invalid (NaN or Infinity), stop early
            if (!float.IsFinite(pos2D.x) || !float.IsFinite(pos2D.y))
            {
                return pointCount;
            }

            // Stop early if the ball hits the ground (y < 0)
            if (pos2D.y < 0f)
            {
                return pointCount;
            }
        }

        return pointCount;
    }

    public void DrawCachedArc(Vector3[] points, int pointCount)
    {
        if (line == null)
            return;

        if (!enabledInGame || points == null || pointCount <= 1)
        {
            ClearArc();
            return;
        }

        int safeCount = Mathf.Min(pointCount, points.Length);
        line.positionCount = safeCount;

        for (int i = 0; i < safeCount; i++)
            line.SetPosition(i, points[i]);
    }

    // Ensures all required references exist and preview is enabled
    bool IsSetupValid()
    {
        // The preview requires:
        //   - a LineRenderer to draw into
        //   - a trajectory solver for RK4 physics
        //   - a contact point (3D origin)
        //   - a reticle (aim direction)
        //   - preview enabled
        return line != null &&
               traj != null &&
               contactPoint != null &&
               reticle != null &&
               enabledInGame;
    }

    // Performs a single RK4 integration step for 2D ballistic motion
    void RK4Step(ref Vector2 pos, ref Vector2 vel, ref Vector3 spin, float dt)
{
    spin = traj.magnus.ApplySpinDecay(spin, dt);
    Vector3 spinForStep = spin;

    Vector2 Accel(Vector2 v)
    {
        Vector3 v3 = new Vector3(v.x, v.y, 0f);

        Vector3 drag3   = traj.magnus.DragAcceleration(v3, spinForStep);
        Vector3 magnus3 = traj.magnus.MagnusAcceleration(v3, spinForStep);
        Vector3 gravity3 = new Vector3(0f, traj.phys.gravity, 0f);

        Vector3 a3 = drag3 + magnus3 + gravity3;
        return new Vector2(a3.x, a3.y);
    }

    // RK4 stages (unchanged)
    Vector2 k1_v = Accel(vel);
    Vector2 k1_p = vel;

    Vector2 v2 = vel + 0.5f * dt * k1_v;
    Vector2 k2_v = Accel(v2);
    Vector2 k2_p = v2;

    Vector2 v3 = vel + 0.5f * dt * k2_v;
    Vector2 k3_v = Accel(v3);
    Vector2 k3_p = v3;

    Vector2 v4 = vel + dt * k3_v;
    Vector2 k4_v = Accel(v4);
    Vector2 k4_p = v4;

    vel += (dt / 6f) * (k1_v + 2*k2_v + 2*k3_v + k4_v);
    pos += (dt / 6f) * (k1_p + 2*k2_p + 2*k3_p + k4_p);
}

}

