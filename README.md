# Unity Sparse Voxel Octrees

A Unity-based method of rendering voxels using Sparse Voxel Octrees as seen in [Nvidia's paper: "Efficient Sparse Voxel Octrees – Analysis, Extensions, and Implementation"](https://www.nvidia.com/docs/IO/88972/nvr-2010-001.pdf).

## Installation

Currently the only way to use the library is to download the files and compile them yourself in unity.

## Quick Start

Place the SVO folder in your Unity Project. You must enable unsafe code: Project Settings > Player > Other Settings > Allow 'unsafe' Code.
To get a simple octree working, create a script:
- Create a Start method
- In the start method, create an octree with `new SVO.Octree()`
- Modify the octree however you want
- Set the texture of a material using an octree shader with `material.mainTexture = octree.Apply()`

A demo with code is available [here](https://github.com/BudgetToaster/unity-svo-demo).

## Screenshot

This image contains voxels from the minimum size of 2^-23 up to approximately 2^-8. At this scale there are a lot of artifacts due to floating point errors, but in practice (when working at reasonable scales) this will not happen.

![image](https://user-images.githubusercontent.com/28935064/132603244-ad48f9f3-82f7-41aa-afe5-546eeec427d0.png)

## Limitations

- Deformations, such as vertex deformations commonly used to animate characters or other objects, are not implemented, and are not planned (If its even possible).
- No collider support. Unlike normal meshes, there is no built-in collider for representing an Octree. Any octree-based object will need to be represented using an approximate collider. This is not a huge deal, as an approximate collider is generally the more performant approach with traditional triangle-based meshes anyways. Theoretically, an exact octree collider could be made by recreating the octree using box colliders, but this would be memory intensive and slow, and implementation would take time, so this is not planned.
- No shadows. Someone with experience developing shaders in Unity could resolve this, but as of now I do not have the time to add them in. PRs are welcome.
