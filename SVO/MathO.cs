using UnityEngine;

namespace SVO
{
    public static class MathO
    {
        public static Vector3 ToBarycentricCoordinates(Vector3 point, Vector3 p1, Vector3 p2, Vector3 p3)
        {
            var triNormal = Vector3.Cross(p2 - p1, p3 - p1);
            var triNormalMag = triNormal.magnitude;
            var triNormalNormalized = triNormal / triNormalMag;
            
            var projectedPoint = point - triNormalNormalized * Vector3.Dot(triNormalNormalized, point);
            
            var triArea = CalculateTriangleArea(p1, p2, p3);
            var b0 = CalculateTriangleArea(projectedPoint, p2, p3) / triArea;
            var b1 = CalculateTriangleArea(p1, projectedPoint, p3) / triArea;
            var b2 = CalculateTriangleArea(p1, p2, projectedPoint) / triArea;
            return new Vector3(b0, b1, b2);
        }

        public static float CalculateTriangleArea(Vector3 p1, Vector3 p2, Vector3 p3)
        {
            return Vector3.Cross(p2 - p1, p3 - p1).magnitude / 2;
        }
    }
}