using UnityEngine;

[DisallowMultipleComponent]
public class ShotSolverComponent : MonoBehaviour
{
    [Header("Solver Tuning")]
    public float minSpeed = 6f;
    public float maxSpeed = BaseShotLibrary.RallyMaxSpeedMps;
    public float minAngleDeg = 8f;
    public float maxAngleDeg = 40f;
    public float netMargin = 0.25f;

    // Runtime instances (read-only from other scripts)
    public DragBallistics phys { get; private set; }
    public MagnusBallistics magnus { get; private set; }   // NEW
    public DragTrajectorySolver traj { get; private set; }
    public DragShotSolver solver { get; private set; }

    void Awake()
    {
        // Create physics models
        phys = new DragBallistics();
        magnus = new MagnusBallistics();                    // NEW

        // Create trajectory solver with both drag + Magnus
        traj = new DragTrajectorySolver(phys, magnus);      // FIXED

        // Create shot solver using the shared trajectory solver
        solver = new DragShotSolver(traj);

        // Apply inspector tuning
        ApplyTuning();
    }

    // If you change tuning at runtime in the inspector, call this to reapply
    public void ApplyTuning()
    {
        if (solver == null) return;

        solver.minSpeed = minSpeed;
        solver.maxSpeed = maxSpeed;
        solver.minAngleDeg = minAngleDeg;
        solver.maxAngleDeg = maxAngleDeg;
        solver.netMargin = netMargin;
    }
}
