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

Shader "Octree/OctreeStandardLit"
{
    Properties
    {
        _Shininess ("Shininess", Range(0, 1))=1
        [MainTexture] [NoScaleOffset] _Volume ("Volume", 3D) = "" {}
    }
    SubShader
    {
        Tags { "Queue"="Transparent" }
        
        Cull Front

        Pass
        {
            Tags { "RenderType"="Transparent" }
            
            ZWrite On

            HLSLPROGRAM

            #include "UnityCG.cginc"
            #include "GeometryRayCast.hlsl"

            #pragma vertex vert
            #pragma fragment frag
            
            float _Shininess;
            Texture3D<int> _Volume;

            struct v2f
            {
                float4 pos : SV_POSITION;
                float3 world_pos : TEXCOORD0;
            };

            struct frag_out
            {
                half4 color : SV_Target0;
                float depth : SV_Depth;
            };
            
            v2f vert(appdata_base v)
            {
                v2f o;
                o.pos = UnityObjectToClipPos(v.vertex);
                o.world_pos = mul(unity_ObjectToWorld, v.vertex).xyz;
                
                return o;
            }
            
            frag_out frag(v2f i)
            {
                frag_out o;
                
                int structure_depth = get_structure_depth(_Volume);
                if(structure_depth <= 0 || structure_depth >= 2048)
                {
                    clip(-1);
                    return o;
                }
                
                float3 camera_pos = unity_CameraToWorld._m03_m13_m23;
                ray ray;
                if(UNITY_MATRIX_P[3][3] == 1.f)
                {
                    float3 cam_dir = unity_CameraToWorld._m02_m12_m22;
                    ray.origin = i.world_pos - cam_dir * 100;
                    ray.dir = cam_dir;
                }
                else
                {
                    ray.dir = normalize(i.world_pos - camera_pos);
                    ray.origin = camera_pos;
                }
                float4 color;
                float3 world_pos;
                int shading_data_ptr;
                if(cast_ray(ray, unity_ObjectToWorld, unity_WorldToObject, _Volume, color, world_pos, shading_data_ptr))
                {
                    float3 normal = decode_normal(sample_attrib(_Volume, shading_data_ptr, get_structure_depth(_Volume)));
                    normal = UnityObjectToWorldNormal(normal);
                    half light0Strength = max(0, dot(normal, _WorldSpaceLightPos0.xyz));

                    float3 world_view_dir = normalize(UnityWorldSpaceViewDir(world_pos));
                    float3 world_refl = reflect(-world_view_dir, normal);
                    half3 spec = float3(1.f, 1.f, 1.f) * _Shininess * pow(max(0, dot(world_refl, _WorldSpaceLightPos0.xyz)), 32);
                    
                    o.color = color * light0Strength;
                    o.color.rgb += ShadeSH3Order(half4(normal, 1));
                    o.color.rgb += spec;
                    float4 clip_pos = UnityWorldToClipPos(world_pos);

                    o.depth = clip_pos.z / clip_pos.w;
                    #if defined(SHADER_API_GLCORE) || defined(SHADER_API_OPENGL) || \
                        defined(SHADER_API_GLES) || defined(SHADER_API_GLES3)
                        o.depth = (o.depth + 1.0) * 0.5;
                    #endif
                    
                    return o;
                }
                clip(-1.f);
                return o;
            }

            ENDHLSL
        }
        
        Pass
        {
            Tags {"LightMode"="ShadowCaster"}
            
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_shadowcaster
            #include "UnityCG.cginc"
            #include "GeometryRayCast.hlsl"

            Texture3D<int> _Volume;
            
            struct v2f
            {
                V2F_SHADOW_CASTER;
                float3 world_pos : TEXCOORD1;
            };

            v2f vert(appdata_base v)
            {
                v2f o;
                o.pos = UnityObjectToClipPos(v.vertex);
                o.world_pos = mul(unity_ObjectToWorld, v.vertex).xyz;
                
                return o;
            }

            void frag(v2f i, out float4 out_color : SV_Target, out float out_depth : SV_Depth)
            {
                int structure_depth = get_structure_depth(_Volume);
                if(structure_depth <= 0 || structure_depth >= 2048)
                {
                    clip(-1);
                    return;
                }
                
                ray ray;
                float3 light_dir = normalize(-UNITY_MATRIX_V[2].xyz);
                ray.origin = i.world_pos - light_dir * 50;
                ray.dir = light_dir;

                float4 color;
                float3 world_pos;
                int shading_data_ptr;
                if(!cast_ray(ray, unity_ObjectToWorld, unity_WorldToObject, _Volume, color, world_pos, shading_data_ptr))
                {
                    clip(-1);
                    return;
                }

                float4 shadow_pos = UnityWorldToClipPos(world_pos);

                out_color = out_depth = shadow_pos.z / shadow_pos.w;
                #if defined(SHADER_API_GLCORE) || defined(SHADER_API_OPENGL) || \
                    defined(SHADER_API_GLES) || defined(SHADER_API_GLES3)
                    outColor = outDepth = (outDepth + 1.0) * 0.5;
                #endif
            }
            ENDCG
        }
    }
}