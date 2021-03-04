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

#define GET_TYPE(data) data >> 31 & 1;
#define GET_SHADING_PTR(data) data & 0x7FFFFFFF;
#define IS_EMPTY_VOXEL(data) (data == (1 << 31));

#include "Util.hlsl"

int get_structure_depth(Texture3D<int> tex)
{
    uint x, y, z;
    tex.GetDimensions(x, y, z);
    return tex[uint3(x - 1, y - 1, z - 1)];
}

int sample_structure(Texture3D<int> tex, uint idx)
{
    uint z = idx >> 22;
    uint y = idx >> 11 & 2047;
    uint x = idx & 2047;
    return tex[uint3(x, y, z)];
}

int sample_attrib(Texture3D<int> tex, uint idx, uint structure_depth)
{
    uint z = (idx >> 22) + structure_depth;
    uint y = idx >> 11 & 2047;
    uint x = idx & 2047;
    return tex[uint3(x, y, z)];
}

/* Casts a ray into an octree.
 * Returns a ray_hit.
 * If a voxel is hit, the ray_hit will have data describing the hit,
 * otherwise, ray_hit will have a position of float3(-1, -1, -1).
 * ray_hit normals and position are both in world pos.
 *
 * The mesh (in object space) of the octree should always have center (0, 0, 0) and size (1, 1, 1)
 * 
 * Note: ray.direction does not have to be normalized!
 */
bool cast_ray(ray world_ray,
    float4x4 object_to_world,
    float4x4 world_to_object,
    Texture3D<int> volume,
    out float4 color,
    out float3 world_pos,
    out int shading_data_ptr)
{
    const int structure_depth = get_structure_depth(volume);

    ray ray = world_ray;
    ray.dir = mul(world_to_object, float4(ray.dir, 0));
    ray.dir = normalize(ray.dir);
    ray.origin = mul(world_to_object, float4(ray.origin, 1));
    // Calculations assume octree voxels are in [1, 2) but object is in [-.5, .5]. This corrects that.
    ray.origin += 1.5;
     
    static const int max_depth = 23;
    static const float epsilon = exp2(-max_depth);
    // Mirror coordinate system such that all ray direction components are negative.
    int sign_mask = 0;
    if(ray.dir.x > 0.f) sign_mask ^= 4, ray.origin.x = 3.f - ray.origin.x;
    if(ray.dir.y > 0.f) sign_mask ^= 2, ray.origin.y = 3.f - ray.origin.y;
    if(ray.dir.z > 0.f) sign_mask ^= 1, ray.origin.z = 3.f - ray.origin.z;

    ray.dir = -abs(ray.dir);
    ray.inv_dir = -abs(ray.inv_dir);
    
    // Get intersections of chunk (if hit)
    float3 root_min_distances = (2.f - ray.origin) * ray.inv_dir;
    float3 root_max_distances = (1.f - ray.origin) * ray.inv_dir;
    float root_tmin = max(max(max(root_min_distances.x, root_min_distances.y), root_min_distances.z), 0);
    float root_tmax = min(min(root_max_distances.x, root_max_distances.y), root_max_distances.z);
    
    if(root_tmax < 0 || root_tmin >= root_tmax) return false;
    
    float3 next_path = clamp(ray.origin + ray.dir * root_tmin, 1.f, asfloat(0x3fffffff));
    
    int stack[max_depth + 1];
    stack[0] = 0;
    int stack_depth = 0;
    float3 stack_path = float3(1, 1, 1);

    int i = 0;
    do
    {
        i++;
        // GET voxel at targetPos
        int differing_bits = asint(stack_path.x) ^ asint(next_path.x);
        differing_bits |= asint(stack_path.y) ^ asint(next_path.y);
        differing_bits |= asint(stack_path.z) ^ asint(next_path.z);
        const int first_set = 23 - firstbithigh(differing_bits);
        int depth = min(first_set - 1, stack_depth);
        int ptr = stack[depth];
        int type = GET_TYPE(sample_structure(volume, ptr));
        while(type == POINTER_TYPE)
        {
            ptr = sample_structure(volume, ptr);
            depth++;
            const int xm = (asint(next_path.x) >> 23 - depth) & 1; // 1 or 0 for sign of movement in x direction
            const int ym = (asint(next_path.y) >> 23 - depth) & 1; // 1 or 0 for sign of movement in y direction
            const int zm = (asint(next_path.z) >> 23 - depth) & 1; // 1 or 0 for sign of movement in z direction
            int child_index = (xm << 2) + (ym << 1) + zm;
            child_index ^= sign_mask;
            ptr += child_index;
            stack[depth] = ptr;
            type = GET_TYPE(sample_structure(volume, ptr));
        }
        stack_depth = depth;
        stack_path = asfloat(asint(next_path) & ~((1 << 23 - depth) - 1)); // Remove unused bits
        
        // Return hit if voxel is solid
        bool isEmpty = IS_EMPTY_VOXEL(sample_structure(volume, ptr));
        if(type == VOXEL_TYPE && !isEmpty)
        {
            const int shading_ptr = GET_SHADING_PTR(sample_structure(volume, ptr));

            const int color_data = sample_attrib(volume, shading_ptr, structure_depth);
            shading_data_ptr = shading_ptr + 1;
            color = float4((color_data >> 16 & 0xFF) / 255.f, (color_data >> 8 & 0xFF) / 255.f, (color_data & 0xFF) / 255.f, (color_data >> 24 & 0xFF) / 255.f);

            // Undo coordinate mirroring in next_path
            float3 mirrored_path = next_path;
            if(sign_mask >> 2 != 0) mirrored_path.x = 3.f - next_path.x;
            if(sign_mask >> 1 & 1 != 0) mirrored_path.y = 3.f - next_path.y;
            if(sign_mask & 1 != 0) mirrored_path.z = 3.f - next_path.z;
            world_pos = mul(object_to_world, float4(mirrored_path - 1.5f, 1.f));
            
            return true;
        }

        // Step to the next voxel by moving along the normal on the far side of the voxel that was hit.
        float3 t_max = (stack_path - ray.origin) * ray.inv_dir;
        float min_t_max = min(t_max);
        next_path = clamp(ray.origin + ray.dir * min_t_max, stack_path, asfloat(asint(stack_path) + (1 << 23 - depth) - 1));

        if(t_max.x <= min_t_max) next_path.x = stack_path.x - epsilon;
        if(t_max.y <= min_t_max) next_path.y = stack_path.y - epsilon;
        if(t_max.z <= min_t_max) next_path.z = stack_path.z - epsilon;
    }
    while(all((asint(next_path) & 0xFF800000) == 0x3f800000) && i <= 250); // Same as 1 <= next_path < 2 

    return false;
}

float3 decode_normal(int encoded_normal)
{
    int sign_bit = encoded_normal >> 22 & 1;
    int axis = encoded_normal >> 20 & 3;
    int comp2 = encoded_normal >> 10 & 0x3FF;
    int comp1 = encoded_normal & 0x3FF;

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
    default: break; // Uh oh, normal has invalid axis
    }
    normal = normalize(normal);
    return normal;
}
