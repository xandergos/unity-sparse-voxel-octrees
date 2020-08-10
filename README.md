# Unity Sparse Voxel Octrees

## Installation

Currently the only way to use the library is to download the files and compile them yourself in unity.

## Setup

The one requirement for making the scripts work is to have some GameObject with the "Shaders" script. Use this script to set the shaders to whatever you want, but make sure to only have one. Currently the recommended shaders are these:

- Unlit: TerrainRenderer (Currently only terrain rendering is supported)
- Clear: ClearTextures
