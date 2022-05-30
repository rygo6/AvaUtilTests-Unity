using UnityEngine;
using UnityEngine.Splines;

public class SplinePool : MonoBehaviour
{
    [SerializeField] 
    SplineContainer m_SplineContainerPrefab;

    public SplineContainer CreateSpline(Vector3 startPoint, Quaternion startRotation)
    {
        var splineInstance = Instantiate(m_SplineContainerPrefab);
        splineInstance.Spline.Add(new BezierKnot(startPoint, 0, 0, startRotation));
        return splineInstance;
    }
}
