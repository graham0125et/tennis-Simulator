using UnityEngine;
using System;
using System.Reflection;
using System.Collections;
using System.Collections.Generic;

public class BallReferenceFinder : MonoBehaviour
{
    public GameObject ball; // assign the spawned ball or leave null to auto-find by tag "Ball"
    public float scanDelay = 0.1f; // wait a bit after hit to let things run

    void Start()
    {
        if (ball == null)
        {
            var b = GameObject.FindWithTag("Ball");
            if (b != null) ball = b;
        }
    }
    private void Update()
    {
        if (Input.GetKeyDown(KeyCode.R)) RunScan();
    }
    // Call this from the Console or wire to a keypress after you reproduce the problem
    public void RunScan()
    {
        StartCoroutine(RunScanCoroutine());
    }

    IEnumerator RunScanCoroutine()
    {
        if (ball == null)
        {
            Debug.LogWarning("[BallReferenceFinder] No ball assigned or found with tag 'Ball'.");
            yield break;
        }

        // small delay so the hit and any immediate resets can occur
        yield return new WaitForSeconds(scanDelay);

        Component[] all = FindObjectsByType<MonoBehaviour>(FindObjectsSortMode.None);
        var rb = ball.GetComponent<Rigidbody>();
        var matches = new List<string>();

        foreach (var comp in all)
        {
            if (comp == null) continue;
            Type t = comp.GetType();
            FieldInfo[] fields = t.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            foreach (var f in fields)
            {
                try
                {
                    object val = f.GetValue(comp);
                    if (val == null) continue;

                    // direct matches
                    if (val is GameObject go && go == ball)
                    {
                        matches.Add($"{comp.GetType().Name} (field {f.Name}) -> GameObject");
                    }
                    else if (val is Transform tr && tr.gameObject == ball)
                    {
                        matches.Add($"{comp.GetType().Name} (field {f.Name}) -> Transform");
                    }
                    else if (val is Rigidbody r && r == rb)
                    {
                        matches.Add($"{comp.GetType().Name} (field {f.Name}) -> Rigidbody");
                    }
                    else
                    {
                        // check collections (arrays, lists)
                        var enumerable = val as System.Collections.IEnumerable;
                        if (enumerable != null)
                        {
                            foreach (var item in enumerable)
                            {
                                if (item == null) continue;
                                if (item is GameObject igo && igo == ball)
                                {
                                    matches.Add($"{comp.GetType().Name} (field {f.Name}) -> GameObject in collection");
                                    break;
                                }
                                if (item is Transform itr && itr.gameObject == ball)
                                {
                                    matches.Add($"{comp.GetType().Name} (field {f.Name}) -> Transform in collection");
                                    break;
                                }
                                if (item is Rigidbody irb && irb == rb)
                                {
                                    matches.Add($"{comp.GetType().Name} (field {f.Name}) -> Rigidbody in collection");
                                    break;
                                }
                            }
                        }
                    }
                }
                catch (Exception)
                {
                    // ignore reflection errors for some Unity types
                }
            }
        }

        if (matches.Count == 0)
        {
            Debug.Log("[BallReferenceFinder] No direct field references to the ball found on active MonoBehaviours.");
            Debug.Log("[BallReferenceFinder] Next: search project for 'FindWithTag(\"Ball\")' or code that calls GetComponent<Rigidbody>() on found objects.");
        }
        else
        {
            Debug.Log("[BallReferenceFinder] Found references:");
            foreach (var s in matches) Debug.Log("  " + s);
        }
    }
}