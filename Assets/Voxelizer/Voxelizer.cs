using System.Collections;
using System.Collections.Generic;
using UnityEngine;

#if UNITY_2018_2_OR_NEWER
using UnityEngine.Rendering;
#else
using UnityEngine.Experimental.Rendering;
#endif

#if UNITY_EDITOR
using UnityEditor;
#endif

namespace UnityEngine.Voxelizer
{
	public class Voxelizer
	{
		public Mesh mesh;
		public float voxelsSize = 0.5f ;
		public bool keepInside = true;

		bool m_processing = false;
		public bool processing {get{return m_processing;}}

		List<Vector3> m_voxels;

		public ComputeShader computeShader;

		public List<Vector3> voxels
		{
			get{ return m_voxels; }
		}

		public UnityEngine.Events.UnityAction finishedCallback;

		public IEnumerator Voxelize()
		{
#if UNITY_EDITOR
			string csPath = System.IO.Path.Combine( "Assets", "VoxelizeCS.compute");

			if (computeShader == null)
				computeShader = AssetDatabase.LoadAssetAtPath<ComputeShader>( csPath );

			
			Debug.Log("CS at path: "+csPath+" : "+( computeShader != null ) );
#endif

			if ( m_voxels == null) m_voxels = new List<Vector3>();
			m_voxels.Clear();
			
			float time = Time.realtimeSinceStartup;
			
			if (mesh == null) yield break;

			m_processing = true;
			
			Bounds bounds = mesh.bounds;
			
			// Number of voxels on each axis.
			Vector3Int m_voxelsCount = new Vector3Int(
				Mathf.CeilToInt( bounds.extents.x * 2f / voxelsSize ),
				Mathf.CeilToInt( bounds.extents.y * 2f / voxelsSize ),
				Mathf.CeilToInt( bounds.extents.z * 2f / voxelsSize )
			);
			
			Vector3 m_voxelsCountF = m_voxelsCount;
			
			// Resize the bounds to match the border of voxels.
			bounds.extents = m_voxelsCountF * 0.5f * voxelsSize;
			
			// The bounds of the center of the voxels.
			var voxelBounds = bounds;
			voxelBounds.extents = voxelBounds.extents - 0.5f * Vector3.one * voxelsSize;

			// Material to render the slices.
			Material material = new Material(Shader.Find("Hidden/VoxelizeShader"));
			material.SetInt("_ColorMask", 8 );
			Material[] materials = new Material[mesh.subMeshCount];
			for (int i = 0; i < mesh.subMeshCount; ++i) materials[i] = material;

			// Create the camera to render the object slices
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
			camera.farClipPlane = bounds.extents.z * 2 + 2f;

			// Quad for the background.
			Mesh quad = new Mesh();
			quad.vertices = new Vector3[]{
				new Vector3( bounds.min.x - 1f, bounds.min.y - 1f, bounds.min.z - 1f ),
				new Vector3( bounds.max.x + 1f, bounds.min.y - 1f, bounds.min.z - 1f ),
				new Vector3( bounds.max.x + 1f, bounds.max.y + 1f, bounds.min.z - 1f ),
				new Vector3( bounds.min.x - 1f, bounds.max.y + 1f, bounds.min.z - 1f )
			};
			quad.triangles = new int[]{
				0,1,2,
				2,3,0
			};
			quad.RecalculateBounds();
			quad.RecalculateNormals();

			// Render texture to render the slices.
			RenderTexture renderTexture = new RenderTexture(m_voxelsCount.x, m_voxelsCount.y, 0, RenderTextureFormat.ARGB32);
			renderTexture.antiAliasing = 1;

			camera.targetTexture = renderTexture;

			int kernelIndex = computeShader.FindKernel("FilterVoxels");
			var filteredm_voxelsBuffer = new ComputeBuffer(m_voxelsCount.x * m_voxelsCount.y, sizeof(float)*3, ComputeBufferType.Counter );
			computeShader.SetBuffer(kernelIndex, "filteredVoxelsBuffer", filteredm_voxelsBuffer);
			
			// Buffer to store count in.
			var countBuffer = new ComputeBuffer(1, sizeof(int), ComputeBufferType.IndirectArguments);
			int[] counter = new int[1] { 0 };
			
			computeShader.SetVector("minP", voxelBounds.min);
			computeShader.SetVector("maxP", voxelBounds.max);
			
			computeShader.SetVector( "textureSize", new Vector2(m_voxelsCount.x, m_voxelsCount.y) );
					
			computeShader.SetTexture(kernelIndex, "slices", renderTexture);
			computeShader.SetBool("keepInside", keepInside);
			
			Vector3[] filteredm_voxels = new Vector3[m_voxelsCount.x * m_voxelsCount.y];
			Vector3[] zeroData = new Vector3[m_voxelsCount.x * m_voxelsCount.y];
			for (int i=0 ; i<zeroData.Length ; ++i) zeroData[i] = Vector3.one * 20;
			
			//Vector3Int threadGroups = new Vector3Int( Mathf.CeilToInt( m_voxelsCountF.x / 8f ), Mathf.CeilToInt( m_voxelsCountF.y / 8f ), 1 );
			Vector3Int threadGroups = new Vector3Int( m_voxelsCount.x , m_voxelsCount.y , 1 );

			int colorMask = 0;
			Color backgroundColor = Color.red;

			CommandBuffer cmd = new CommandBuffer();
			cmd.name = "Draw Slices";

			int prevColorMask = 0;
			for (int z = 0; z <= m_voxelsCount.z; ++z)
			{
				camera.nearClipPlane = 1f + (z + 0.5f) * voxelsSize;

				camera.RemoveAllCommandBuffers();

				cmd.Clear();
				cmd.ClearRenderTarget(true, true, Color.black, 1.0f);
				cmd.DrawMesh(quad, Matrix4x4.identity, material);
				cmd.DrawMesh(mesh, Matrix4x4.identity, material);

				camera.AddCommandBuffer( CameraEvent.AfterEverything, cmd);
				
				camera.Render();
				
				computeShader.SetInt("sliceIndex", colorMask);
				computeShader.SetFloat("zValue", Mathf.Lerp(voxelBounds.min.z, voxelBounds.max.z, (float)z / m_voxelsCount.z) );
				
				filteredm_voxelsBuffer.SetData(zeroData );
				filteredm_voxelsBuffer.SetCounterValue(0);
				
				computeShader.Dispatch(kernelIndex, threadGroups.x, threadGroups.y, 1);
				
				var readbackRequest = AsyncGPUReadback.Request(filteredm_voxelsBuffer);
				while (!readbackRequest.done)
				{
					yield return null;
				}
				
				// Copy the count.
				ComputeBuffer.CopyCount(filteredm_voxelsBuffer, countBuffer, 0);
				// Retrieve it into array.
				countBuffer.GetData(counter);

				filteredm_voxels = readbackRequest.GetData<Vector3>().ToArray();
				
				//filteredm_voxelsBuffer.GetData(filteredm_voxels);
				
				System.Array.Resize(ref filteredm_voxels, counter[0]);
				
				//Debug.Log( filteredm_voxels.Aggregate("Data ("+filteredm_voxels.Length+"): ", (s, v) => s += v.ToString() ) );
				
				m_voxels.AddRange(filteredm_voxels);

				prevColorMask = colorMask;
				colorMask = (colorMask+1)%3;
				material.SetInt("_ColorMask", 1 + ( 1 << ( 3 - colorMask ) ) );

				//Debug.Log("ColorMask: "+material.GetInt("_ColorMask"));
			}

			countBuffer.Dispose();
			filteredm_voxelsBuffer.Dispose();
			cmd.Dispose();

			Object.Destroy(camera.gameObject);
			Object.Destroy(renderTexture);
			Object.Destroy(material);

			time = Time.realtimeSinceStartup - time;
			Debug.Log("Generated "+m_voxels.Count+" m_voxels in "+time+" seconds.");

			m_processing = false;
			
			if (finishedCallback != null ) finishedCallback();
		}
	}
}
