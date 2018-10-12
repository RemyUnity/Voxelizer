using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
#if UNITY_2018_2_OR_NEWER
using UnityEngine.Rendering;
#else
using UnityEngine.Experimental.Rendering;
#endif

public class Voxelizer : MonoBehaviour 
{
    [SerializeField] Mesh mesh;
    [SerializeField] float voxelSize = 0.1f;
    [SerializeField] bool keepInside = true;

    public Bounds debugBounds;

    Matrix4x4[][] voxelsMatrices;
    [SerializeField] Mesh instanceMesh;
    [SerializeField] Material instanceMaterial;

    int colorMask = 0; // 0 to 2 for r, g and b

    public bool useComputeShader = true;
    public ComputeShader computeShader;

    public bool eachFrame = false;

    List<Vector3> voxels;

    bool canRedo = false;

    IEnumerator Start()
    {
        yield return DoStuff();
    }
    
	// Use this for initialization
    IEnumerator DoStuff ()
    {
        canRedo = false;
        
		if (mesh == null)
        {
            MeshFilter meshFilter = GetComponent<MeshFilter>();
            if (meshFilter != null && meshFilter.sharedMesh != null) mesh = meshFilter.sharedMesh;
        }

        if (mesh != null)
        {
            yield return Voxelize(mesh, voxelSize, keepInside);
            
            if (voxels.Count > 0)
            {
                int voxelsPerMatrix = 1000;

                voxelsMatrices = new Matrix4x4[ Mathf.CeilToInt( 1.0f * voxels.Count / voxelsPerMatrix ) ][];
                voxelsMatrices[0] = new Matrix4x4[voxelsPerMatrix];

                int i1=0;
                int i2=0;
                int v=0;

                while (v < voxels.Count)
                {
                    voxelsMatrices[i1][i2] = /* transform.localToWorldMatrix * */ Matrix4x4.TRS(voxels[v], Quaternion.identity, Vector3.one * voxelSize);

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

                if (instanceMesh == null)
                {
                    instanceMesh = tmp.GetComponent<MeshFilter>().sharedMesh;
                }

                Renderer rndr = GetComponent<Renderer>();
                if (rndr!=null && instanceMaterial == null) instanceMaterial = rndr.sharedMaterial;
                if (instanceMaterial == null)
                {
                    instanceMaterial = Object.Instantiate( tmp.GetComponent<MeshRenderer>().sharedMaterial );
                    instanceMaterial.enableInstancing = true;
                }

                Destroy(tmp);
            }
        }

        canRedo = true;
    }
    
    void Update()
    {
        if (eachFrame && canRedo) StartCoroutine(DoStuff());
        
        if (voxelsMatrices != null && instanceMesh != null)
        for (int i=0 ; i<voxelsMatrices.Length ; ++i)
            Graphics.DrawMeshInstanced(instanceMesh, 0, instanceMaterial, voxelsMatrices[i]);
    }

    IEnumerator Voxelize( Mesh _mesh, float _voxelSize, bool keepInside = true)
    {
        if ( voxels == null) voxels = new List<Vector3>();
        voxels.Clear();
        
        float time = Time.realtimeSinceStartup;
        
        yield return null;
        
        if (_mesh == null) yield break;
        
        Bounds bounds = mesh.bounds;
        
        
        Vector3Int voxelsCount = new Vector3Int(
            Mathf.CeilToInt( bounds.extents.x * 2f / _voxelSize ),
            Mathf.CeilToInt( bounds.extents.y * 2f / _voxelSize ),
            Mathf.CeilToInt( bounds.extents.z * 2f / _voxelSize )
        );
        
        Vector3 voxelsCountF = voxelsCount;
        
        bounds.extents = voxelsCountF * 0.5f * _voxelSize;
        
        var voxelBounds = bounds;
        voxelBounds.extents = voxelBounds.extents - 0.5f * Vector3.one * _voxelSize;

        GameObject meshGO = new GameObject("Mesh2Voxelize");
        meshGO.layer = 31;
        MeshFilter meshFilter = meshGO.AddComponent<MeshFilter>();
        meshFilter.mesh = _mesh;
        MeshRenderer meshRenderer = meshGO.AddComponent<MeshRenderer>();

        Material material = new Material(Shader.Find("Hidden/VoxelizeShader"));
        material.SetInt("_ColorMask", 8 );
        Material[] materials = new Material[mesh.subMeshCount];
        for (int i = 0; i < mesh.subMeshCount; ++i) materials[i] = material;

        meshRenderer.sharedMaterials = materials;
        
        meshGO.transform.localPosition = Vector3.zero;
        meshGO.transform.localRotation = Quaternion.identity;
        meshGO.transform.localScale = Vector3.one;

        Camera camera = new GameObject("VoxelizeCamera").AddComponent<Camera>();
        camera.gameObject.layer = 31;
        camera.cullingMask = LayerMask.GetMask( LayerMask.LayerToName(31) );
        camera.transform.position = bounds.center + Vector3.back * ( bounds.extents.z + 1f );
        camera.clearFlags = CameraClearFlags.Depth;
        camera.backgroundColor = Color.black;
        camera.orthographic = true;
        camera.orthographicSize = Mathf.Max( bounds.extents.x, bounds.extents.y );
        camera.allowMSAA = false;

        camera.nearClipPlane = 0.9f;
        camera.farClipPlane = voxelsCountF.z * _voxelSize + 2f;

        GameObject backgroundQuad = GameObject.CreatePrimitive(PrimitiveType.Quad);
        backgroundQuad.layer = 31;
        backgroundQuad.transform.parent = camera.transform;
        backgroundQuad.transform.localPosition = Vector3.forward * ( camera.farClipPlane - 0.01f);
        backgroundQuad.transform.localRotation = Quaternion.identity;
        backgroundQuad.transform.localScale = Vector3.one * camera.orthographicSize * 4f;
        backgroundQuad.GetComponent<Renderer>().sharedMaterial = material;

        RenderTexture renderTexture = new RenderTexture(voxelsCount.x, voxelsCount.y, 0, RenderTextureFormat.ARGB32);
        renderTexture.antiAliasing = 1;
        Texture2D texture = new Texture2D(voxelsCount.x, voxelsCount.y, TextureFormat.ARGB32, false);

        camera.targetTexture = renderTexture;
        RenderTexture previous = RenderTexture.active;
        RenderTexture.active = renderTexture;

        bool[,,] bools = new bool[ voxelsCount.x, voxelsCount.y, voxelsCount.z ];
        Color[] pixels;

        int kernelIndex = computeShader.FindKernel("FilterVoxels");
        var filteredVoxelsBuffer = new ComputeBuffer(voxelsCount.x * voxelsCount.y, sizeof(float)*3, ComputeBufferType.Counter );
        computeShader.SetBuffer(kernelIndex, "filteredVoxelsBuffer", filteredVoxelsBuffer);
        
        // Buffer to store count in.
        var countBuffer = new ComputeBuffer(1, sizeof(int), ComputeBufferType.IndirectArguments);
        int[] counter = new int[1] { 0 };
        
        computeShader.SetVector("minP", voxelBounds.min);
        computeShader.SetVector("maxP", voxelBounds.max);
        
        computeShader.SetVector( "textureSize", new Vector2(voxelsCount.x, voxelsCount.y) );
                
        computeShader.SetTexture(kernelIndex, "slices", renderTexture);
        computeShader.SetBool("keepInside", keepInside);
        
        Vector3[] filteredVoxels = new Vector3[voxelsCount.x * voxelsCount.y];
        Vector3[] zeroData = new Vector3[voxelsCount.x * voxelsCount.y];
        for (int i=0 ; i<zeroData.Length ; ++i) zeroData[i] = Vector3.one * 20;
        
        //Vector3Int threadGroups = new Vector3Int( Mathf.CeilToInt( voxelsCountF.x / 8f ), Mathf.CeilToInt( voxelsCountF.y / 8f ), 1 );
        Vector3Int threadGroups = new Vector3Int( voxelsCount.x , voxelsCount.y , 1 );


        int prevColorMask = 0;
        for (int z = 0; z <= voxelsCount.z; ++z)
        {
            camera.nearClipPlane = 1f + (z + 0.5f) * _voxelSize;
            
            camera.Render();

            if (useComputeShader)
            {
                computeShader.SetInt("sliceIndex", colorMask);
                computeShader.SetFloat("zValue", Mathf.Lerp(voxelBounds.min.z, voxelBounds.max.z, (float)z / voxelsCount.z) );
                
                filteredVoxelsBuffer.SetData(zeroData );
                filteredVoxelsBuffer.SetCounterValue(0);
                
                computeShader.Dispatch(kernelIndex, threadGroups.x, threadGroups.y, 1);
                
                yield return null;
                
                var readbackRequest = AsyncGPUReadback.Request(filteredVoxelsBuffer);
                while (!readbackRequest.done)
                {
                    yield return null;
                }
                
                // Copy the count.
                ComputeBuffer.CopyCount(filteredVoxelsBuffer, countBuffer, 0);
                // Retrieve it into array.
                countBuffer.GetData(counter);

                filteredVoxels = readbackRequest.GetData<Vector3>().ToArray();
                
                //filteredVoxelsBuffer.GetData(filteredVoxels);
                
                System.Array.Resize(ref filteredVoxels, counter[0]);
                
                //Debug.Log( filteredVoxels.Aggregate("Data ("+filteredVoxels.Length+"): ", (s, v) => s += v.ToString() ) );
                
                voxels.AddRange(filteredVoxels);
                yield return null;
            }
            else
            {
                texture.ReadPixels(new Rect(0, 0, voxelsCount.x, voxelsCount.y), 0, 0);

                pixels = texture.GetPixels();

                bool isValidVoxel = false;

                if (keepInside)
                {
                    if (z < voxelsCount.z)
                        for (int x = 0; x < voxelsCount.x; ++x)
                        {
                            for (int y = 0; y < voxelsCount.y; ++y)
                            {
                                if (pixels[x + y * voxelsCount.x][colorMask] > 0.5f)
                                    voxels.Add(new Vector3(
                                        Mathf.Lerp( voxelBounds.min.x, voxelBounds.max.x, (float)x/voxelsCount.x ),
                                        Mathf.Lerp( voxelBounds.min.y, voxelBounds.max.y, (float)y/voxelsCount.y ),
                                        Mathf.Lerp( voxelBounds.min.z, voxelBounds.max.z, (float)z/voxelsCount.z )
                                    ));
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
                                    if (!(x == 0 || x == (voxelsCount.x - 1) || y == 0 || y == (voxelsCount.y - 1) || z == 1 || z == (voxelsCount.z))) // if not at the border
                                    {
                                        isValidVoxel = false;

                                        for (int x1 = x - 1; x1 < x + 2; ++x1)
                                        for (int y1 = y - 1; y1 < y + 2; ++y1)
                                        for (int z1 = 0; z1 < 3; ++z1)
                                            isValidVoxel |= pixels[x1 + y1 * voxelsCount.x][z1] < 0.5f; // if any pixel around is "empty"
                                    }


                                if (debugBounds.Contains(new Vector3(x, y, z)))
                                    Debug.DrawRay(new Vector3(x - 0.5f, y - 0.5f, z - 1.5f) * _voxelSize - (voxelsCountF - Vector3.one) * _voxelSize * 0.5f, Vector3.one * _voxelSize, pixels[x + y * voxelsCount.x], 10f);

                                if (isValidVoxel)
                                    voxels.Add(new Vector3(
                                        Mathf.Lerp( voxelBounds.min.x, voxelBounds.max.x, (float)x/voxelsCount.x ),
                                        Mathf.Lerp( voxelBounds.min.y, voxelBounds.max.y, (float)y/voxelsCount.y ),
                                        Mathf.Lerp( voxelBounds.min.z, voxelBounds.max.z, (float)z/voxelsCount.z )
                                        ));
                            }
                        }
                    }
                }
            }

            prevColorMask = colorMask;
            colorMask = (colorMask+1)%3;
            material.SetInt("_ColorMask", 1 + ( 1 << ( 3 - colorMask ) ) );

            //Debug.Log("ColorMask: "+material.GetInt("_ColorMask"));
        }

        if (useComputeShader)
        {
            countBuffer.Dispose();
            filteredVoxelsBuffer.Dispose();
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

        time = Time.realtimeSinceStartup - time;
        Debug.Log("Generated "+voxels.Count+" voxels in "+time+" seconds.");
        
        yield return null;
        //return voxels.ToArray();
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
