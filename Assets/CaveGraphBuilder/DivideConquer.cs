using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace CaveGraphBuilder
{
    public class DivideConquer
    {
        public List<int>[] neighbors;
        
        
        private List<Vector3> nodes;

        public DivideConquer(List<Vector3> nodes)
        {
            this.nodes = nodes;
            neighbors = new List<int>[nodes.Count];
            for (int i = 0; i < nodes.Count; i++)
                neighbors[i] = new List<int>();
        }

        public List<(int a, int b)> Generate()
        {
            SortList(nodes);
            
            Triangulate(0, nodes.Count);

            return FilterGabriel(GetEdges());
        }
        private int CompareXZ(Vector3 a, Vector3 b)
        {
            int c = a.x.CompareTo(b.x);
            return c != 0 ? c : a.z.CompareTo(b.z);
        }

        public List<(int a, int b)> GetEdges()
        {
            var result = new List<(int a, int b)>();

            for (int a = 0; a < neighbors.Length; a++)
            {
                foreach (int b in neighbors[a])
                {
                    if (a < b)
                        result.Add((a, b));
                }
            }

            return result;
        }

        private void SortList(List<Vector3> list)
        {
            if (list.Count < 2) return;
            var buffer = new Vector3[list.Count];
            MergeSort(list, buffer, 0, list.Count);
        }

        // сортирует полуинтервал [lo, hi)
        private void MergeSort(List<Vector3> a, Vector3[] buf, int lo, int hi)
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

        private void AddEdge(int a, int b)
        {
            neighbors[a].Add(b);
            neighbors[b].Add(a);
        }

        private void RemoveEdge(int a, int b)
        {
            neighbors[a].Remove(b);
            neighbors[b].Remove(a);
        }

        private double Orient2D(int a, int b, int c)
        {
            double abX = (double)nodes[b].x - nodes[a].x;
            double abZ = (double)nodes[b].z - nodes[a].z;

            double acX = (double)nodes[c].x - nodes[a].x;
            double acZ = (double)nodes[c].z - nodes[a].z;

            return abX * acZ - abZ * acX;
        }
        private bool InCircle(int a, int b, int c, int d)
        {
            double orient = Orient2D(a, b, c);
            if (orient == 0) return false;

            double ax = (double)nodes[a].x - nodes[d].x;
            double ay = (double)nodes[a].z - nodes[d].z;
            double bx = (double)nodes[b].x - nodes[d].x;
            double by = (double)nodes[b].z - nodes[d].z;
            double cx = (double)nodes[c].x - nodes[d].x;
            double cy = (double)nodes[c].z - nodes[d].z;

            double aL = ax * ax + ay * ay;
            double bL = bx * bx + by * by;
            double cL = cx * cx + cy * cy;

            double det =
                ax * (by * cL - bL * cy)
                - ay * (bx * cL - bL * cx)
                + aL * (bx * cy - by * cx);

            return det * Math.Sign(orient) > 0;
        }

        private void Triangulate(int lo, int hi) // [lo, hi)
        {
            int n = hi - lo;
            
            if (n < 2) return;
            
            if (n == 2)
            {
                int a = lo, b = lo + 1;
                
                AddEdge(a, b);
                
                return;
            }

            if (n == 3)
            {
                
                int a = lo, b = lo + 1, c = lo + 2;
                
                AddEdge(a, b);
                AddEdge(b, c);
                if (Orient2D(a, b, c) != 0) 
                    AddEdge(c, a);
                return;

            }
            
            int mid = (lo + hi) / 2;
            Triangulate(lo, mid);
            Triangulate(mid, hi);

            Merge(lo, mid, hi);
        }

        public void Merge(int lo, int mid, int hi)
        {
            var (l, r) = FindLowerTangent(mid);
            AddEdge(l, r);

            int guard = 0;
            while (true)
            {
                if (++guard > (hi - lo) * 3)
                    throw new Exception("Merge: зацикливание");

                // правый кандидат
                int rc = NextCW(r, l);
                if (Orient2D(l, r, rc) > 0)
                {
                    while (true)
                    {
                        int next = NextCW(r, rc);   // до RemoveEdge
                        if (Orient2D(l, r, next) > 0 && InCircle(l, r, rc, next))
                        {
                            RemoveEdge(r, rc);
                            rc = next;
                        }
                        else break;
                    }
                }
                else rc = -1;

                // левый кандидат: то же самое зеркально
                int lc = NextCCW(l, r);
                if (Orient2D(l, r, lc) > 0)
                {
                    while (true)
                    {
                        int next = NextCCW(l, lc);
                        if (Orient2D(l, r, next) > 0 && InCircle(l, r, lc, next))
                        {
                            RemoveEdge(l, lc);
                            lc = next;
                        }
                        else break;
                    }
                }
                else lc = -1;

                // выбор
                if (rc == -1 && lc == -1) break;

                bool takeRight;
                if (lc == -1) takeRight = true;
                else if (rc == -1) takeRight = false;
                else takeRight = !InCircle(l, r, rc, lc);

                if (takeRight) { AddEdge(l, rc); r = rc; }
                else           { AddEdge(lc, r); l = lc; }
            }
        }

        public int NextCCW(int v, int u)
        {
            var best = u;
            double bestDelta = double.PositiveInfinity;
            var angleU = Angle(v, u);

            foreach (var w in neighbors[v])
            {
                if (w == u)
                    continue;
                
                var delta = Angle(v, w) - angleU;
                while (delta <= 0)
                {
                    delta += 2 * Math.PI;
                }
                if (delta < bestDelta)
                {
                    bestDelta = delta;
                    best = w;
                }
            }

            return best;
        }
        
        public int NextCW(int v, int u)
        {
            var best = u;
            double bestDelta = double.NegativeInfinity;
            var angleU = Angle(v, u);

            foreach (var w in neighbors[v])
            {
                if (w == u)
                    continue;
                
                var delta = Angle(v, w) - angleU;
                while (delta <= 0)
                {
                    delta += 2 * Math.PI;
                }
                if (delta > bestDelta)
                {
                    bestDelta = delta;
                    best = w;
                }
                
            }

            return best;
        }

        public double Angle(int v, int u)
        {
            return Math.Atan2(nodes[u].z - nodes[v].z, nodes[u].x - nodes[v].x);
        }

        private (int l, int r) FindLowerTangent(int mid)
        {
            int l = mid - 1;
            int r = mid;
            bool moved;
            int guard = 0;

            do
            {
                moved = false;

                foreach (int p in neighbors[l])
                {
                    if (Orient2D(l, r, p) < 0)
                    {
                        l = p;
                        moved = true;
                        break;
                    }
                }

                foreach (int q in neighbors[r])
                {
                    if (Orient2D(l, r, q) < 0)
                    {
                        r = q;
                        moved = true;
                        break;
                    }
                }

                if (++guard > nodes.Count * 2)
                    throw new System.Exception("FindLowerTangent: зацикливание");

            } while (moved);

            return (l, r);
        }

        public List<(int a, int b)> FilterGabriel(List<(int a, int b)> edges)
        {
            var result = new List<(int a, int b)>();

            foreach (var (a, b) in edges)
            {
                bool ok = true;

                foreach (int w in neighbors[a].Intersect(neighbors[b]))
                {
                    if (Dot(a, b, w) < 0)
                    {
                        ok = false;
                        break;
                    }
                }

                if (ok)
                    result.Add((a, b));
            }

            return result;
        }

        private double Dot(int a, int b, int c)
        {
            double ax = (double)nodes[a].x - nodes[c].x;
            double az = (double)nodes[a].z - nodes[c].z;
            double bx = (double)nodes[b].x - nodes[c].x;
            double bz = (double)nodes[b].z - nodes[c].z;
            return ax * bx + az * bz;
        }
    }
}
