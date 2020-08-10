using System;
using System.Threading;
using JetBrains.Annotations;
using UnityEngine;

namespace World
{
    public class OctreeBranch: IOctreeNode
    {
        [CanBeNull] public readonly OctreeBranch Parent;

        internal readonly IOctreeNode[] Children;
        public readonly byte Index;
        private int _cachedDataSize = -1;
        
        internal OctreeBranch([CanBeNull] OctreeBranch parent, int index)
        {
            Parent = parent;
            Index = (byte)index;
            
            Children = new IOctreeNode[] {
                new EmptyVoxel(this, 0),
                new EmptyVoxel(this, 1),
                new EmptyVoxel(this, 2),
                new EmptyVoxel(this, 3),
                new EmptyVoxel(this, 4),
                new EmptyVoxel(this, 5),
                new EmptyVoxel(this, 6),
                new EmptyVoxel(this, 7)
            };
        }
        
        public Voxel GetVoxel(Vector3 normPos, out int depth)
        {
            int d = 0;
    
            int xpath = BitConverter.ToInt32(BitConverter.GetBytes(normPos.x), 0);
            int ypath = BitConverter.ToInt32(BitConverter.GetBytes(normPos.y), 0);
            int zpath = BitConverter.ToInt32(BitConverter.GetBytes(normPos.z), 0);
    
            IOctreeNode node = this;
    
            while(node is OctreeBranch) {
                d++;
                int childIndex = (((xpath >> (23 - d)) & 1) << 2) + (((ypath >> (23 - d)) & 1) << 1) + ((zpath >> (23 - d)) & 1);

                node = Children[childIndex];
            }

            depth = d;
            return (Voxel)node;
        }

        public ColoredVoxel SetVoxel(Vector3 normPos, int depth, Color color)
        {
            var posX = normPos.x >= 1.5;
            var posY = normPos.y >= 1.5;
            var posZ = normPos.z >= 1.5;
            var index = (Convert.ToByte(posX) << 2) + (Convert.ToByte(posY) << 1) + Convert.ToByte(posZ);
            
            if (depth == 1)
            {
                Children[index].Dispose();
                var v = new ColoredVoxel(color, this, index);
                Children[index] = v;
                
                return v;
            }

            var next = Children[index];
            if (next is Voxel voxel)
            {
                Children[index] = voxel.Split(this, index);
                voxel.Dispose();
                next = Children[index];
            }
            var subIndex = new Vector3(
                posX ? (normPos.x - 1.5f) * 2f + 1 : (normPos.x - 1) * 2f + 1, 
                posY ? (normPos.y - 1.5f) * 2f + 1 : (normPos.y - 1) * 2f + 1, 
                posZ ? (normPos.z - 1.5f) * 2f + 1 : (normPos.z - 1) * 2f + 1);
            return ((OctreeBranch) next).SetVoxel(subIndex, depth - 1, color);
        }

        bool Optimizable()
        {
            for(int i = 0; i < 7; i++)
            {
                if (Children[i] == Children[i + 1]) return false;
            }

            return true;
        }

        /**
         * Calculates the size of this tree, as well as the size of all children.
         */
        public int GetTotalDataSize(bool recalculate)
        {
            if (recalculate)
            {
                _cachedDataSize = 1;
                foreach (var child in Children)
                {
                    _cachedDataSize += child.GetTotalDataSize(true);
                }
            }

            return _cachedDataSize;
        }
        
        public void InsertIntoArray(int[] arr, int initPos, int depth)
        {
            arr[initPos] = _cachedDataSize;
            var p = initPos + 1;
            for(int i = 0; i < 8; i++)
            {
                Children[i].InsertIntoArray(arr, p, depth + 1);
                p += Children[i].GetTotalDataSize(false);
            }
        }

        public void Dispose()
        {
            foreach (var child in Children)
                child.Dispose();
            GC.SuppressFinalize(this);
        }
    }
}