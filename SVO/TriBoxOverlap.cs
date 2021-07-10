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
    public static class TriBoxOverlap
    {
        /// <summary>
        /// Performs an intersection test between a triangle and a box.
        /// 
        /// Code was initially retrieved from and ported to use unity classes.
        /// https://stackoverflow.com/questions/17458562/efficient-aabb-triangle-intersection-in-c-sharp
        ///
        /// Method from
        /// Tomas Akenine-Moller. (2001, March). Fast 3D Triangle-Box Overlap Testing.
        /// Retrieved from https://fileadmin.cs.lth.se/cs/Personal/Tomas_Akenine-Moller/code/tribox_tam.pdf
        /// </summary>
        /// <param name="box"></param>
        /// <param name="triangle"></param>
        /// <returns></returns>
        public static bool IsIntersecting(Bounds box, Vector3[] triangle)
        {
            float triangleMin, triangleMax;
            
            // Test the box normals (x, y and z axes)
            var boxNormals = new[] {
                new Vector3(1,0,0),
                new Vector3(0,1,0),
                new Vector3(0,0,1)
            };
            for (int i = 0; i < 3; i++)
            {
                Project(triangle, boxNormals[i], out triangleMin, out triangleMax);
                if (triangleMax < box.min[i] || triangleMin > box.max[i])
                    return false; // No intersection possible.
            }

            // Test the triangle normal
            var boxVertices = new Vector3[8];
            for(var i = 0; i < 8; i++)
                boxVertices[i] = box.min;
            for (var i = 0; i < 8; i++)
            {
                if ((i & 4) > 0) boxVertices[i].x += box.size.x;
                if ((i & 2) > 0) boxVertices[i].y += box.size.y;
                if ((i & 1) > 0) boxVertices[i].z += box.size.z;
            }
            var triangleNorm = Vector3.Cross(triangle[0] - triangle[1], triangle[2] - triangle[1]);
            float triangleOffset = Vector3.Dot(triangleNorm, triangle[0]);
            Project(boxVertices, triangleNorm, out var boxMin, out var boxMax);
            if (boxMax < triangleOffset || boxMin > triangleOffset)
                return false; // No intersection possible.

            // Test the nine edge cross-products
            Vector3[] triangleEdges = {
                triangle[0] - triangle[1],
                triangle[1] - triangle[2],
                triangle[2] - triangle[0]
            };
            for (var i = 0; i < 3; i++)
            {
                for (var j = 0; j < 3; j++)
                {
                    // The box normals are the same as it's edge tangents
                    var axis = Vector3.Cross(triangleEdges[i], boxNormals[j]);
                    Project(boxVertices, axis, out boxMin, out boxMax);
                    Project(triangle, axis, out triangleMin, out triangleMax);
                    if (boxMax < triangleMin || boxMin > triangleMax)
                        return false; // No intersection possible
                }
            }

            // No separating axis found.
            return true;
        }

        private static void Project(Vector3[] points, Vector3 axis, out float min, out float max)
        {
            min = float.PositiveInfinity;
            max = float.NegativeInfinity;
            foreach (var p in points)
            {
                var val = Vector3.Dot(axis, p);
                if (val < min) min = val;
                if (val > max) max = val;
            }
        }
    }
}