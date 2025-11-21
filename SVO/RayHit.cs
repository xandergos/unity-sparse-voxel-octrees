using UnityEngine;

namespace SVO
{
    public struct RayHit
    { 
        public Color color;
        public Vector3 voxelObjPos;
        public float voxelObjSize;
        public Vector3 objPos;
        public Vector3 worldPos;
        public int attributesPtr;
        public Vector3 faceNormal;
    }
}