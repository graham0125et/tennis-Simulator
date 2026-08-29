using System.Collections.Generic;
using UnityEngine;

public class GameplayFpsLogger : MonoBehaviour
{
    [Header("Session Logging")]
    [SerializeField] private float warmupSeconds = 2f;
    [SerializeField] private float logIntervalSeconds = 30f;
    [SerializeField] private bool logPeriodicReports = true;
    [SerializeField] private bool logOnDisable = true;
    [SerializeField] private KeyCode logManualKey = KeyCode.F9;
    [SerializeField] private int maxStoredFrameSamples = 360000;

    private readonly List<float> frameTimes = new List<float>(8192);
    private float warmupRemaining;
    private float elapsed;
    private float intervalElapsed;
    private double totalFrameTime;
    private int frames;
    private int droppedFrameSamples;
    private bool finalReportWritten;

    private void OnEnable()
    {
        ResetStats();
    }

    private void Update()
    {
        float dt = Time.unscaledDeltaTime;
        if (dt <= 0f)
            return;

        if (warmupRemaining > 0f)
        {
            warmupRemaining -= dt;
            return;
        }

        elapsed += dt;
        intervalElapsed += dt;
        totalFrameTime += dt;
        frames++;

        if (frameTimes.Count < Mathf.Max(1, maxStoredFrameSamples))
            frameTimes.Add(dt);
        else
            droppedFrameSamples++;

        if (logManualKey != KeyCode.None && Input.GetKeyDown(logManualKey))
            LogReport("manual");

        if (logPeriodicReports && logIntervalSeconds > 0f && intervalElapsed >= logIntervalSeconds)
        {
            LogReport("periodic");
            intervalElapsed = 0f;
        }
    }

    private void OnDisable()
    {
        if (logOnDisable && !finalReportWritten)
        {
            LogReport("session-end");
            finalReportWritten = true;
        }
    }

    private void OnApplicationQuit()
    {
        if (logOnDisable && !finalReportWritten)
        {
            LogReport("application-quit");
            finalReportWritten = true;
        }
    }

    private void ResetStats()
    {
        frameTimes.Clear();
        warmupRemaining = Mathf.Max(0f, warmupSeconds);
        elapsed = 0f;
        intervalElapsed = 0f;
        totalFrameTime = 0d;
        frames = 0;
        droppedFrameSamples = 0;
        finalReportWritten = false;
    }

    private void LogReport(string reason)
    {
        if (frames <= 0 || totalFrameTime <= 0d || frameTimes.Count == 0)
            return;

        float avgFps = (float)(frames / totalFrameTime);
        List<float> sorted = new List<float>(frameTimes);
        sorted.Sort();

        float p99Frame = PercentileFrameTime(sorted, 0.99f);
        float p999Frame = PercentileFrameTime(sorted, 0.999f);
        float worstFrame = sorted[sorted.Count - 1];
        float bestFrame = sorted[0];

        Debug.Log(
            $"[FPS] {reason} elapsed={elapsed:0.0}s frames={frames} " +
            $"avg={avgFps:0.0}fps 1pctLow={FpsFromFrameTime(p99Frame):0.0}fps " +
            $"0.1pctLow={FpsFromFrameTime(p999Frame):0.0}fps min={FpsFromFrameTime(worstFrame):0.0}fps " +
            $"max={FpsFromFrameTime(bestFrame):0.0}fps maxFrame={worstFrame * 1000f:0.0}ms " +
            $"samples={frameTimes.Count} droppedSamples={droppedFrameSamples}");
    }

    private static float PercentileFrameTime(List<float> sortedFrameTimes, float percentile)
    {
        if (sortedFrameTimes == null || sortedFrameTimes.Count == 0)
            return 0f;

        int index = Mathf.Clamp(Mathf.CeilToInt(percentile * sortedFrameTimes.Count) - 1, 0, sortedFrameTimes.Count - 1);
        return sortedFrameTimes[index];
    }

    private static float FpsFromFrameTime(float frameTime)
    {
        return frameTime > 0f ? 1f / frameTime : 0f;
    }
}
