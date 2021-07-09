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

#define POINTER_TYPE 0
#define VOXEL_TYPE 1

#define GET_TYPE(structure_data) (structure_data >> 31 & 1)
#define GET_ATTRIBUTES_HEAD_PTR(structure_data) (structure_data & 0x7FFFFFFF)
#define IS_EMPTY_VOXEL(structure_data) (structure_data == (1 << 31))

#include "Util.hlsl"

int sample_data(Texture3D<int> tex, uint idx)
{
    uint z = idx >> 16;
    uint y = idx >> 8 & 0xFF;
    uint x = idx & 0xFF;
    return tex[uint3(x, y, z)];
}

/* Casts a ray into an octree.
 * If a voxel is hit, will return true and output variables will be set.
 * otherwise, false will be returned and output variables will be undefined.
 *
 * All inputs and outputs are in object space.
 * 
 * Note: ray.direction does not have to be normalized.
 */
bool cast_ray(ray ray,
    Texture3D<int> volume,
    out float4 color,
    out float3 hit_voxel_pos,
    out float hit_voxel_size,
    out float3 hit_pos,
    out int attributes_ptr,
    out float3 face_normal,
    out int loops)
{
    // Calculations assume octree is in [1, 2) but object is in [-.5, .5]. This corrects that.
    ray.origin += 1.5;
     
    static const int max_depth = 23;
    static const float epsilon = exp2(-max_depth);
    
    // Mirror coordinate system such that all ray direction components are negative.
    int sign_mask = 0;
    if(ray.dir.x > 0.f) sign_mask ^= 4, ray.origin.x = 3.f - ray.origin.x;
    if(ray.dir.y > 0.f) sign_mask ^= 2, ray.origin.y = 3.f - ray.origin.y;
    if(ray.dir.z > 0.f) sign_mask ^= 1, ray.origin.z = 3.f - ray.origin.z;

    ray.dir = -abs(ray.dir);
    ray.inv_dir = -abs(1 / ray.dir);
    
    // Get intersections of octree. Near distance >= far distance when not hit.
    const float3 root_near_plane_distances = (2.f - ray.origin) * ray.inv_dir;
    const float root_near_distance = max(max(max(root_near_plane_distances.x, root_near_plane_distances.y), root_near_plane_distances.z), 0);

    // We can assume the octree is hit, because otherwise the fragment shader would never be called.
    // const float3 root_far_plane_distances = (1.f - ray.origin) * ray.inv_dir;
    // const float root_far_distance = min(min(root_far_plane_distances.x, root_far_plane_distances.y), root_far_plane_distances.z);
    // if(root_far_distance < 0 || root_near_distance >= root_far_distance) return false;

    // Get the face of the octree we initially move through
    // TODO: Figure out why the sign mask has to be checked here, should be done once a voxel is hit,
    // but that only seems to work on voxel with depth > 0
    if(root_near_distance == root_near_plane_distances.x)
    {
        face_normal = float3(1, 0, 0);
        if(sign_mask >> 2 != 0)
            face_normal.x = -face_normal.x;
    }
    else if(root_near_distance == root_near_plane_distances.y)
    {
        face_normal = float3(0, 1, 0);
        if(sign_mask >> 1 & 1 != 0)
            face_normal.y = -face_normal.y;
    }
    else
    {
        face_normal = float3(0, 0, 1);
        if(sign_mask & 1 != 0)
            face_normal.z = -face_normal.z;
    }

    // Path of next voxel to move to
    float3 next_path = clamp(ray.origin + ray.dir * root_near_distance, 1.f, asfloat(0x3fffffff));
    
    int stack[max_depth + 1];
    stack[0] = 0;
    int stack_depth = 0;
    float3 stack_path = float3(1, 1, 1);
    loops = 0;
    do
    {
        loops++;
        
        // Recall deepest shared branch
        int differing_bits = asint(stack_path.x) ^ asint(next_path.x);
        differing_bits |= asint(stack_path.y) ^ asint(next_path.y);
        differing_bits |= asint(stack_path.z) ^ asint(next_path.z);
        const int first_set = 23 - firstbithigh(differing_bits);
        int depth = min(first_set - 1, stack_depth);
        int ptr = stack[depth];
        
        // Step down to next voxel
        int data = sample_data(volume, ptr);
        int type = GET_TYPE(data);
        while(type == POINTER_TYPE)
        {
            ptr = data;
            depth++;
            const int xm = (asint(next_path.x) >> 23 - depth) & 1; // 1 or 0 for sign of movement in x direction
            const int ym = (asint(next_path.y) >> 23 - depth) & 1; // 1 or 0 for sign of movement in y direction
            const int zm = (asint(next_path.z) >> 23 - depth) & 1; // 1 or 0 for sign of movement in z direction
            int child_index = (xm << 2) + (ym << 1) + zm;
            child_index ^= sign_mask;
            ptr += child_index;
            stack[depth] = ptr;
            data = sample_data(volume, ptr); // Follow ptr
            type = GET_TYPE(data);
        }
        stack_depth = depth;
        stack_path = asfloat(asint(next_path) & ~((1 << 23 - depth) - 1)); // Remove unused bits
        
        // Return hit if voxel is solid
        if(type == VOXEL_TYPE && !IS_EMPTY_VOXEL(data))
        {
            const int attributes_head_ptr = GET_ATTRIBUTES_HEAD_PTR(data);

            const int color_data = sample_data(volume, attributes_head_ptr);
            attributes_ptr = attributes_head_ptr + 1;
            color = float4((color_data >> 16 & 0xFF) / 255.f, (color_data >> 8 & 0xFF) / 255.f, (color_data & 0xFF) / 255.f, (color_data >> 24 & 0xFF) / 255.f);

            // Undo coordinate mirroring
            float3 mirrored_path = next_path;
            hit_voxel_size = exp2(-depth);
            if(sign_mask >> 2 != 0)
            {
                face_normal.x = -face_normal.x;
                mirrored_path.x = 3.f - next_path.x;
            }
            if(sign_mask >> 1 & 1 != 0)
            {
                face_normal.y = -face_normal.y;
                mirrored_path.y = 3.f - next_path.y;
            }
            if(sign_mask & 1 != 0)
            {
                face_normal.z = -face_normal.z;
                mirrored_path.z = 3.f - next_path.z;
            }
            hit_pos = mirrored_path - 1.5f;
            hit_voxel_pos = asfloat(asint(mirrored_path) & ~((1 << 23 - depth) - 1)) - 1.5f;
            
            return true;
        }

        // Step to the next voxel by moving along the normal on the far side of the voxel that was hit.
        const float3 t_max = (stack_path - ray.origin) * ray.inv_dir;
        const float min_t_max = min(t_max);
        next_path = clamp(ray.origin + ray.dir * min_t_max, stack_path, asfloat(asint(stack_path) + (1 << 23 - depth) - 1));

        // Update next path to adjacent voxel and set face normal        
        if(t_max.x == min_t_max)
        {
            face_normal = float3(1, 0, 0);
            next_path.x = stack_path.x - epsilon;
        }
        else if(t_max.y == min_t_max)
        {
            face_normal = float3(0, 1, 0);
            next_path.y = stack_path.y - epsilon;
        }
        else
        {
            face_normal = float3(0, 0, 1);
            next_path.z = stack_path.z - epsilon;
        }
    }
    while(all(next_path >= 1 && next_path < 2) &&
        loops <= 1000);

    return false;
}

float3 decode_normal(int encoded_normal)
{
    const int sign_bit = encoded_normal >> 22 & 1;
    const int axis = encoded_normal >> 20 & 3;
    const int comp2 = encoded_normal >> 10 & 0x3FF;
    const int comp1 = encoded_normal & 0x3FF;

    float3 normal = float3(0, 0, 0);
    switch(axis)
    {
    case 0:
        normal.x = sign_bit * 2.f - 1;
        normal.y = comp2 / 1023.f * 2.f - 1;
        normal.z = comp1 / 1023.f * 2.f - 1;
        break;
    case 1:
        normal.x = comp2 / 1023.f * 2.f - 1;
        normal.y = sign_bit * 2.f - 1;
        normal.z = comp1 / 1023.f * 2.f - 1;
        break;
    case 2:
        normal.x = comp2 / 1023.f * 2.f - 1;
        normal.y = comp1 / 1023.f * 2.f - 1;
        normal.z = sign_bit * 2.f - 1;
        break;
    default: break; // normal has invalid axis
    }
    normal = normalize(normal);
    return normal;
}
