using UnityEngine;

namespace SVO
{
    public static class MathO
    {
        public static Vector3 ToBarycentricCoordinates(Vector3 point, Vector3 p1, Vector3 p2, Vector3 p3)
        {
            return new Vector3(
                CalculateTriangleArea(point, p2, p3), 
                CalculateTriangleArea(p1, point, p3), 
                CalculateTriangleArea(p1, p2, point)) / CalculateTriangleArea(p1, p2, p3);
        }

        public static float CalculateTriangleArea(Vector3 p1, Vector3 p2, Vector3 p3)
        {
            return Vector3.Cross(p2 - p1, p3 - p1).magnitude / 2;
        }
    }
}