struct aabb
{
    float3 center;
    float radius;
    float inv_radius;
};

struct ray
{
    float3 origin;
    float3 dir;
    float3 inv_dir;
};

float max(float3 v)
{
    return max(max(v.x, v.y), v.z);
}

float min(float3 v)
{
    return min(min(v.x, v.y), v.z);
}