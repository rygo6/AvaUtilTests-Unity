using System.Collections;
using System.Diagnostics;
using UnityEngine;
using Debug = UnityEngine.Debug;

namespace GeoTetra.GTPerfTests
{
    public class InstantiateTestRunner : MonoBehaviour
    {
        [SerializeField] InstantiateTestGetComponent m_InstantiateTestGetComponentPrefab;
        [SerializeField] InstantiateTestDeserialize m_InstantiateTestDeserializePrefab;

        const int k_IterationCount = 1000;
        
        IEnumerator Start()
        {
            // just wait for anything running to settle
            // yield return new WaitForSeconds(1.0f);
            //
            // InstantiateDeserializePrefabs();

            // // just wait for anything running to settle
            yield return new WaitForSeconds(1.0f);
            
            InstantiateGetComponentPrefabs();
        }
        
        void InstantiateGetComponentPrefabs()
        {
            Stopwatch sw = new Stopwatch();
            sw.Start();
            for (int i = 0; i < k_IterationCount; ++i)
            {
                Instantiate(m_InstantiateTestGetComponentPrefab);
            }

            sw.Stop();
            
            Debug.Log($"GetComponent: {sw.ElapsedMilliseconds}");
        }
        
        void InstantiateDeserializePrefabs()
        {
            Stopwatch sw = new Stopwatch();
            sw.Start();
            for (int i = 0; i < k_IterationCount; ++i)
            {
                Instantiate(m_InstantiateTestDeserializePrefab);
            }

            sw.Stop();
            
            Debug.Log($"Deserialize {sw.ElapsedMilliseconds}");
        }
    }
}