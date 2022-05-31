using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Splines;

namespace GeoTetra.GTDoppel
{
    public class SplinePool : MonoBehaviour
    {
        [SerializeField] 
        DoppelSpline m_SplineContainerPrefab;

        List<DoppelSpline> m_Splines = new();
        
        public DoppelSpline CreateSpline(Vector3 startPoint, Quaternion startRotation)
        {
            var splineInstance = Instantiate(m_SplineContainerPrefab);
            splineInstance.Container.Spline.Add(new BezierKnot(startPoint, 0, 0, startRotation));
            m_Splines.Add(splineInstance);
            return splineInstance;
        }
    }
}