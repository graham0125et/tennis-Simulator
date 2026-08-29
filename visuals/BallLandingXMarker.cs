using UnityEngine;

[DefaultExecutionOrder(80)]
public class BallLandingXMarker : MonoBehaviour
{
    [Header("Ball")]
    public Rigidbody ball;
    public float minBallSpeed = 4f;

    [Header("Court")]
    public float netX = 0f;
    public float courtY = 0f;
    public float ballRadius = 0.033f;
    public float minimumOpponentDepth = 0.35f;

    [Header("Prediction")]
    public float updateInterval = 0.04f;
    public float predictionSettleSeconds = 0.12f;
    public float predictionStep = 0.02f;
    public float maxPredictionSeconds = 3f;
    public float postBounceVisibleSeconds = 0.65f;

    [Header("Marker")]
    public float markerRadius = 0.22f;
    public float markerHeight = 0.035f;
    public float lineWidth = 0.025f;
    public Color markerColor = new Color(0.08f, 0.95f, 1f, 0.9f);

    private LineRenderer lineA;
    private LineRenderer lineB;
    private Material markerMaterial;
    private float nextPredictionTime;
    private float visibleUntil;
    private float shotStartSide;
    private bool hasShotStartSide;
    private Vector3 lastVelocity;
    private float shotChangedTime;
    private bool markerLocked;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void Bootstrap()
    {
        if (FindFirstObjectByType<BallLandingXMarker>() != null)
            return;

        var marker = new GameObject("BallLandingXMarker");
        DontDestroyOnLoad(marker);
        marker.AddComponent<BallLandingXMarker>();
    }

    private void Awake()
    {
        EnsureMarkerRenderers();
        SetMarkerVisible(false);
    }

    private void Update()
    {
        EnsureBallReference();
        if (ball == null)
        {
            SetMarkerVisible(false);
            return;
        }

        if (Time.time >= nextPredictionTime)
        {
            nextPredictionTime = Time.time + Mathf.Max(0.01f, updateInterval);
            UpdateShotSideTracking();
            UpdatePrediction();
        }

        if (Time.time > visibleUntil)
            SetMarkerVisible(false);
    }

    private void EnsureBallReference()
    {
        if (ball != null)
            return;

        BallController controller = FindFirstObjectByType<BallController>();
        if (controller != null)
            ball = controller.GetComponent<Rigidbody>();
    }

    private void EnsureMarkerRenderers()
    {
        if (markerMaterial == null)
        {
            Shader shader = Shader.Find("Sprites/Default");
            if (shader == null)
                shader = Shader.Find("Universal Render Pipeline/Unlit");
            if (shader == null)
                shader = Shader.Find("Unlit/Color");

            markerMaterial = new Material(shader);
            markerMaterial.name = "Ball Landing X Marker Material";
            markerMaterial.color = markerColor;
        }

        lineA = EnsureLineRenderer(lineA, "LandingX_A");
        lineB = EnsureLineRenderer(lineB, "LandingX_B");
    }

    private LineRenderer EnsureLineRenderer(LineRenderer line, string childName)
    {
        if (line != null)
            return line;

        Transform child = transform.Find(childName);
        GameObject lineObject = child != null ? child.gameObject : new GameObject(childName);
        lineObject.transform.SetParent(transform, false);

        line = lineObject.GetComponent<LineRenderer>();
        if (line == null)
            line = lineObject.AddComponent<LineRenderer>();

        line.useWorldSpace = true;
        line.positionCount = 2;
        line.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        line.receiveShadows = false;
        line.alignment = LineAlignment.View;
        line.textureMode = LineTextureMode.Stretch;
        line.material = markerMaterial;
        line.startWidth = lineWidth;
        line.endWidth = lineWidth;
        line.startColor = markerColor;
        line.endColor = markerColor;
        return line;
    }

