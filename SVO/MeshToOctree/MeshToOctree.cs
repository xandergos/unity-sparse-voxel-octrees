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

using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace SVO
{
    public abstract class MeshToOctree: MonoBehaviour
    {
        public Mesh mesh;
        public float voxelSize;
        public Material material;
        public void Generate()
        {
            var idealBounds = FindIdealOctreeBounds();   

            var depth = Mathf.RoundToInt(Mathf.Log(idealBounds.size.x / voxelSize, 2));

            if (depth > 12)
                throw new NotSupportedException("Octree voxel size is too small. Please split the mesh.");

            var octreeData = new Octree();
            for (var submesh = 0; submesh < mesh.subMeshCount; submesh++)
            {
                FillSubmesh(octreeData, depth, submesh, idealBounds.size.x, idealBounds.center);
            }
            AssetDatabase.CreateAsset(octreeData.Apply(true), "Assets/mesh.asset");
        }

        private void FillSubmesh(Octree data, int depth, int submesh, float octreeSize, Vector3 octreeCenter)
        {
            OnFillSubmesh(submesh);
            
            var indices = mesh.GetIndices(submesh);
            var triIndices = new int[3];
            var vertices = new List<Vector3>();
            mesh.GetVertices(vertices);
                
            var triVerts = new Vector3[3];
            var triVertsMesh = new Vector3[3];

            var octreeSizeInv = 1 / octreeSize;

            for (var i = 0; i < indices.Length; i += 3)
            {
                triIndices[0] = indices[i];
                triIndices[1] = indices[i + 1];
                triIndices[2] = indices[i + 2];
                triVerts[0] = (vertices[triIndices[0]] - octreeCenter) * octreeSizeInv;
                triVerts[1] = (vertices[triIndices[1]] - octreeCenter) * octreeSizeInv;
                triVerts[2] = (vertices[triIndices[2]] - octreeCenter) * octreeSizeInv;
                triVertsMesh[0] = vertices[triIndices[0]];
                triVertsMesh[1] = vertices[triIndices[1]];
                triVertsMesh[2] = vertices[triIndices[2]];
                Tuple<Color, int[]> InternalGenerateAttributes(Bounds bounds)
                {
                    var transformedCenter = bounds.center * octreeSize + octreeCenter;
                    return GenerateAttributes(triVertsMesh, triIndices, bounds,
                        new Bounds(transformedCenter, Vector3.one * voxelSize),
                        octreeSize, octreeCenter);
                }
                data.FillTriangle(triVerts, depth, InternalGenerateAttributes);
            }
        }

        protected abstract void OnFillSubmesh(int submesh);

        protected abstract Tuple<Color, int[]> GenerateAttributes(Vector3[] triangleVertices, int[] indices,
            Bounds voxelLocalBounds, Bounds voxelMeshBounds, float octreeSize, Vector3 octreeCenter);
        
        private Bounds FindIdealOctreeBounds()
        {
            // octreePos = center of mesh bounds aligned to grid of size voxelSize
            // Alignment to grid makes it easier to align with other octrees of same voxelSize.
            var octreePos = new Vector3();
            for(var i = 0; i < 3; i++) 
                octreePos[i] = Mathf.Round(mesh.bounds.center[i] / voxelSize) * voxelSize;
            
            // octreeSize = Smallest octree size that still encapsulates entire mesh
            var octreeSize = -1f;
            for (var i = 0; i < 3; i++)
            {
                octreeSize = Mathf.Max(octreeSize, Mathf.Abs(mesh.bounds.max[i] - octreePos[i]));
                octreeSize = Mathf.Max(octreeSize, Mathf.Abs(mesh.bounds.min[i] - octreePos[i]));
            }

            octreeSize *= 2;
            
            // Make octreeSize the smallest number for which voxelSize * 2^n exists for some natural number n
            // Makes sure that a voxel of voxelSize can actually be made.
            octreeSize = Mathf.NextPowerOfTwo(Mathf.CeilToInt(octreeSize / voxelSize)) * voxelSize;
            
            return new Bounds(octreePos, Vector3.one * octreeSize);
        }
    }
}