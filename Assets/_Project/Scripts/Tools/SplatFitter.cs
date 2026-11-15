using System;
using System.Collections.Generic;
using UnityEngine;

namespace Bangkku.Furniture
{
    public static class SplatFitter
    {
        // positions는 방향을 맞춘 스플랫 중심 좌표이며, targetMeters는 X/Y/Z 치수입니다.
        public static bool TryFit(IReadOnlyList<Vector3> positions, Vector3 targetMeters,
            out Vector3 measuredSize, out float scale, out float fitError)
        {
            measuredSize = default;
            scale = 1f;
            fitError = 0f;
            if (positions == null || positions.Count < 2 || !Positive(targetMeters)) return false;

            var x = new float[positions.Count];
            var y = new float[positions.Count];
            var z = new float[positions.Count];
            for (int i = 0; i < positions.Count; i++)
            {
                var p = positions[i];
                if (!float.IsFinite(p.x) || !float.IsFinite(p.y) || !float.IsFinite(p.z)) return false;
                x[i] = p.x;
                y[i] = p.y;
                z[i] = p.z;
            }

            int trim = positions.Count / 100;
            var bounds = new Vector3(TrimmedSpan(x, trim), TrimmedSpan(y, trim), TrimmedSpan(z, trim));
            if (!Positive(bounds)) return false;
            var ratios = new[] { targetMeters.x / bounds.x, targetMeters.y / bounds.y, targetMeters.z / bounds.z };
            Array.Sort(ratios);
            var fitted = bounds * ratios[1];
            if (!Positive(fitted)) return false;
            measuredSize = bounds;
            scale = ratios[1];
            fitError = Mathf.Max(Mathf.Abs(fitted.x / targetMeters.x - 1f),
                Mathf.Abs(fitted.y / targetMeters.y - 1f), Mathf.Abs(fitted.z / targetMeters.z - 1f));
            if (fitError > 0.05f) Debug.LogWarning($"[방꾸] 스플랫 치수 보정 오차 {fitError:P1}");
            return true;
        }

        static float TrimmedSpan(float[] values, int trim)
        {
            Array.Sort(values);
            return values[values.Length - 1 - trim] - values[trim];
        }

        static bool Positive(Vector3 v) =>
            float.IsFinite(v.x) && v.x > 0 && float.IsFinite(v.y) && v.y > 0 && float.IsFinite(v.z) && v.z > 0;
    }
}
