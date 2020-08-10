using System;
using System.Diagnostics.Contracts;
using UnityEngine;

namespace World
{
    public class Caster
    {
        public static Matrix4x4 CameraInverseProjection;
        public static Matrix4x4 CameraToWorld;
        public static Vector3 OctreeScale;
        public static Vector3 OctreePos;

        public static Ray CreateRay(Vector2 uv)
        {
            Vector3 origin = CameraToWorld * new Vector4(0.0f, 0.0f, 0.0f, 1.0f);
            origin += OctreeScale;
            origin -= OctreePos;
            origin.Scale(new Vector3(1f / OctreeScale.x, 1f / OctreeScale.y, 1f / OctreeScale.z));
            Vector3 direction = CameraInverseProjection * new Vector4(uv.x, uv.y, 0.0f, 1.0f);
    
            direction = CameraToWorld * new Vector4(direction.x, direction.y, direction.z, 0.0f);
            direction.Normalize();

            Ray ray = new Ray();
            ray.origin = origin;
            ray.direction = direction;
    
            return ray;
        }
        
         public static Color CastRay(int[] chunkData, Ray ray)
        {
            const int maxDepth = 23;
            float epsilon = Mathf.Pow(2f, -maxDepth);
            
            Vector3Int rayDirSign = new Vector3Int(Math.Sign(ray.direction.x), Math.Sign(ray.direction.y), Math.Sign(ray.direction.z));
            
            // Get intersections of chunk (if hit)
            float root_tmin = Mathf.Max(Mathf.Max(Mathf.Max(
                (1.5f - .5f * rayDirSign.x - ray.origin.x) / ray.direction.x,
                (1.5f - .5f * rayDirSign.y - ray.origin.y) / ray.direction.y),
                (1.5f - .5f * rayDirSign.z - ray.origin.z) / ray.direction.z), 0f);
            float root_tmax = Mathf.Min(Mathf.Min(
                (1.5f + .5f * rayDirSign.x - ray.origin.x) / ray.direction.x,
                (1.5f + .5f * rayDirSign.y - ray.origin.y) / ray.direction.y),
                (1.5f + .5f * rayDirSign.z - ray.origin.z) / ray.direction.z
            );
            Vector3 root_mid = ray.origin + ray.direction * (.5f * (root_tmin + root_tmax));
            
            int[] stack = new int[maxDepth + 1];
            stack[0] = 0;
            int stackDepth = 0;
            Vector3 stackPath = new Vector3();
            Vector3 targetPath = new Vector3();
            targetPath.x = Mathf.Clamp(ray.origin.x + ray.direction.x * root_tmin, 1f, 2f);
            targetPath.y = Mathf.Clamp(ray.origin.y + ray.direction.y * root_tmin, 1f, 2f);
            targetPath.z = Mathf.Clamp(ray.origin.z + ray.direction.z * root_tmin, 1f, 2f);
            
            if(root_tmax < 0 || Mathf.Abs(root_mid.x - 1.5f) > .5 || 
               Mathf.Abs(root_mid.y - 1.5f) > .5 || Mathf.Abs(root_mid.z - 1.5f) > .5) return Color.black;
            
            do
            {
                // GET voxel at targetPos
                int differingBits = AsInt(stackPath.x) ^ AsInt(targetPath.x);
                differingBits |= AsInt(stackPath.y) ^ AsInt(targetPath.y);
                differingBits |= AsInt(stackPath.z) ^ AsInt(targetPath.z);
                differingBits &= 0x007fffff;
                int firstSet = 23 - ((AsInt(differingBits) >> 23) - 127);
                int depth = Mathf.Min(firstSet - 1, stackDepth);
                int ptr = stack[depth];
                int type = (chunkData[ptr] >> 30) & 3;
                while(type == 0)
                {
                    depth++;
                    int xm = (AsInt(targetPath.x) >> (23 - depth)) & 1;
                    int ym = (AsInt(targetPath.y) >> (23 - depth)) & 1;
                    int zm = (AsInt(targetPath.z) >> (23 - depth)) & 1;
                    int childId = (xm << 2) + (ym << 1) + zm;
                    ptr++;
                    for(int i = 0; i < childId; i++) ptr += chunkData[ptr] >> 30 == 0 ? chunkData[ptr] : 1;
                    stack[depth] = ptr;
                    type = (chunkData[ptr] >> 30) & 3;
                }
                stackDepth = depth;
                stackPath = new Vector3();
                stackPath.x = AsFloat((AsInt(targetPath.x) >> (23 - stackDepth)) << (23 - stackDepth));
                stackPath.y = AsFloat((AsInt(targetPath.y) >> (23 - stackDepth)) << (23 - stackDepth));
                stackPath.z = AsFloat((AsInt(targetPath.z) >> (23 - stackDepth)) << (23 - stackDepth));
                
                // if voxel is solid DRAW 
                if(type == 1) 
                {
                    int colorRGB = chunkData[ptr];
                    Color color = new Color(((colorRGB >> 16) & 0xFF) / 255f, ((colorRGB >> 8) & 0xFF) / 255f, (colorRGB & 0xFF) / 255f, 1f);
                    return color;
                }
                
                // STEP to next voxel
                float x_far = rayDirSign.x == 1 ? AsFloat(AsInt(stackPath.x) + (1 << (23 - stackDepth))) : stackPath.x;
                float y_far = rayDirSign.y == 1 ? AsFloat(AsInt(stackPath.y) + (1 << (23 - stackDepth))) : stackPath.y;
                float z_far = rayDirSign.z == 1 ? AsFloat(AsInt(stackPath.z) + (1 << (23 - stackDepth))) : stackPath.z;
                float tx_max = Mathf.Max((x_far - ray.origin.x) / ray.direction.x, 0);
                float ty_max = Mathf.Max((y_far - ray.origin.y) / ray.direction.y, 0);
                float tz_max = Mathf.Max((z_far - ray.origin.z) / ray.direction.z, 0);
                float t_max = tx_max;
                
                if(ty_max < t_max) {
                    if(tz_max < ty_max) {
                        t_max = tz_max;
                        targetPath = ray.origin + ray.direction * t_max;
                        if(rayDirSign.z == 1) targetPath.z = z_far;
                        else targetPath.z = z_far - epsilon;
                    }
                    else {
                        t_max = ty_max;
                        targetPath = ray.origin + ray.direction * t_max;
                        if(rayDirSign.y == 1) targetPath.y = y_far;
                        else targetPath.y = y_far - epsilon;
                    }
                }
                else if(tz_max < t_max) {
                    t_max = tz_max;
                    targetPath = ray.origin + ray.direction * t_max;
                    if(rayDirSign.z == 1) targetPath.z = z_far;
                    else targetPath.z = z_far - epsilon;
                }
                else {
                    t_max = tx_max;
                    targetPath = ray.origin + ray.direction * t_max;
                    if(rayDirSign.x == 1) targetPath.x = x_far;
                    else targetPath.x = x_far - epsilon;
                }
            }
            while(targetPath.x >= 1f && targetPath.x < 2f && 
                  targetPath.y >= 1f && targetPath.y < 2f && 
                  targetPath.z >= 1f && targetPath.z < 2f);

            return Color.black;
        }

         [Pure]
         private static int AsInt(float f)
         {
             return System.BitConverter.ToInt32(System.BitConverter.GetBytes(f), 0);
         }

         [Pure]
         private static float AsFloat(int i)
         {
             return System.BitConverter.ToSingle(System.BitConverter.GetBytes(i), 0);
         }
    }
}