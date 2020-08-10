using UnityEngine;

namespace World
{
    public abstract class Voxel: IOctreeNode
    {
        internal abstract OctreeBranch Split(OctreeBranch parent, int index);
        
        public int GetTotalDataSize(bool recalculate)
        {
            return 1;
        }

        public abstract void InsertIntoArray(int[] arr, int initPos, int depth);

        public virtual void Dispose() { }

        public abstract bool IsSolid();
    }
}