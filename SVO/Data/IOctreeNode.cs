using System;
using UnityEngine;

namespace SVO
{
    public interface IOctreeNode: IDisposable
    {
        int GetTotalDataSize(bool recalculate);

        void InsertIntoArray(int[] arr, int initPos, int depth);
    }
}