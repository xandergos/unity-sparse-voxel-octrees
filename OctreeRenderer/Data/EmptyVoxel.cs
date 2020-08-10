using UnityEngine;

namespace World
{
    public class EmptyVoxel: Voxel
    {
        public readonly OctreeBranch Parent;
        
        public EmptyVoxel(OctreeBranch parent, int index)
        {
            Parent = parent;
        }

        internal override OctreeBranch Split(OctreeBranch parent, int index)
        {
            var branch = new OctreeBranch(parent, index);
            for(int i = 0; i < 8; i++)
                branch.Children[i] = new EmptyVoxel(branch, i);
            return branch;
        }

        public override void InsertIntoArray(int[] arr, int initPos, int depth)
        {
            arr[initPos] = 2 << 30;
        }

        public override bool IsSolid()
        {
            return false;
        }
    }
}