using UnityEngine;

public sealed class TennisNetImpactRelay : MonoBehaviour
{
    [SerializeField] private TennisNetImpactWave wave;

    public void SetWave(TennisNetImpactWave impactWave)
    {
        wave = impactWave;
    }

    private void OnCollisionEnter(Collision collision)
    {
        if (wave == null || collision == null || collision.contactCount == 0)
            return;

        wave.RegisterImpact(collision.GetContact(0).point, collision.relativeVelocity);
    }
}
