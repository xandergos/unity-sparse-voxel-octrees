using System;
using UnityEngine;

namespace SVO
{
    public class Shaders : MonoBehaviour
    {
        public static ComputeShader Unlit;
        public static ComputeShader Clear;
        public ComputeShader MUnlit;
        public ComputeShader MClear;

        private void Start()
        {
            Unlit = MUnlit;
            Clear = MClear;
        }
    }
}