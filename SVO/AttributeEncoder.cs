using UnityEngine;

namespace SVO
{
    public static class AttributeEncoder
    {
        /// <summary>
        /// Encodes the shading data used by most standard octree shaders.
        /// </summary>
        /// <param name="normal">Normal to encode.</param>
        /// <returns>The shading data to be used.</returns>
        public static int[] EncodeStandardAttributes(Vector3 normal)
        {
            return new[] { EncodeNormal(normal) };
        }
        
        public static int EncodeNormal(Vector3 normal)
        {
            var encoded = 0;
            var maxAbsComp = Mathf.Max(Mathf.Max(Mathf.Abs(normal.x), Mathf.Abs(normal.y)), Mathf.Abs(normal.z));
            var cubicNormal = normal / maxAbsComp;
            var cubicNormalUnorm = cubicNormal * .5f + new Vector3(.5f, .5f, .5f);
            if (Mathf.Abs(normal.x) == maxAbsComp)
            {
                encoded |= ((System.Math.Sign(normal.x) + 1) / 2) << 22;
                encoded |= (int)(1023f * cubicNormalUnorm.y) << 10;
                encoded |= (int)(1023f * cubicNormalUnorm.z);
            }
            else if (Mathf.Abs(normal.y) == maxAbsComp)
            {
                encoded |= ((System.Math.Sign(normal.y) + 1) / 2) << 22;
                encoded |= 1 << 20;
                encoded |= (int)(1023f * cubicNormalUnorm.x) << 10;
                encoded |= (int)(1023f * cubicNormalUnorm.z);
            }
            else if (Mathf.Abs(normal.z) == maxAbsComp)
            {
                encoded |= ((System.Math.Sign(normal.z) + 1) / 2) << 22;
                encoded |= 2 << 20;
                encoded |= (int)(1023f * cubicNormalUnorm.x) << 10;
                encoded |= (int)(1023f * cubicNormalUnorm.y);
            }

            return encoded;
        }
    }
}