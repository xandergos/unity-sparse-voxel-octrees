/*
 *  Unity Sparse Voxel Octrees
 *  Copyright (C) 2021  Alexander Goslin
 *
 *  This program is free software: you can redistribute it and/or modify
 *  it under the terms of the GNU General Public License as published by
 *  the Free Software Foundation, either version 3 of the License, or
 *  (at your option) any later version.
 *
 *  This program is distributed in the hope that it will be useful,
 *  but WITHOUT ANY WARRANTY; without even the implied warranty of
 *  MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
 *  GNU General Public License for more details.
 *
 *  You should have received a copy of the GNU General Public License
 *  along with this program.  If not, see <https://www.gnu.org/licenses/>.
 */

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