    private void UpdateShotSideTracking()
    {
        Vector3 velocity = ball.linearVelocity;
        float speed = velocity.magnitude;
        float currentSide = SideSign(ball.position.x);
        Vector3 lastPlanarVelocity = new Vector3(lastVelocity.x, 0f, lastVelocity.z);
        Vector3 planarVelocity = new Vector3(velocity.x, 0f, velocity.z);
        bool directionChanged = lastPlanarVelocity.sqrMagnitude > 1f
            && planarVelocity.sqrMagnitude > 1f
            && speed > minBallSpeed
            && Vector3.Dot(lastPlanarVelocity.normalized, planarVelocity.normalized) < 0.25f;

        if (speed < 1f)
        {
            hasShotStartSide = false;
            markerLocked = false;
        }
        else if (!hasShotStartSide || directionChanged)
        {
            shotStartSide = currentSide;
            hasShotStartSide = true;
            shotChangedTime = Time.time;
            markerLocked = false;
            SetMarkerVisible(false);
        }

        lastVelocity = velocity;
    }

    private void UpdatePrediction()
    {
        Vector3 velocity = ball.linearVelocity;
        if (velocity.magnitude < minBallSpeed)
            return;

        if (Time.time < shotChangedTime + predictionSettleSeconds)
            return;

        if (markerLocked)
            return;

        if (TryPredictFirstBounce(ball.position, velocity, out Vector3 bouncePoint, out float timeToBounce)
            && IsOpponentSideBounce(bouncePoint.x))
        {
            SetMarkerPosition(bouncePoint);
            visibleUntil = Time.time + Mathf.Max(0.05f, timeToBounce) + postBounceVisibleSeconds;
            markerLocked = true;
            SetMarkerVisible(true);
        }
    }

    private bool TryPredictFirstBounce(Vector3 startPosition, Vector3 startVelocity, out Vector3 bouncePoint, out float timeToBounce)
    {
        Vector3 position = startPosition;
        Vector3 velocity = startVelocity;
        float groundY = courtY + ballRadius;
        bouncePoint = Vector3.zero;
        timeToBounce = 0f;

        for (float t = predictionStep; t <= maxPredictionSeconds; t += predictionStep)
        {
            Vector3 previous = position;
            velocity += Physics.gravity * predictionStep;
            position += velocity * predictionStep;

            if (previous.y > groundY && position.y <= groundY)
            {
                float lerp = Mathf.InverseLerp(previous.y, position.y, groundY);
                bouncePoint = Vector3.Lerp(previous, position, lerp);
                bouncePoint.y = markerHeight;
                timeToBounce = t;
                return true;
            }
        }

        return false;
    }

    private bool IsOpponentSideBounce(float x)
    {
        if (!hasShotStartSide)
            return Mathf.Abs(x - netX) >= minimumOpponentDepth;

        float bounceSide = SideSign(x);
        return bounceSide != shotStartSide && Mathf.Abs(x - netX) >= minimumOpponentDepth;
    }

    private float SideSign(float x)
    {
        return x >= netX ? 1f : -1f;
    }

    private void SetMarkerPosition(Vector3 center)
    {
        EnsureMarkerRenderers();

        Vector3 forwardLeft = new Vector3(-markerRadius, 0f, -markerRadius);
        Vector3 forwardRight = new Vector3(markerRadius, 0f, markerRadius);
        Vector3 backLeft = new Vector3(-markerRadius, 0f, markerRadius);
        Vector3 backRight = new Vector3(markerRadius, 0f, -markerRadius);

        lineA.SetPosition(0, center + forwardLeft);
        lineA.SetPosition(1, center + forwardRight);
        lineB.SetPosition(0, center + backLeft);
        lineB.SetPosition(1, center + backRight);
    }

    private void SetMarkerVisible(bool visible)
    {
        if (lineA != null)
            lineA.enabled = visible;
        if (lineB != null)
            lineB.enabled = visible;
    }
}
