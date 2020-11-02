#define POINTER_TYPE 0
#define VOXEL_TYPE 1

struct ray
{
    float3 origin;
    float3 direction;
};

struct ray_hit
{
    float4 color;
    float3 world_position;
    int shading_data_size;
    int shading_data_ptr;
};

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
ray_hit cast_ray(ray world_ray,
    float4x4 object_to_world,
    float4x4 world_to_object,
    StructuredBuffer<int> octree_primary_data,
    StructuredBuffer<int> octree_attrib_data)
{
    ray_hit failed_ray_hit;
    failed_ray_hit.world_position = float3(-1.f, -1.f, -1.f);

    ray ray = world_ray;
    ray.direction = mul(world_to_object, float4(ray.direction, 0));
    ray.direction = normalize(ray.direction);
    ray.origin = mul(world_to_object, float4(ray.origin, 1));
    // Calculations assume octree voxels are in [1, 2) but mesh is in [-.5, .5]. This corrects that.
    ray.origin += 1.5;
    
    static const int max_depth = 23;
    static const float epsilon = exp2(-max_depth);
    // Mirror coordinate system such that all ray direction components are negative.
    int sign_mask = 0;
    if(ray.direction.x > 0.f) sign_mask ^= 4, ray.origin.x = 3.f - ray.origin.x;
    if(ray.direction.y > 0.f) sign_mask ^= 2, ray.origin.y = 3.f - ray.origin.y;
    if(ray.direction.z > 0.f) sign_mask ^= 1, ray.origin.z = 3.f - ray.origin.z;

    ray.direction = -abs(ray.direction);
    
    // Get intersections of chunk (if hit)
    float3 root_min_distances = (2.f - ray.origin) / ray.direction;
    float3 root_max_distances = (1.f - ray.origin) / ray.direction;
    float root_tmin = max(max(max(root_min_distances.x, root_min_distances.y), root_min_distances.z), 0);
    float root_tmax = min(min(root_max_distances.x, root_max_distances.y), root_max_distances.z);
    
    if(root_tmax < 0 || root_tmin >= root_tmax)
        return failed_ray_hit;
    
    float3 next_path = clamp(ray.origin + ray.direction * root_tmin, 1.f, asfloat(0x3fffffff));
    
    int stack[max_depth + 1];
    stack[0] = 0;
    int stack_depth = 0;
    float3 stack_path = float3(1, 1, 1);
    
    do
    {
        // GET voxel at targetPos
        int differing_bits = asint(stack_path.x) ^ asint(next_path.x);
        differing_bits |= asint(stack_path.y) ^ asint(next_path.y);
        differing_bits |= asint(stack_path.z) ^ asint(next_path.z);
        int first_set = 23 - firstbithigh(differing_bits);
        int depth = min(first_set - 1, stack_depth);
        int ptr = stack[depth];
        int type = octree_primary_data[ptr].x >> 31 & 1;
        while(type == POINTER_TYPE)
        {
            ptr = octree_primary_data[ptr].x;
            depth++;
            int xm = asint(next_path.x) >> 23 - depth & 1;
            int ym = asint(next_path.y) >> 23 - depth & 1;
            int zm = asint(next_path.z) >> 23 - depth & 1;
            int child_index = (xm << 2) + (ym << 1) + zm;
            child_index ^= sign_mask;
            ptr += child_index;
            stack[depth] = ptr;
            type = octree_primary_data[ptr].x >> 31 & 1;
        }
        stack_depth = depth;
        stack_path = asfloat(asint(next_path) & ~((1 << 23 - depth) - 1)); // Remove unused bits
        
        // Return hit if voxel is solid
        if(type == VOXEL_TYPE && octree_primary_data[ptr] != 1 << 31)
        {
            ray_hit hit;

            int shading_ptr = octree_primary_data[ptr] & 0x7FFFFFFF;
            
            int color_data = octree_attrib_data[shading_ptr];
            int shading_data_size = (int)((uint)color_data >> 24);
            hit.shading_data_size = shading_data_size;
            hit.shading_data_ptr = shading_ptr + 1;
            hit.color = float4((color_data >> 16 & 0xFF) / 255.f, (color_data >> 8 & 0xFF) / 255.f, (color_data & 0xFF) / 255.f, 1.f);

            // Undo coordinate mirroring in next_path
            float3 mirrored_path = next_path;
            if(sign_mask >> 2 != 0) mirrored_path.x = 3.f - next_path.x;
            if(sign_mask >> 1 & 1 != 0) mirrored_path.y = 3.f - next_path.y;
            if(sign_mask & 1 != 0) mirrored_path.z = 3.f - next_path.z;
            hit.world_position = mul(object_to_world, mirrored_path - 1.5f);
            
            return hit;
        }

        // Step to the next voxel by moving along the normal on the far side of the voxel that was hit.
        float x_far = stack_path.x;
        float y_far = stack_path.y;
        float z_far = stack_path.z;
        float tx_max = (x_far - ray.origin.x) / ray.direction.x;
        float ty_max = (y_far - ray.origin.y) / ray.direction.y;
        float tz_max = (z_far - ray.origin.z) / ray.direction.z;
        float t_max = min(min(tx_max, ty_max), tz_max);
        next_path = clamp(ray.origin + ray.direction * t_max, stack_path, asfloat(asint(stack_path) + (1 << 23 - depth) - 1));

        if(tx_max <= t_max) next_path.x = x_far - epsilon;
        if(ty_max <= t_max) next_path.y = y_far - epsilon;
        if(tz_max <= t_max) next_path.z = z_far - epsilon;
    }
    while(all((asint(next_path) & 0xFF800000) == 0x3f800000)); // Same as 1 <= next_path < 2 

    return failed_ray_hit;
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
