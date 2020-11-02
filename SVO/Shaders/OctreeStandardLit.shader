Shader "Octree/OctreeStandardLit"
{
    Properties
    {
        _Shininess ("Shininess", Range(0, 1))=1
    }
    SubShader
    {
        Tags { "RenderType"="Opaque" }

        Pass
        {
            ZWrite On
            Cull Front

            HLSLPROGRAM

            #include "UnityCG.cginc"
            #include "GeometryRayCast.hlsl"

            #pragma vertex vert
            #pragma fragment frag
            uniform StructuredBuffer<int> octree_primary_data;
            uniform StructuredBuffer<int> octree_attrib_data;
            uniform int initialized = 0;

            float _Shininess;

            struct v2f
            {
                float4 pos : SV_POSITION;
                float3 worldPos : TEXCOORD0;
                float3 worldScale : TEXCOORD1;
            };

            struct fragOut
            {
                half4 color : SV_Target0;
                float depth : SV_Depth;
            };
            
            v2f vert(appdata_base v)
            {
                v2f o;
                o.pos = UnityObjectToClipPos(v.vertex);
                o.worldPos = mul(unity_ObjectToWorld, v.vertex).xyz;
                
                float3 worldScale = float3(
                    length(unity_ObjectToWorld._m00_m10_m20),
                    length(unity_ObjectToWorld._m01_m11_m21),
                    length(unity_ObjectToWorld._m02_m12_m22)
                );
                o.worldScale = worldScale;
                
                return o;
            }
            
            fragOut frag(v2f i)
            {
                fragOut o;
                if(initialized == 0)
                {
                    clip(-1.f);
                    return o;
                }
                float3 camera_pos = unity_CameraToWorld._m03_m13_m23;
                ray ray;
                ray.direction = normalize(i.worldPos - camera_pos);
                ray.origin = camera_pos;

                ray_hit ray_hit = cast_ray(ray, unity_ObjectToWorld, unity_WorldToObject, octree_primary_data, octree_attrib_data);
                if(ray_hit.world_position.x != -1.f)
                {
                    float3 normal = decode_normal(octree_attrib_data[ray_hit.shading_data_ptr]);
                    normal = UnityObjectToWorldNormal(normal);
                    half light0Strength = max(0, dot(normal, _WorldSpaceLightPos0.xyz));

                    float3 world_view_dir = normalize(UnityWorldSpaceViewDir(ray_hit.world_position));
                    float3 world_refl = reflect(-world_view_dir, normal);
                    half3 spec = float3(1.f, 1.f, 1.f) * _Shininess * pow(max(0, dot(world_refl, _WorldSpaceLightPos0.xyz)), 32);
                    
                    o.color = ray_hit.color * light0Strength;
                    o.color.rgb += ShadeSH3Order(half4(normal, 1));
                    o.color.rgb += spec;
                    float4 clip_pos = mul(UNITY_MATRIX_VP, float4(ray_hit.world_position, 1.0));

                    o.depth = clip_pos.z / clip_pos.w;
                    #if defined(SHADER_API_GLCORE) || defined(SHADER_API_OPENGL) || \
                        defined(SHADER_API_GLES) ||  defined(SHADER_API_GLES3)
                        o.depth = (o.depth + 1.0) * 0.5;
                    #endif
                    
                    clip(1.f);
                    return o;
                }
                clip(-1.f);
                return o;
            }

            ENDHLSL
        }
    }
}