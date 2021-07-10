# Unity Sparse Voxel Octrees

A Unity-based method of rendering voxels using Sparse Voxel Octrees as seen in [Nvidia's paper: "Efficient Sparse Voxel Octrees – Analysis, Extensions, and Implementation"](https://www.nvidia.com/docs/IO/88972/nvr-2010-001.pdf). Project is still a WIP. When a stable or beta build is completed it will be found in the "releases" section.

## Installation

Currently the only way to use the library is to download the files and compile them yourself in unity.

## Quick Start

Place the SVO folder in your Unity Project. You must enable unsafe code: Project Settings > Player > Other Settings > Allow 'unsafe' Code.
To get a simple octree working, create a script:
- Create a Start method
- In the start method, create an octree with `new SVO.Octree()`
- Modify the octree however you want
- Set the texture of a material using an octree shader with `material.mainTexture = octree.Apply()`
A demo with code will be available soon.

## Limitations

- Deformations, such as vertex deformations commonly used to animate characters or other objects, are not implemented, and are not planned (If its even possible).
- No collider support. Unlike normal meshes, there is no built-in collider for representing an Octree. Any octree-based object will need to be represented using an approximate collider. This is not a huge deal, as an approximate collider is generally the more performant approach with traditional triangle-based meshes anyways. Theoretically, an exact octree collider could be made by recreating the octree using box colliders, but this would be memory intensive, and implementation would take time, so this is not planned.

## TODO

- When a branch is changed, optimize it as well as its parent.
