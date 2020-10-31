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
        public override void Generate()
        {
            var mainTexture = (Texture2D)material.mainTexture ?? Texture2D.whiteTexture;
            var data = gameObject.AddComponent<OctreeData>();
            for (int submesh = 0; submesh < mesh.subMeshCount; submesh++)
            {
                var indices = mesh.GetIndices(submesh);
                var vertices = new List<Vector3>();
                var normals = new List<Vector3>();
                var uvs = new List<Vector2>();
                mesh.GetVertices(vertices);
                mesh.GetNormals(normals);
                mesh.GetUVs(0, uvs);
                
                var triVerts = new Vector3[3];

                for (var i = 0; i < indices.Length; i += 3)
                {
                    var index0 = indices[i];
                    var index1 = indices[i + 1];
                    var index2 = indices[i + 2];
                    triVerts[0] = vertices[index0];
                    triVerts[1] = vertices[index1];
                    triVerts[2] = vertices[index2];
                    var iCopy = i;
                    Tuple<Color, int[]> GenerateAttributes(Bounds bounds)
                    {
                        var barycentric = MathO.ToBarycentricCoordinates(bounds.center, triVerts[0], 
                            triVerts[1], triVerts[2]);
                        var interpolatedUV = barycentric.x * uvs[index0] 
                                             + barycentric.y * uvs[index1] 
                                             + barycentric.z * uvs[index2];
                        var interpolatedNormal = barycentric.x * normals[index0] 
                                                 + barycentric.y * normals[index1] 
                                                 + barycentric.z * normals[index2];
                        interpolatedNormal.Normalize();

                        var color = mainTexture.GetPixelBilinear(interpolatedUV.x, interpolatedUV.y);
                        return new Tuple<Color, int[]>(color, AttributeEncoder.EncodeStandardAttributes(interpolatedNormal));
                    }
                    data.FillTriangle(triVerts, depth, GenerateAttributes);
                }
            }
        }
    }
}