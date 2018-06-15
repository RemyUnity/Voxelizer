using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class CSTester : MonoBehaviour
{
	public ComputeShader computeShader;
	public Texture2D shapesTexture;

	public int renderTextureSize = 256;
	public RenderTexture renderTexture;

	struct VecMatPair
	{
	public Vector3 point;
	public Matrix4x4 matrix;
	}

	// Use this for initialization
	void Start ()
	{
		if (computeShader == null) return;
		if (shapesTexture == null) return;

		renderTexture = new RenderTexture(shapesTexture.width, shapesTexture.height, 0);
		renderTexture.name = "renderTexture";
		renderTexture.enableRandomWrite = true;
		renderTexture.filterMode = FilterMode.Point;
		renderTexture.Create();

		int kernelHandle = computeShader.FindKernel("CSMain");

		computeShader.SetTexture(kernelHandle, "Shape", shapesTexture);
		computeShader.SetTexture(kernelHandle, "Result", renderTexture);
		computeShader.SetFloat("threshold", 0.5f);
		computeShader.SetInt("channelOffset", 2);
		computeShader.Dispatch(kernelHandle, shapesTexture.width/8, shapesTexture.height/8, 1);

		RenderTexture counterTexture = new RenderTexture(2, 1, 0, RenderTextureFormat.ARGB32);
		counterTexture.name = "counterTexture";
		counterTexture.enableRandomWrite = true;
		counterTexture.filterMode = FilterMode.Point;
		counterTexture.Create();

		RenderTexture coordinatesTexture = new RenderTexture(shapesTexture.width, shapesTexture.height, 0, RenderTextureFormat.ARGBFloat);
		coordinatesTexture.name = "coordinatesTexture";
		coordinatesTexture.enableRandomWrite = true;
		coordinatesTexture.filterMode = FilterMode.Point;
		coordinatesTexture.Create();

		int filterHandle = computeShader.FindKernel("CSFilter");
		computeShader.SetTexture(filterHandle, "Voxels", renderTexture);
		computeShader.SetTexture(filterHandle, "Counter", counterTexture);
		computeShader.SetTexture(filterHandle, "Coordinates", coordinatesTexture);
		computeShader.SetInt("index", 0);

		ComputeBuffer appendVoxelsBuffer = new ComputeBuffer(2048, sizeof(float)*3, ComputeBufferType.Append);
		computeShader.SetBuffer(filterHandle, Shader.PropertyToID("AppendVoxelsBuffer"), appendVoxelsBuffer);

		ComputeBuffer fixedVoxelsBuffer = new ComputeBuffer( 2048*2048, sizeof(float)*3 );
		computeShader.SetBuffer(filterHandle, Shader.PropertyToID("FixedVoxelsBuffer"), fixedVoxelsBuffer);
		
		computeShader.Dispatch(filterHandle, shapesTexture.width/64, shapesTexture.height, 1);

		Vector3[] data = new Vector3[2048];
		appendVoxelsBuffer.GetData(data);

		
		string strData = "Data: ";
		for (int i=0 ; i<2048 ; ++i)
			if (data[i] != null)
				strData = string.Concat(strData, data[i].ToString("G6"), " ; ");
		Debug.Log(strData);

		// Display the RT
		Renderer renderer = GetComponent<Renderer>();
		if (renderer != null)
		{
			renderer.material.mainTexture = counterTexture;
		}
	}
}
