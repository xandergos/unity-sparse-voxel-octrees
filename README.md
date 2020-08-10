# Unity Sparse Voxel Octrees

A new, currently in-development method of rendering voxels using Sparse Voxel Octrees as seen in [Nvidia's paper: "Efficient Sparse Voxel Octrees – Analysis, Extensions, and Implementation"](https://www.nvidia.com/docs/IO/88972/nvr-2010-001.pdf).

## Installation

Currently the only way to use the library is to download the files and compile them yourself in unity.

## Setup

The one requirement for making the scripts work is to have some GameObject with the "Shaders" script. Use this script to set the shaders to whatever you want, but make sure to only have one. Currently the recommended shaders are these:

- Unlit: TerrainRenderer (Currently only terrain rendering is supported)
- Clear: ClearTextures

## TODO

- Add another layer of abstraction on the rendering process. (High Priority)
- Parallelize and optimize the creation of ComputeBuffers from octrees. This includes allowing the creation of ComputeBuffers with a maximum depth. (High Priority)
- Implement Beam Optimization as described in the Nvidia paper on pg. 13. (Medium Priority)
- Replace ray sign checks with a coordinate system change, as described in the NVR paper on pg. 13. (Medium Priority)
- Eliminate the need for setting a GameObject to use the "Shaders" script. (Low Priority)
- Make Octree rendering work alongside Unity's normal rendering system. (Low Priority)
