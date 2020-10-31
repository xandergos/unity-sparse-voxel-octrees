using UnityEngine;

namespace SVO
{
    public abstract class MeshToOctree: MonoBehaviour
    {
        public Mesh mesh;
        public int depth;
        public Material material;

        public abstract void Generate();
    }
}