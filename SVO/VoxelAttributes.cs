using System;
using UnityEngine;

namespace SVO
{
    public static class VoxelAttribute
    {
        public static int[] Encode(Color color, Vector3 normal)
        {
            int[] data = new int[2];
            data[0] |= 0b1100_0000 << 24;
            data[0] |= EncodeColor(color);
            data[1] = EncodeNormal(normal);
            return data;
        }
        
        private static int EncodeNormal(Vector3 normal)
        {
            var encoded = 0;
            var maxAbsComp = Mathf.Max(Mathf.Max(Mathf.Abs(normal.x), Mathf.Abs(normal.y)), Mathf.Abs(normal.z));
            var cubicNormal = normal / maxAbsComp;
            var cubicNormalUnorm = cubicNormal * .5f + new Vector3(.5f, .5f, .5f);
            if (Mathf.Abs(normal.x) == maxAbsComp)
            {
                encoded |= ((Math.Sign(normal.x) + 1) / 2) << 22;
                encoded |= (int)(1023f * cubicNormalUnorm.y) << 10;
                encoded |= (int)(1023f * cubicNormalUnorm.z);
            }
            else if (Mathf.Abs(normal.y) == maxAbsComp)
            {
                encoded |= ((Math.Sign(normal.y) + 1) / 2) << 22;
                encoded |= 1 << 20;
                encoded |= (int)(1023f * cubicNormalUnorm.x) << 10;
                encoded |= (int)(1023f * cubicNormalUnorm.z);
            }
            else if (Mathf.Abs(normal.z) == maxAbsComp)
            {
                encoded |= ((Math.Sign(normal.z) + 1) / 2) << 22;
                encoded |= 2 << 20;
                encoded |= (int)(1023f * cubicNormalUnorm.x) << 10;
                encoded |= (int)(1023f * cubicNormalUnorm.y);
            }

            return encoded;
        }

        private static int EncodeColor(Color color)
        {
            var encoded = 0;
            encoded |= (int)(color.r * 255) << 16;
            encoded |= (int)(color.g * 255) << 8;
            encoded |= (int)(color.b * 255) << 0;
            return encoded;
        }
    }
}