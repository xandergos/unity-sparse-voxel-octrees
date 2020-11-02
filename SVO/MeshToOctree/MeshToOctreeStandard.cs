using System;
using System.Collections.Generic;
using UnityEngine;

namespace SVO
{
    /// <summary>
    /// Utility class for converting a mesh to an octree: Uses standard attributes given by AttributeEncoder.
    /// </summary>
    public class MeshToOctreeStandard: MeshToOctree
    {
        
        private readonly List<Vector3> _normals = new List<Vector3>();
        private readonly List<Vector2> _uvs = new List<Vector2>();
        private Texture2D _mainTexture;

        protected override void OnFillSubmesh(int submesh)
        {
            mesh.GetNormals(_normals);
            mesh.GetUVs(0, _uvs);
            _mainTexture = (Texture2D) material.mainTexture ?? Texture2D.whiteTexture;
        }

        protected override Tuple<Color, int[]> GenerateAttributes(Vector3[] triangleVertices, int[] indices, 
            Bounds voxelLocalBounds, Bounds voxelMeshBounds, float octreeSize, Vector3 octreeCenter)
        {
            var barycentric = MathO.ToBarycentricCoordinates(voxelMeshBounds.center, triangleVertices[0],
                triangleVertices[1], triangleVertices[2]);
            var interpolatedUV = barycentric.x * _uvs[indices[0]] + barycentric.y * _uvs[indices[1]] + barycentric.z * _uvs[indices[2]];
            var interpolatedNormal = barycentric.x * _normals[indices[0]] + barycentric.y * _normals[indices[1]] + barycentric.z * _normals[indices[2]];
            interpolatedNormal.Normalize();

            var color = _mainTexture.GetPixelBilinear(interpolatedUV.x, interpolatedUV.y);
            return new Tuple<Color, int[]>(color, AttributeEncoder.EncodeStandardAttributes(interpolatedNormal));
        }
    }
}