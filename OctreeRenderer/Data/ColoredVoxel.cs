using System;
using UnityEngine;

namespace World
{
    public class ColoredVoxel: Voxel
    {
        public readonly Color Color;
        public readonly OctreeBranch Parent;

        internal ColoredVoxel(Color color, OctreeBranch parent, int index)
        {
            Parent = parent;
            Color = color;
        }

        internal override OctreeBranch Split(OctreeBranch parent, int index)
        {
            var branch = new OctreeBranch(parent, index);
            for(int i = 0; i < 8; i++)
                branch.Children[i] = new ColoredVoxel(Color, branch, i);
            return branch;
        }
        
        public override void InsertIntoArray(int[] arr, int initPos, int depth)
        {
            var rgb = ((int) (Color.r * 255) << 16) + ((int) (Color.g * 255) << 8) + (int) (Color.b * 255);
            arr[initPos] = (1 << 30) | rgb;
        }

        public override void Dispose()
        {
            GC.SuppressFinalize(this);
        }

        public override bool IsSolid()
        {
            return true;
        }
    }
}