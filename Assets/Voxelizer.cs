using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
#if UNITY_2018_2_OR_NEWER
using UnityEngine.Rendering;
#else
using UnityEngine.Experimental.Rendering;
#endif

using System.Linq;

public class Voxelizer : MonoBehaviour 
{
    [SerializeField] Mesh mesh;
    [SerializeField] float voxelSize = 0.1f;
    [SerializeField] bool keepInside = true;

    Matrix4x4[][] voxelsMatrices;

    [SerializeField] Mesh instanceMesh;
    [SerializeField] Material instanceMaterial;

    public bool eachFrame = false;

    bool canRedo = false;

    UnityEngine.Voxelizer.Voxelizer voxelizer;

    void Start()
    {
        if (mesh == null) return;

        voxelizer = new UnityEngine.Voxelizer.Voxelizer();
        voxelizer.mesh = mesh;
        voxelizer.voxelsSize = voxelSize;

        voxelizer.finishedCallback += BuildMatrices;

        StartCoroutine( voxelizer.Voxelize() );
    }
    
    void Update()
    {   
        if (voxelsMatrices != null && instanceMesh != null)
        for (int i=0 ; i<voxelsMatrices.Length ; ++i)
            Graphics.DrawMeshInstanced(instanceMesh, 0, instanceMaterial, voxelsMatrices[i]);

        if (eachFrame && !voxelizer.processing)
        {
            voxelizer.voxelsSize = voxelSize;
            StartCoroutine(voxelizer.Voxelize());
        }
    }

    void BuildMatrices()
    {
        if (voxelizer == null) return;

        int maxCount = 1000;

        voxelsMatrices = new Matrix4x4[Mathf.CeilToInt(voxelizer.voxels.Count / maxCount)][];

        for (int i=0 ; i<voxelsMatrices.Length ; ++i)
        {
            int startIndex = i*maxCount;
            int count = Mathf.Min( maxCount, voxelizer.voxels.Count - startIndex );
            voxelsMatrices[i] = new Matrix4x4[ count ];
            for(int j=0 ; j<count ; ++j)
            {
                voxelsMatrices[i][j] = Matrix4x4.TRS(
                    voxelizer.voxels[startIndex+j],
                    Quaternion.identity,
                    Vector3.one * voxelSize
                    );
            }
        }
    }

    public bool IsValidVoxel(bool[,,] bools, int x, int y, int z, bool keepInside = true)
    {
        if (keepInside && bools[x, y, z]) return true;

        // If the current voxel is false, return false
        if (!bools[x, y, z]) return false;

        // If it's on the "border", return true
        if (x == 0 || y == 0 || z == 0 || x == (bools.GetLength(0)-1) || y == (bools.GetLength(1)-1) || z == (bools.GetLength(2)-1) ) return true;

        // If any of the surroundings is false, it is a border voxel, return true
        for (int dx = x-1; dx < (x+2); ++dx)
        {
            for (int dy = y-1; dy < (y+2); ++dy)
            {
                for (int dz = z-1; dz < (z+2); ++dz)
                {
                    if (!bools[dx, dy, dz]) return true;
                }
            }
        }

        return false;
    }
}
