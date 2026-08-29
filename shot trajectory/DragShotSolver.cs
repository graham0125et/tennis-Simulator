/*
    DragShotSolver searches for a *feasible tennis shot* that reaches a target
    (the reticle) while clearing the net. It does this by trying different launch
    angles and, for each angle, finding the launch speed that makes the ball land
    at the target X‑position. Every candidate shot is checked to ensure:

        • it clears the net with a safety margin
        • it uses a valid launch speed (within min/max)
        • it has a reasonable apex height (not too high or extreme)

    The solver uses DragTrajectorySolver internally, which simulates the ball’s
    flight using RK4 integration. The maths is simple:

        Position changes because of velocity:
            dx/dt = vx
            dy/dt = vy

        Velocity changes because of acceleration:
            dv/dt = a

        Acceleration comes from:
            a = gravity + drag

    Drag always opposes motion and scales with speed². Gravity pulls downward.
    RK4 samples the motion four times per timestep, giving a smooth and accurate
    trajectory. This solver does *not* simulate the full flight itself — it calls
    the trajectory solver to evaluate each candidate shot.
*/

using System;
using UnityEngine;

public class DragShotSolver
{
    private readonly DragTrajectorySolver traj;   // Shared RK4 trajectory simulator

    // Designer‑tunable limits for shot search
    public float minSpeed = 6f;                   // Lowest allowed launch speed
    public float maxSpeed = BaseShotLibrary.RallyMaxSpeedMps; // Highest allowed launch speed
    public float minAngleDeg = 8f;                // Lowest allowed launch angle
    public float maxAngleDeg = 40f;               // Highest allowed launch angle
    public float netMargin = 0.25f;               // Extra height required above the net

    // Logging control to avoid console spam
    private static int _solverLogCount = 0;
    private const int _solverLogMax = 5;

    // Internal constants for search behaviour
    private const float landingHeight = 0f;       // Ground level
    private const int angleStepDeg = 1;           // Angle sweep step size
    private const float apexDt = 0.01f;           // Timestep for apex simulation
    private const int apexMaxSteps = 5000;        // Safety limit for apex loop
    private const float apexMaxHeight = 200f;     // Prevents runaway trajectories

    public DragShotSolver(DragTrajectorySolver traj)
    {
        this.traj = traj;                         // Inject trajectory solver
    }
    public float PredictLandingX(Vector2 startPos, float v0, float theta, Vector3 spin)

    {
        ShotResult result = traj.SimulateShot(startPos, v0, theta, float.NegativeInfinity, spin, 0f);
        return result.xLand;



    }



    // Main solver: finds a launch speed + angle that reaches xTarget and clears the net
    public (float v0, float theta) SolveForReticle(
    Vector2 startPos,
    float xTarget,
    float netX,
    float netHeight,
    float netMarginLocal,
    Vector3 spin        // NEW
        )

    {
        // Use provided margin if valid, otherwise fallback to default
        float margin = float.IsNaN(netMarginLocal) ? this.netMargin : netMarginLocal;

        // Reject targets behind the player
        if (xTarget <= startPos.x)
            return (float.NaN, float.NaN);

        // Reject targets too close to the net (not enough space to arc)
        if (xTarget < netX + 0.5f)
        {
            LogOnce($"[ShotSolver] Target too close to net. xTarget={xTarget}, netX={netX}");
            return (float.NaN, float.NaN);
        }

        // Best solution found so far
        float bestV0 = float.PositiveInfinity;
        float bestTheta = 0f;
        float bestApex = float.PositiveInfinity;

        // Sweep through candidate angles (in degrees)
        for (float deg = minAngleDeg; deg <= maxAngleDeg; deg += angleStepDeg)
        {
            float theta = deg * Mathf.Deg2Rad;    // Convert degrees → radians

            // For this angle, find the launch speed that lands at xTarget
            float v0 = SolveV0ForAngle(startPos, xTarget, theta, spin);

            if (!float.IsFinite(v0)) continue;    // Skip invalid solutions
            if (v0 < minSpeed || v0 > maxSpeed) continue;

            // Check height at the net
            float yAtNet = traj.GetHeightAtX(startPos, v0, theta, netX, spin);

            if (!float.IsFinite(yAtNet)) continue;
            if (yAtNet < netHeight + margin) continue; // Must clear net with margin

            // Compute apex height (highest point of trajectory)
            float apex = ComputeApexHeight(startPos, v0, theta, spin);

            if (!float.IsFinite(apex)) continue;

            // Prefer lower apex (flatter, more realistic shot)
            // If apex ties, prefer lower launch speed
            bool better =
                apex < bestApex ||
                (Mathf.Approximately(apex, bestApex) && v0 < bestV0);

            if (better)
            {
                bestApex = apex;
                bestV0 = v0;
                bestTheta = theta;
            }
        }

        // If no valid solution found, return NaN pair
        if (!float.IsFinite(bestV0))
        {
            LogOnce("[ShotSolver] Solver result: no feasible solution found");
            return (float.NaN, float.NaN);
        }

        // Log final chosen solution
        LogOnce($"[ShotSolver] Solver result: v0={bestV0:F5}, theta={bestTheta:F7}, apex={bestApex:F5}");
        return (bestV0, bestTheta);
    }

