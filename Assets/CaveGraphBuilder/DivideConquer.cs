using System.Collections.Generic;
using UnityEngine;

namespace CaveGraphBuilder
{
    public class DivideConquer
    {

        public void Generate()
        {
            
        }
        private static int CompareXZ(Vector3 a, Vector3 b)
        {
            int c = a.x.CompareTo(b.x);
            return c != 0 ? c : a.z.CompareTo(b.z);
        }

        private static void SortList(List<Vector3> list)
        {
            if (list.Count < 2) return;
            var buffer = new Vector3[list.Count];
            MergeSort(list, buffer, 0, list.Count);
        }

        // сортирует полуинтервал [lo, hi)
        private static void MergeSort(List<Vector3> a, Vector3[] buf, int lo, int hi)
        {
            if (hi - lo < 2) return;

            int mid = (lo + hi) / 2;
            MergeSort(a, buf, lo, mid);
            MergeSort(a, buf, mid, hi);

            int i = lo, j = mid, k = lo;
            while (i < mid && j < hi)
                buf[k++] = CompareXZ(a[j], a[i]) < 0 ? a[j++] : a[i++];
            while (i < mid) buf[k++] = a[i++];
            while (j < hi)  buf[k++] = a[j++];

            for (k = lo; k < hi; k++) a[k] = buf[k];
        }
    }
}
