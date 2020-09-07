using UnityEngine;

namespace SVO
{
    public struct RayHit
    {
        public Color Color;
        public Vector3 WorldPosition;
        public Vector3 OctreePosition;
        public Vector3 Normal;
        public Vector3 FaceNormal;
    }
}