    // PUBLIC WRAPPER — solve for speed only, given a fixed angle
    public float SolveSpeedForFixedAngle(
        Vector2 startPos,
        float xTarget,
        float theta,
        Vector3 spin,
        int iterations = 32
    )
    {
        return SolveV0ForAngle(startPos, xTarget, theta, spin, iterations);
    }

    // Finds the launch speed for a fixed angle that makes the ball reach xTarget
    // Uses binary search: repeatedly halves the speed range until converged
    // Finds the launch speed for a fixed angle that makes the ball reach xTarget
    // Uses binary search: repeatedly halves the speed range until converged
    private float SolveV0ForAngle(Vector2 startPos, float xTarget, float theta, Vector3 spin, int iterations = 32)
    {
        float low = minSpeed;     // Too weak
        float high = maxSpeed;    // Too strong

        // Evaluate heights at low and high speeds
        float yLow = traj.GetHeightAtX(startPos, low, theta, xTarget, spin);   // UPDATED
        float yHigh = traj.GetHeightAtX(startPos, high, theta, xTarget, spin); // UPDATED

        if (!float.IsFinite(yLow) || !float.IsFinite(yHigh))
            return float.NaN;

        int solveIterations = Mathf.Clamp(iterations, 4, 32);
        for (int i = 0; i < solveIterations; i++)
        {
            float mid = 0.5f * (low + high);  // Midpoint speed
            float yAtTarget = traj.GetHeightAtX(startPos, mid, theta, xTarget, spin); // UPDATED
            if (!float.IsFinite(yAtTarget)) return float.NaN;

            // If mid-speed shot is ABOVE ground at target, speed is too high → reduce
            if (yAtTarget > landingHeight)
                high = mid;
            else
                low = mid;
        }

        return 0.5f * (low + high); // Final converged speed
    }


    // Computes the apex (highest point) of the trajectory using RK4 physics
    // UPDATED: now accepts spin and passes it into the local RK4 integrator
    private float ComputeApexHeight(Vector2 startPos, float v0, float theta, Vector3 spin)
    {
        // Initial state
        Vector2 pos = startPos;
        Vector2 vel = new Vector2(
            v0 * Mathf.Cos(theta),
            v0 * Mathf.Sin(theta)
        );

        int steps = 0;

        // Continue until vertical velocity becomes negative (ball starts falling)
        while (vel.y > 0f && steps < apexMaxSteps && pos.y < apexMaxHeight)
        {
            RK4StepLocal(ref pos, ref vel, ref spin, apexDt);   // UPDATED: spin added

            if (!float.IsFinite(pos.y))
                return float.PositiveInfinity;

            steps++;
        }

        return pos.y; // Highest point reached
    }


    // Local RK4 integrator (same physics as DragTrajectorySolver)
    private void RK4StepLocal(ref Vector2 pos, ref Vector2 vel, ref Vector3 spin, float stepDt)

    {
        // Computes acceleration from drag + gravity
        Vector2 Accel(Vector2 v)
        {
            float drag = traj.phys.k;
            float g = traj.phys.gravity;

            float speed = v.magnitude;
            Vector2 dragAccel = -drag * speed * v; // Opposes motion
            Vector2 gravityAccel = new Vector2(0f, g);

            return dragAccel + gravityAccel;
        }

        // RK4 stage 1
        Vector2 k1_v = Accel(vel);
        Vector2 k1_p = vel;

        // RK4 stage 2
        Vector2 v2 = vel + 0.5f * stepDt * k1_v;
        Vector2 k2_v = Accel(v2);
        Vector2 k2_p = v2;

        // RK4 stage 3
        Vector2 v3 = vel + 0.5f * stepDt * k2_v;
        Vector2 k3_v = Accel(v3);
        Vector2 k3_p = v3;

        // RK4 stage 4
        Vector2 v4 = vel + stepDt * k3_v;
        Vector2 k4_v = Accel(v4);
        Vector2 k4_p = v4;

        // Weighted blend of all four stages
        vel += (stepDt / 6f) * (k1_v + 2f * k2_v + 2f * k3_v + k4_v);
        pos += (stepDt / 6f) * (k1_p + 2f * k2_p + 2f * k3_p + k4_p);
    }

    // Logs a message only a limited number of times
    private static void LogOnce(string msg)
    {
        if (!Debug.isDebugBuild) return;
        if (_solverLogCount >= _solverLogMax) return;

        Debug.Log($"{msg}  (log {(_solverLogCount + 1)}/{_solverLogMax})");
        _solverLogCount++;
    }

    // Resets logging counter
    public static void ResetSolverLogCount()
    {
        _solverLogCount = 0;
    }
}
