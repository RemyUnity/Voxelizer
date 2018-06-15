using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class Voxelizer : MonoBehaviour 
{
    [SerializeField] Mesh mesh;
    [SerializeField] float voxelSize = 0.1f;
    [SerializeField] bool keepInside = true;

    Matrix4x4[][] voxelsMatrices;
    Mesh instanceMesh;
    Material defaultMaterial;

    int colorMask = 0; // 0 to 2 for r, g and b

	// Use this for initialization
	void Start () 
    {
		if (mesh == null)
        {
            MeshFilter meshFilter = GetComponent<MeshFilter>();
            if (meshFilter != null && meshFilter.sharedMesh != null) mesh = meshFilter.sharedMesh;
        }

        if (mesh != null)
        {
            Vector3[] voxels = Voxelize(mesh, voxelSize, keepInside);
            if (voxels.Length > 0)
            {
                int voxelsPerMatrix = 1000;

                voxelsMatrices = new Matrix4x4[ Mathf.CeilToInt( 1.0f * voxels.Length / voxelsPerMatrix ) ][];
                voxelsMatrices[0] = new Matrix4x4[voxelsPerMatrix];

                int i1=0;
                int i2=0;
                int v=0;

                while (v < voxels.Length)
                {
                    voxelsMatrices[i1][i2] = transform.localToWorldMatrix * Matrix4x4.TRS(voxels[v], Quaternion.identity, Vector3.one * voxelSize);

                    ++v;
                    ++i2;
                    if (i2 > ( voxelsPerMatrix-1) )
                    {
                        ++i1;
                        voxelsMatrices[i1] = new Matrix4x4[voxelsPerMatrix];
                        i2=0;
                    }
                }
                
                GameObject tmp = GameObject.CreatePrimitive(PrimitiveType.Cube);
                instanceMesh = tmp.GetComponent<MeshFilter>().sharedMesh;
                defaultMaterial = Object.Instantiate( tmp.GetComponent<MeshRenderer>().sharedMaterial );
                defaultMaterial.enableInstancing = true;
                Destroy(tmp);
            }
        }
	}
    void Update()
    {
        if (voxelsMatrices != null)
        for (int i=0 ; i<voxelsMatrices.Length ; ++i)
            Graphics.DrawMeshInstanced(instanceMesh, 0, defaultMaterial, voxelsMatrices[i]);
    }

    Vector3[] Voxelize( Mesh _mesh, float _voxelSize, bool keepInside = true)
    {
        if (_mesh == null) return null;

        Bounds bounds = mesh.bounds;
        Vector3 voxelsCountF = bounds.extents * 2f / _voxelSize;
        voxelsCountF.x = Mathf.CeilToInt(voxelsCountF.x);
        voxelsCountF.y = Mathf.CeilToInt(voxelsCountF.y);
        voxelsCountF.z = Mathf.CeilToInt(voxelsCountF.z);
        Vector3Int voxelsCount = new Vector3Int((int)voxelsCountF.x, (int)voxelsCountF.y, (int)voxelsCountF.z);

        Camera camera = new GameObject("VoxelizeCamera").AddComponent<Camera>();
        camera.transform.position = new Vector3(0f, -5f, 0f);
        camera.clearFlags = CameraClearFlags.Depth;
        camera.backgroundColor = Color.black;
        camera.orthographic = true;
        camera.orthographicSize = Mathf.Max(voxelsCount.x, voxelsCount.y) * _voxelSize * 0.5f;

        GameObject meshGO = new GameObject("Mesh2Voxelize");
        MeshFilter meshFilter = meshGO.AddComponent<MeshFilter>();
        meshFilter.mesh = _mesh;
        MeshRenderer meshRenderer = meshGO.AddComponent<MeshRenderer>();

        Material material = new Material(Shader.Find("Hidden/VoxelizeShader"));
        material.SetInt("_ColorMask", 8 );
        Material[] materials = new Material[mesh.subMeshCount];
        for (int i = 0; i < mesh.subMeshCount; ++i) materials[i] = material;

        meshRenderer.sharedMaterials = materials;

        meshGO.transform.parent = camera.transform;
        meshGO.transform.localPosition = Vector3.forward * ( voxelsCountF.z * _voxelSize * 0.5f + 1f );

        camera.nearClipPlane = 0.9f;
        camera.farClipPlane = voxelsCountF.z * _voxelSize + 2f;

        GameObject backgroundQuad = GameObject.CreatePrimitive(PrimitiveType.Quad);
        backgroundQuad.transform.parent = camera.transform;
        backgroundQuad.transform.localPosition = Vector3.forward * ( camera.farClipPlane - 0.01f);
        backgroundQuad.transform.localRotation = Quaternion.identity;
        backgroundQuad.transform.localScale = Vector3.one * camera.orthographicSize * 4f;
        backgroundQuad.GetComponent<Renderer>().sharedMaterial = material;

        RenderTexture renderTexture = new RenderTexture(voxelsCount.x, voxelsCount.y, 0, RenderTextureFormat.ARGB32);
        Texture2D texture = new Texture2D(voxelsCount.x, voxelsCount.y, TextureFormat.ARGB32, false);

        camera.targetTexture = renderTexture;
        RenderTexture previous = RenderTexture.active;
        RenderTexture.active = renderTexture;

        List<Vector3> voxels = new List<Vector3>();
        bool[,,] bools = new bool[ voxelsCount.x, voxelsCount.y, voxelsCount.z ];
        Color[] pixels;


        int prevColorMask = 0;
        for (int z = 0; z <= voxelsCount.z; ++z)
        {
            camera.nearClipPlane = 1f + (z + 0.5f) * _voxelSize;
                    
            camera.Render();

            texture.ReadPixels(new Rect(0, 0, voxelsCount.x, voxelsCount.y), 0, 0);

            pixels = texture.GetPixels();

            bool isValidVoxel = false;

            if (keepInside)
            {
                if (z<voxelsCount.z)
                    for (int x = 0; x < voxelsCount.x; ++x)
                    {
                        for (int y = 0; y < voxelsCount.y; ++y)
                        {
                            if ( pixels[x + y * voxelsCount.x][colorMask] > 0.5f )
                                voxels.Add(new Vector3(x, y, z) * _voxelSize - ( voxelsCountF - Vector3.one ) * _voxelSize * 0.5f);
                        }
                    } 
            }
            else
            {
                if (z > 0)
                {
                    for (int x = 0; x < voxelsCount.x; ++x)
                    {
                        for (int y = 0; y < voxelsCount.y; ++y)
                        {
                            isValidVoxel = pixels[x + y * voxelsCount.x][prevColorMask] > 0.5f;

                            if (isValidVoxel)
                                if ( !(x==0 || x==(voxelsCount.x-1) || y==0 || y==(voxelsCount.y-1) || z==1 || z==(voxelsCount.z)) ) // if not at the border
                                {
                                    isValidVoxel = false;

                                    for (int x1 = x-1 ; x1 < x+2 ; ++x1 )
                                        for (int y1 = y-1 ; y1 < y+2 ; ++y1 )
                                            for (int z1=0 ; z1<3 ; ++z1)
                                                isValidVoxel |= pixels[x1 + y1 * voxelsCount.x][z1] < 0.5f; // if any pixel around is "empty"
                                }
                            

                            /*
                            if (z == 9)
                                Debug.DrawLine(new Vector3(x-.5f, y-.5f, z-1.5f) * _voxelSize - ( voxelsCountF - Vector3.one ) * _voxelSize * 0.5f, new Vector3(x+.5f, y+.5f, z-.5f) * _voxelSize - ( voxelsCountF - Vector3.one ) * _voxelSize * 0.5f, pixels[x + y * voxelsCount.x], 5f );
                            */

                            if (isValidVoxel)
                                voxels.Add(new Vector3(x, y, z-1) * _voxelSize - ( voxelsCountF - Vector3.one ) * _voxelSize * 0.5f);
                        }
                    }
                }
            }
            
            prevColorMask = colorMask;
            colorMask = (colorMask+1)%3;
            material.SetInt("_ColorMask", 1 + ( 1 << ( 3 - colorMask ) ) );

            Debug.Log("ColorMask: "+material.GetInt("_ColorMask"));
        }

        RenderTexture.active = previous;

        /*
        for (int x = 0; x < voxelsCount.x; ++x)
        {
            for (int y = 0; y < voxelsCount.y; ++y)
            {
                for (int z = 0; z < voxelsCount.z; ++z)
                {
                    if ( IsValidVoxel(bools, x, y, z, keepInside) )
                        voxels.Add(new Vector3(x, y, z) * _voxelSize - ( voxelsCountF - Vector3.one ) * _voxelSize * 0.5f);
                }
            }
        }
        */

        Destroy(camera.gameObject);
        Destroy(texture);
        Destroy(renderTexture);
        Destroy(meshGO);
        Destroy(material);
        Destroy(backgroundQuad);

        return voxels.ToArray();
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
