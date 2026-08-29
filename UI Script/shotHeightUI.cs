using UnityEngine;
using TMPro;

public class ShotHeightUI : MonoBehaviour
{
    // HEIGHT ANGLES
    public TextMeshProUGUI targetHeightAngleValue;   // solver/trajectory height angle
    public TextMeshProUGUI actualHeightAngleValue;   // final applied height angle

    // BALL SPEEDS
    public TextMeshProUGUI manualBallSpeedValue;       // raw swipe speed
    public TextMeshProUGUI blendedBallSpeedValue;      // final launch speed
    public TextMeshProUGUI targetBallSpeedValue;   // solver-calculated speed to reticle


    // LATERAL ANGLES
    public TextMeshProUGUI lateralAimValue;            // angle to reticle
    public TextMeshProUGUI lateralShotValue;           // actual lateral shot angle

    // SPEED CAPS
    public TextMeshProUGUI backswingCapSpeedValue; // raw backswing cap in mph
    public TextMeshProUGUI retainedCapSpeedValue;  // generated contact speed cap after retention in mph

    // NET CLEARANCE
    public TextMeshProUGUI solverNetClearanceValue;    // requested solver net clearance in cm
    public TextMeshProUGUI actualNetClearanceValue;    // estimated actual launched clearance in cm

    // HEIGHT UPDATES
    public void UpdateTargetHeight(float angle)
    {
        targetHeightAngleValue.text = $"{angle:F1}°";
    }

    public void UpdateActualHeight(float angle)
    {
        actualHeightAngleValue.text = $"{angle:F1}°";
    }

    // SPEED UPDATES
    public void UpdateManualBallSpeed(float speed)
    {
        manualBallSpeedValue.text = $"{speed:F1} m/s";
    }

    public void UpdateBlendedBallSpeed(float speed)
    {
        blendedBallSpeedValue.text = $"{speed:F1} m/s";
    }

    public void UpdateTargetBallSpeed(float speed)
    {
        targetBallSpeedValue.text = $"{speed:F1} m/s";
    }

    public void UpdateBackswingCapSpeed(float speedMph)
    {
        if (backswingCapSpeedValue != null)
            backswingCapSpeedValue.text = FormatMph(speedMph);
    }

    public void UpdateRetainedCapSpeed(float speedMph)
    {
        if (retainedCapSpeedValue != null)
            retainedCapSpeedValue.text = FormatMph(speedMph);
    }


    // LATERAL UPDATES
    public void UpdateLateralAim(float angle)
    {
        lateralAimValue.text = $"{angle:F1}°";
    }

    public void UpdateLateralShot(float angle)
    {
        lateralShotValue.text = $"{angle:F1}°";
    }

    public void UpdateSolverNetClearance(float clearanceCm)
    {
        if (solverNetClearanceValue != null)
            solverNetClearanceValue.text = FormatClearanceCm(clearanceCm);
    }

    public void UpdateActualNetClearance(float clearanceCm)
    {
        if (actualNetClearanceValue != null)
            actualNetClearanceValue.text = FormatClearanceCm(clearanceCm);
    }

    private static string FormatClearanceCm(float clearanceCm)
    {
        return float.IsFinite(clearanceCm) ? $"{clearanceCm:F0} cm" : "--";
    }

    private static string FormatMph(float speedMph)
    {
        return float.IsFinite(speedMph) ? $"{speedMph:F0} mph" : "--";
    }
}