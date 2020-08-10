using System;
using UnityEngine;

namespace World
{
    public interface IOctreeNode: IDisposable
    {
        int GetTotalDataSize(bool recalculate);

        void InsertIntoArray(int[] arr, int initPos, int depth);
    }
}