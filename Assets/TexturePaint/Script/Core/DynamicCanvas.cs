﻿using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

#if UNITY_EDITOR

using UnityEditor;
using UnityEditor.SceneManagement;

#endif

namespace Es.TexturePaint
{
	/// <summary>
	/// Texture paint to canvas.
	/// To set the per-material.
	/// </summary>
	[RequireComponent(typeof(Renderer))]
	[RequireComponent(typeof(Collider))]
	[DisallowMultipleComponent]
	public class DynamicCanvas : MonoBehaviour
	{
		[Serializable]
		private class PaintSet
		{
			/// <summary>
			/// Applying paint materials.
			/// </summary>
			[HideInInspector]
			[NonSerialized]
			public Material material;

			[SerializeField, Tooltip("The property name of the main texture.")]
			public string mainTextureName = "_MainTex";

			[SerializeField, Tooltip("Normal map texture property name.")]
			public string normalTextureName = "_BumpMap";

			[SerializeField, Tooltip("The property name of the heightmap texture.")]
			public string heightTextureName = "_ParallaxMap";

			[SerializeField, Tooltip("Whether or not use main texture paint.")]
			public bool useMainPaint = true;

			[SerializeField, Tooltip("Whether or not use normal map paint (you need material on normal maps).")]
			public bool useNormalPaint = false;

			[SerializeField, Tooltip("Whether or not use heightmap painting (you need material on the heightmap).")]
			public bool useHeightPaint = false;

			/// <summary>
			/// In the first time set to the material's main texture.
			/// </summary>
			[HideInInspector]
			[NonSerialized]
			public Texture mainTexture;

			/// <summary>
			/// Copied the main texture to rendertexture that use to paint.
			/// </summary>
			[HideInInspector]
			[NonSerialized]
			public RenderTexture paintMainTexture;

			/// <summary>
			/// In the first time set to the material's normal map.
			/// </summary>
			[HideInInspector]
			[NonSerialized]
			public Texture normalTexture;

			/// <summary>
			/// Copied the normal map to rendertexture that use to paint.
			/// </summary>
			[HideInInspector]
			[NonSerialized]
			public RenderTexture paintNormalTexture;

			/// <summary>
			/// In the first time set to the material's height map.
			/// </summary>
			[HideInInspector]
			[NonSerialized]
			public Texture heightTexture;

			/// <summary>
			/// Copied the height map to rendertexture that use to paint.
			/// </summary>
			[HideInInspector]
			[NonSerialized]
			public RenderTexture paintHeightTexture;

			#region ShaderPropertyID

			[HideInInspector]
			[NonSerialized]
			public int mainTexturePropertyID;

			[HideInInspector]
			[NonSerialized]
			public int normalTexturePropertyID;

			[HideInInspector]
			[NonSerialized]
			public int heightTexturePropertyID;

			#endregion ShaderPropertyID
		}

		private const int DEFAULT_TEXTURE_SIZE = 256;
		private static Material paintMaterial = null;
		private static Material paintNormalMaterial = null;
		private static Material paintHeightMaterial = null;
		private Action<DynamicCanvas> onInitializedAfter = null;

		/// <summary>
		/// Called by dynamic canvas initialization completion times.
		/// </summary>
		public Action<DynamicCanvas> OnInitializedAfter { set { onInitializedAfter += value; } }

		#region SerializedField

		[SerializeField]
		private List<PaintSet> paintSet = null;

		#endregion SerializedField

		#region ShaderPropertyID

		private int paintUVPropertyID;

		private int brushTexturePropertyID;
		private int brushScalePropertyID;
		private int brushColorPropertyID;
		private int brushNormalTexturePropertyID;
		private int brushNormalBlendPropertyID;
		private int brushHeightTexturePropertyID;
		private int brushHeightBlendPropertyID;
		private int brushHeightColorPropertyID;

		#endregion ShaderPropertyID

		#region ShaderKeywords

		private const string COLOR_BLEND_USE_CONTROL = "TEXTURE_PAINT_COLOR_BLEND_USE_CONTROL";
		private const string COLOR_BLEND_USE_BRUSH = "TEXTURE_PAINT_COLOR_BLEND_USE_BRUSH";
		private const string COLOR_BLEND_NEUTRAL = "TEXTURE_PAINT_COLOR_BLEND_NEUTRAL";
		private const string COLOR_BLEND_ALPHA_ONLY = "TEXTURE_PAINT_COLOR_BLEND_ALPHA_ONLY";

		private const string NORMAL_BLEND_USE_BRUSH = "TEXTURE_PAINT_NORMAL_BLEND_USE_BRUSH";
		private const string NORMAL_BLEND_ADD = "TEXTURE_PAINT_NORMAL_BLEND_ADD";
		private const string NORMAL_BLEND_SUB = "TEXTURE_PAINT_NORMAL_BLEND_SUB";
		private const string NORMAL_BLEND_MIN = "TEXTURE_PAINT_NORMAL_BLEND_MIN";
		private const string NORMAL_BLEND_MAX = "TEXTURE_PAINT_NORMAL_BLEND_MAX";

		private const string HEIGHT_BLEND_USE_BRUSH = "TEXTURE_PAINT_HEIGHT_BLEND_USE_BRUSH";
		private const string HEIGHT_BLEND_ADD = "TEXTURE_PAINT_HEIGHT_BLEND_ADD";
		private const string HEIGHT_BLEND_SUB = "TEXTURE_PAINT_HEIGHT_BLEND_SUB";
		private const string HEIGHT_BLEND_MIN = "TEXTURE_PAINT_HEIGHT_BLEND_MIN";
		private const string HEIGHT_BLEND_MAX = "TEXTURE_PAINT_HEIGHT_BLEND_MAX";
		private const string HEIGHT_BLEND_COLOR_RGB_HEIGHT_A = "TEXTURE_PAINT_HEIGHT_BLEND_COLOR_RGB_HEIGHT_A";

		#endregion ShaderKeywords

		#region MeshData

		private MeshOperator meshOperator;

		public MeshOperator MeshOperator
		{
			get
			{
				if(meshOperator == null)
					Debug.LogError("To take advantage of the features must Mesh filter or Skinned mesh renderer component associated Mesh.");

				return meshOperator;
			}
		}

		#endregion MeshData

		#region UnityEventMethod

		private void Awake()
		{
			InitPropertyID();
			SetMaterial();
			SetTexture();
			MeshDataCache();
		}

		private void Start()
		{
			SetRenderTexture();
			if(onInitializedAfter != null)
				onInitializedAfter(this);
		}

		private void OnDestroy()
		{
			Debug.Log("Dynamic canvas has been destroyed.");
			ReleaseRenderTexture();
		}

		#endregion UnityEventMethod

		#region PrivateMethod

		/// <summary>
		/// Cach data from the mesh.
		/// </summary>
		private void MeshDataCache()
		{
			var meshFilter = GetComponent<MeshFilter>();
			var skinnedMeshRenderer = GetComponent<SkinnedMeshRenderer>();
			if(meshFilter != null)
				meshOperator = new MeshOperator(meshFilter.sharedMesh);
			else if(skinnedMeshRenderer != null)
				meshOperator = new MeshOperator(skinnedMeshRenderer.sharedMesh);
			else
				Debug.LogWarning("Sometimes if the MeshFilter or SkinnedMeshRenderer does not exist in the component part does not work correctly.");
		}

		/// <summary>
		/// To initialize the shader property ID.
		/// </summary>
		private void InitPropertyID()
		{
			foreach(var p in paintSet)
			{
				p.mainTexturePropertyID = Shader.PropertyToID(p.mainTextureName);
				p.normalTexturePropertyID = Shader.PropertyToID(p.normalTextureName);
				p.heightTexturePropertyID = Shader.PropertyToID(p.heightTextureName);
			}
			paintUVPropertyID = Shader.PropertyToID("_PaintUV");
			brushTexturePropertyID = Shader.PropertyToID("_Brush");
			brushScalePropertyID = Shader.PropertyToID("_BrushScale");
			brushColorPropertyID = Shader.PropertyToID("_ControlColor");
			brushNormalTexturePropertyID = Shader.PropertyToID("_BrushNormal");
			brushNormalBlendPropertyID = Shader.PropertyToID("_NormalBlend");
			brushHeightTexturePropertyID = Shader.PropertyToID("_BrushHeight");
			brushHeightBlendPropertyID = Shader.PropertyToID("_HeightBlend");
			brushHeightColorPropertyID = Shader.PropertyToID("_Color");
		}

		/// <summary>
		/// Set and retrieve the material.
		/// </summary>
		private void SetMaterial()
		{
			if(paintMaterial == null)
				paintMaterial = Resources.Load<Material>("Es.TexturePaint.PaintMain");
			if(paintNormalMaterial == null)
				paintNormalMaterial = Resources.Load<Material>("Es.TexturePaint.PaintNormal");
			if(paintHeightMaterial == null)
				paintHeightMaterial = Resources.Load<Material>("Es.TexturePaint.PaintHeight");
			var m = GetComponent<Renderer>().materials;
			for(int i = 0; i < m.Length; ++i)
			{
				paintSet[i].material = m[i];
			}
		}

		/// <summary>
		/// Set and retrieve the texture.
		/// </summary>
		private void SetTexture()
		{
			foreach(var p in paintSet)
			{
				p.mainTexture = p.material.GetTexture(p.mainTexturePropertyID);
				p.normalTexture = p.material.GetTexture(p.normalTexturePropertyID);
				p.heightTexture = p.material.GetTexture(p.heightTexturePropertyID);
			}
		}

		/// <summary>
		/// Creates a rendertexture and set the material.
		/// </summary>
		private void SetRenderTexture()
		{
			foreach(var p in paintSet)
			{
				if(p.useMainPaint)
				{
					if(p.mainTexture == null)
						p.mainTexture = new Texture2D(DEFAULT_TEXTURE_SIZE, DEFAULT_TEXTURE_SIZE, TextureFormat.RGBA32, false);
					p.paintMainTexture = new RenderTexture(p.mainTexture.width, p.mainTexture.height, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.Default);
					Graphics.Blit(p.mainTexture, p.paintMainTexture);
					p.material.SetTexture(p.mainTexturePropertyID, p.paintMainTexture);
				}
				if(p.useNormalPaint)
				{
					if(p.normalTexture != null)
					{
						p.paintNormalTexture = new RenderTexture(p.normalTexture.width, p.normalTexture.height, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.Default);
						Graphics.Blit(p.normalTexture, p.paintNormalTexture);
						p.material.SetTexture(p.normalTexturePropertyID, p.paintNormalTexture);
					}
					else
						Debug.LogWarning("To take advantage of the normal map paint must set normal map to materials.");
				}
				if(p.useHeightPaint)
				{
					if(p.heightTexture != null)
					{
						p.paintHeightTexture = new RenderTexture(p.heightTexture.width, p.heightTexture.height, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.Default);
						Graphics.Blit(p.heightTexture, p.paintHeightTexture);
						p.material.SetTexture(p.heightTexturePropertyID, p.paintHeightTexture);
					}
					else
						Debug.LogWarning("To take advantage of the height map paint must set height map to materials.");
				}
			}
		}

		/// <summary>
		/// Rendertexture release process.
		/// </summary>
		private void ReleaseRenderTexture()
		{
			foreach(var p in paintSet)
			{
				if(RenderTexture.active != p.paintMainTexture && p.paintMainTexture != null && p.paintMainTexture.IsCreated())
					p.paintMainTexture.Release();
				if(RenderTexture.active != p.paintNormalTexture && p.paintNormalTexture != null && p.paintNormalTexture.IsCreated())
					p.paintNormalTexture.Release();
				if(RenderTexture.active != p.paintHeightTexture && p.paintHeightTexture != null && p.paintHeightTexture.IsCreated())
					p.paintHeightTexture.Release();
			}
		}

		/// <summary>
		/// To set the data needed to paint shader.
		/// </summary>
		/// <param name="brush">Brush data.</param>
		/// <param name="uv">UV coordinates for the hit location.</param>
		private void SetPaintMainData(PaintBrush brush, Vector2 uv)
		{
			paintMaterial.SetVector(paintUVPropertyID, uv);
			paintMaterial.SetTexture(brushTexturePropertyID, brush.BrushTexture);
			paintMaterial.SetFloat(brushScalePropertyID, brush.Scale);
			paintMaterial.SetVector(brushColorPropertyID, brush.Color);

			foreach(var key in paintMaterial.shaderKeywords)
				paintMaterial.DisableKeyword(key);
			switch(brush.ColorBlending)
			{
				case PaintBrush.ColorBlendType.UseColor:
					paintMaterial.EnableKeyword(COLOR_BLEND_USE_CONTROL);
					break;

				case PaintBrush.ColorBlendType.UseBrush:
					paintMaterial.EnableKeyword(COLOR_BLEND_USE_BRUSH);
					break;

				case PaintBrush.ColorBlendType.Neutral:
					paintMaterial.EnableKeyword(COLOR_BLEND_NEUTRAL);
					break;

				case PaintBrush.ColorBlendType.AlphaOnly:
					paintMaterial.EnableKeyword(COLOR_BLEND_ALPHA_ONLY);
					break;

				default:
					paintMaterial.EnableKeyword(COLOR_BLEND_USE_CONTROL);
					break;
			}
		}

		/// <summary>
		/// To set the data needed to normal map paint shader
		/// </summary>
		/// <param name="brush">Brush data.</param>
		/// <param name="uv">UV coordinates for the hit location.</param>
		private void SetPaintNormalData(PaintBrush brush, Vector2 uv)
		{
			paintNormalMaterial.SetVector(paintUVPropertyID, uv);
			paintNormalMaterial.SetTexture(brushTexturePropertyID, brush.BrushTexture);
			paintNormalMaterial.SetTexture(brushNormalTexturePropertyID, brush.BrushNormalTexture);
			paintNormalMaterial.SetFloat(brushScalePropertyID, brush.Scale);
			paintNormalMaterial.SetFloat(brushNormalBlendPropertyID, brush.NormalBlend);

			foreach(var key in paintNormalMaterial.shaderKeywords)
				paintNormalMaterial.DisableKeyword(key);
			switch(brush.NormalBlending)
			{
				case PaintBrush.NormalBlendType.UseBrush:
					paintNormalMaterial.EnableKeyword(NORMAL_BLEND_USE_BRUSH);
					break;

				case PaintBrush.NormalBlendType.Add:
					paintNormalMaterial.EnableKeyword(NORMAL_BLEND_ADD);
					break;

				case PaintBrush.NormalBlendType.Sub:
					paintNormalMaterial.EnableKeyword(NORMAL_BLEND_SUB);
					break;

				case PaintBrush.NormalBlendType.Min:
					paintNormalMaterial.EnableKeyword(NORMAL_BLEND_MIN);
					break;

				case PaintBrush.NormalBlendType.Max:
					paintNormalMaterial.EnableKeyword(NORMAL_BLEND_MAX);
					break;

				default:
					paintNormalMaterial.EnableKeyword(NORMAL_BLEND_USE_BRUSH);
					break;
			}
		}

		/// <summary>
		/// To set the data needed to height map paint shader.
		/// </summary>
		/// <param name="brush">Brush data.</param>
		/// <param name="uv">UV coordinates for the hit location.</param>
		private void SetPaintHeightData(PaintBrush brush, Vector2 uv)
		{
			paintHeightMaterial.SetVector(paintUVPropertyID, uv);
			paintHeightMaterial.SetTexture(brushTexturePropertyID, brush.BrushTexture);
			paintHeightMaterial.SetTexture(brushHeightTexturePropertyID, brush.BrushHeightTexture);
			paintHeightMaterial.SetFloat(brushScalePropertyID, brush.Scale);
			paintHeightMaterial.SetFloat(brushHeightBlendPropertyID, brush.HeightBlend);
			paintHeightMaterial.SetVector(brushHeightColorPropertyID, brush.Color);

			foreach(var key in paintHeightMaterial.shaderKeywords)
				paintHeightMaterial.DisableKeyword(key);
			switch(brush.HeightBlending)
			{
				case PaintBrush.HeightBlendType.UseBrush:
					paintHeightMaterial.EnableKeyword(HEIGHT_BLEND_USE_BRUSH);
					break;

				case PaintBrush.HeightBlendType.Add:
					paintHeightMaterial.EnableKeyword(HEIGHT_BLEND_ADD);
					break;

				case PaintBrush.HeightBlendType.Sub:
					paintHeightMaterial.EnableKeyword(HEIGHT_BLEND_SUB);
					break;

				case PaintBrush.HeightBlendType.Min:
					paintHeightMaterial.EnableKeyword(HEIGHT_BLEND_MIN);
					break;

				case PaintBrush.HeightBlendType.Max:
					paintHeightMaterial.EnableKeyword(HEIGHT_BLEND_MAX);
					break;

				case PaintBrush.HeightBlendType.ColorRGB_HeightA:
					paintHeightMaterial.EnableKeyword(HEIGHT_BLEND_COLOR_RGB_HEIGHT_A);
					break;

				default:
					paintHeightMaterial.EnableKeyword(HEIGHT_BLEND_ADD);
					break;
			}
		}

		#endregion PrivateMethod

		#region PublicMethod

		/// <summary>
		/// Paint processing that UV coordinates to the specified.
		/// </summary>
		/// <param name="brush">Brush data.</param>
		/// <param name="uv">UV coordinates for the hit location.</param>
		/// <returns>The success or failure of the paint.</returns>
		public bool PaintUVDirect(PaintBrush brush, Vector2 uv)
		{
			#region ErrorCheck

			if(brush == null)
			{
				Debug.LogError("Do not set the brush.");
				return false;
			}

			#endregion ErrorCheck

			foreach(var p in paintSet)
			{
				if(p.useMainPaint && brush.BrushTexture != null && p.paintMainTexture != null && p.paintMainTexture.IsCreated())
				{
					var mainPaintTextureBuffer = RenderTexture.GetTemporary(p.paintMainTexture.width, p.paintMainTexture.height);
					SetPaintMainData(brush, uv);
					Graphics.Blit(p.paintMainTexture, mainPaintTextureBuffer, paintMaterial);
					Graphics.Blit(mainPaintTextureBuffer, p.paintMainTexture);
					RenderTexture.ReleaseTemporary(mainPaintTextureBuffer);
				}

				if(p.useNormalPaint && brush.BrushNormalTexture != null && p.paintNormalTexture != null && p.paintNormalTexture.IsCreated())
				{
					var normalPaintTextureBuffer = RenderTexture.GetTemporary(p.paintNormalTexture.width, p.paintNormalTexture.height);
					SetPaintNormalData(brush, uv);
					Graphics.Blit(p.paintNormalTexture, normalPaintTextureBuffer, paintNormalMaterial);
					Graphics.Blit(normalPaintTextureBuffer, p.paintNormalTexture);
					RenderTexture.ReleaseTemporary(normalPaintTextureBuffer);
				}

				if(p.useHeightPaint && brush.BrushHeightTexture != null && p.paintHeightTexture != null && p.paintHeightTexture.IsCreated())
				{
					var heightPaintTextureBuffer = RenderTexture.GetTemporary(p.paintHeightTexture.width, p.paintHeightTexture.height);
					SetPaintHeightData(brush, uv);
					Graphics.Blit(p.paintHeightTexture, heightPaintTextureBuffer, paintHeightMaterial);
					Graphics.Blit(heightPaintTextureBuffer, p.paintHeightTexture);
					RenderTexture.ReleaseTemporary(heightPaintTextureBuffer);
				}
			}
			return true;
		}

		/// <summary>
		/// Paint of points close to the given world-space position on the Mesh surface.
		/// </summary>
		/// <param name="brush">Brush data.</param>
		/// <param name="worldPos">Approximate point.</param>
		/// <param name="renderCamera">Camera to use to render the object.</param>
		/// <returns>The success or failure of the paint.</returns>
		public bool PaintNearestTriangleSurface(PaintBrush brush, Vector3 worldPos, Camera renderCamera = null)
		{
			var p = transform.worldToLocalMatrix.MultiplyPoint(worldPos);
			var pd = MeshOperator.NearestLocalSurfacePoint(p);

			return Paint(brush, transform.localToWorldMatrix.MultiplyPoint(pd), renderCamera);
		}

		/// <summary>
		/// Paint processing that use world-space surface position.
		/// </summary>
		/// <param name="brush">Brush data.</param>
		/// <param name="worldPos">Point on object surface (world-space).</param>
		/// <param name="renderCamera">Camera to use to render the object.</param>
		/// <returns>The success or failure of the paint.</returns>
		public bool Paint(PaintBrush brush, Vector3 worldPos, Camera renderCamera = null)
		{
			Vector2 uv;

			if(renderCamera == null)
				renderCamera = Camera.main;

			Vector3 p = transform.InverseTransformPoint(worldPos);
			Matrix4x4 mvp = renderCamera.projectionMatrix * renderCamera.worldToCameraMatrix * transform.localToWorldMatrix;
			if(MeshOperator.LocalPointToUV(p, mvp, out uv))
				return PaintUVDirect(brush, uv);
			else
			{
				Debug.LogWarning("Could not get the point on the surface.");
				return PaintNearestTriangleSurface(brush, worldPos, renderCamera);
			}
		}

		/// <summary>
		/// Paint processing that use raycast hit data.
		/// Must MeshCollider is set to the canvas.
		/// </summary>
		/// <param name="brush">Brush data.</param>
		/// <param name="hitInfo">Raycast hit info.</param>
		/// <returns>The success or failure of the paint.</returns>
		public bool Paint(PaintBrush brush, RaycastHit hitInfo)
		{
			if(hitInfo.collider != null && hitInfo.collider.gameObject == gameObject)
			{
				if(!(GetComponent<Collider>() is MeshCollider))
				{
					Debug.LogWarning("If you want to paint using a Raycast, need set MeshCollider for canvas object.");
					return PaintNearestTriangleSurface(brush, hitInfo.point);
				}
				return PaintUVDirect(brush, hitInfo.textureCoord);
			}
			return false;
		}

		/// <summary>
		/// To reset the paint.
		/// </summary>
		public void ResetPaint()
		{
			ReleaseRenderTexture();
			SetRenderTexture();
			if(onInitializedAfter != null)
				onInitializedAfter(this);
		}

		/// <summary>
		/// To get the original main texture.
		/// </summary>
		/// <param name="materialName">Material name.</param>
		/// <returns>Original main texture.</returns>
		public Texture GetMainTexture(string materialName)
		{
			var data = paintSet.FirstOrDefault(p => p.material.name == materialName);
			if(data == null)
				return null;
			return data.mainTexture;
		}

		/// <summary>
		/// To get the main texture in paint.
		/// </summary>
		/// <param name="materialName">Material name.</param>
		/// <returns>Main texture in paint.</returns>
		public RenderTexture GetPaintMainTexture(string materialName)
		{
			var data = paintSet.FirstOrDefault(p => p.material.name == materialName);
			if(data == null)
				return null;
			return data.paintMainTexture;
		}

		/// <summary>
		/// Set paint texture.
		/// </summary>
		/// <param name="materialName">Material name.</param>
		/// <param name="newTexture">New rendertexture.</param>
		public void SetPaintMainTexture(string materialName, RenderTexture newTexture)
		{
			var data = paintSet.FirstOrDefault(p => p.material.name == materialName);
			if(data == null)
			{
				Debug.LogError("Failed to set texture.");
				return;
			}
			data.paintMainTexture = newTexture;
			data.material.SetTexture(data.mainTextureName, data.paintMainTexture);
			data.useMainPaint = true;
		}

		/// <summary>
		/// To get the original normal map.
		/// </summary>
		/// <param name="materialName">Material name.</param>
		/// <returns>Original normal map.</returns>
		public Texture GetNormalTexture(string materialName)
		{
			var data = paintSet.FirstOrDefault(p => p.material.name == materialName);
			if(data == null)
				return null;
			return data.normalTexture;
		}

		/// <summary>
		/// To get the paint in normal map.
		/// </summary>
		/// <param name="materialName">Material name.</param>
		/// <returns>Normal map in paint.</returns>
		public RenderTexture GetPaintNormalTexture(string materialName)
		{
			var data = paintSet.FirstOrDefault(p => p.material.name == materialName);
			if(data == null)
				return null;
			return data.paintNormalTexture;
		}

		/// <summary>
		/// Set paint texture.
		/// </summary>
		/// <param name="materialName">Material name.</param>
		/// <param name="newTexture">New rendertexture.</param>
		public void SetPaintNormalTexture(string materialName, RenderTexture newTexture)
		{
			var data = paintSet.FirstOrDefault(p => p.material.name == materialName);
			if(data == null)
			{
				Debug.LogError("Failed to set texture.");
				return;
			}
			data.paintNormalTexture = newTexture;
			data.material.SetTexture(data.normalTextureName, data.paintNormalTexture);
			data.useNormalPaint = true;
		}

		/// <summary>
		/// To get the original height map.
		/// </summary>
		/// <param name="materialName">Material name.</param>
		/// <returns>Original height map.</returns>
		public Texture GetHeightTexture(string materialName)
		{
			var data = paintSet.FirstOrDefault(p => p.material.name == materialName);
			if(data == null)
				return null;
			return data.heightTexture;
		}

		/// <summary>
		/// To get the paint in height map.
		/// </summary>
		/// <param name="materialName">Material name.</param>
		/// <returns>Height map in paint.</returns>
		public RenderTexture GetPaintHeightTexture(string materialName)
		{
			var data = paintSet.FirstOrDefault(p => p.material.name == materialName);
			if(data == null)
				return null;
			return data.paintHeightTexture;
		}

		/// <summary>
		/// Set paint texture.
		/// </summary>
		/// <param name="materialName">Material name.</param>
		/// <param name="newTexture">New rendertexture.</param>
		public void SetPaintHeightTexture(string materialName, RenderTexture newTexture)
		{
			var data = paintSet.FirstOrDefault(p => p.material.name == materialName);
			if(data == null)
			{
				Debug.LogError("Failed to set texture.");
				return;
			}
			data.paintHeightTexture = newTexture;
			data.material.SetTexture(data.heightTextureName, data.paintHeightTexture);
			data.useHeightPaint = true;
		}

		#endregion PublicMethod

		#region CustomEditor

#if UNITY_EDITOR

		[CustomEditor(typeof(DynamicCanvas))]
		[CanEditMultipleObjects]
		private class DynamicCanvasInspectorExtension : Editor
		{
			private Renderer renderer;
			private Material[] materials;
			private List<bool> foldOut;

			public override void OnInspectorGUI()
			{
				var instance = target as DynamicCanvas;
				if(instance.paintSet == null)
					instance.paintSet = new List<PaintSet>();

				if(renderer == null)
					renderer = instance.GetComponent<Renderer>();
				if(materials == null)
					materials = renderer.sharedMaterials;
				if(foldOut == null)
					foldOut = new List<bool>();

				if(instance.paintSet.Count < materials.Length)
				{
					for(int i = instance.paintSet.Count; i < materials.Length; ++i)
						instance.paintSet.Add(new PaintSet
						{
							mainTextureName = "_MainTex",
							normalTextureName = "_BumpMap",
							heightTextureName = "_ParallaxMap",
							useMainPaint = true,
							useNormalPaint = false
						});
					foldOut.Clear();
				}

				if(instance.paintSet.Count > materials.Length)
				{
					instance.paintSet.RemoveRange(materials.Length, instance.paintSet.Count - materials.Length);
					foldOut.Clear();
				}

				if(foldOut.Count < instance.paintSet.Count)
					for(int i = foldOut.Count; i < instance.paintSet.Count; ++i)
						foldOut.Add(true);

				EditorGUILayout.Space();

				if(EditorApplication.isPlaying)
					EditorGUILayout.HelpBox("Can not change while playing", MessageType.Info);
				else

					#region Property Setting

					for(int i = 0; i < instance.paintSet.Count; ++i)
					{
						if(foldOut[i] = Foldout(foldOut[i], string.Format("Material \"{0}\"", materials[i].name)))
						{
							EditorGUI.indentLevel = 0;
							EditorGUILayout.BeginVertical("ProgressBarBack");

							//MainPaint
							EditorGUI.BeginChangeCheck();
							instance.paintSet[i].useMainPaint = EditorGUILayout.Toggle("Use Main Paint", instance.paintSet[i].useMainPaint);
							if(EditorGUI.EndChangeCheck())
								ChangeValue(i, "Use Main Paint", p => p.useMainPaint = instance.paintSet[i].useMainPaint);
							if(instance.paintSet[i].useMainPaint)
							{
								EditorGUI.indentLevel++;
								EditorGUI.BeginChangeCheck();
								instance.paintSet[i].mainTextureName = EditorGUILayout.TextField("MainTexture Property Name", instance.paintSet[i].mainTextureName);
								if(EditorGUI.EndChangeCheck())
									ChangeValue(i, "Main Texture Name", p => p.mainTextureName = instance.paintSet[i].mainTextureName);
								EditorGUI.indentLevel--;
							}

							//NormalPaint
							EditorGUI.BeginChangeCheck();
							instance.paintSet[i].useNormalPaint = EditorGUILayout.Toggle("Use NormalMap Paint", instance.paintSet[i].useNormalPaint);
							if(EditorGUI.EndChangeCheck())
								ChangeValue(i, "Use Normal Paint", p => p.useNormalPaint = instance.paintSet[i].useNormalPaint);
							if(instance.paintSet[i].useNormalPaint)
							{
								EditorGUI.indentLevel++;
								EditorGUI.BeginChangeCheck();
								instance.paintSet[i].normalTextureName = EditorGUILayout.TextField("NormalMap Property Name", instance.paintSet[i].normalTextureName);
								if(EditorGUI.EndChangeCheck())
									ChangeValue(i, "Normal Texture Name", p => p.normalTextureName = instance.paintSet[i].normalTextureName);
								EditorGUI.indentLevel--;
							}

							//HeightPaint
							EditorGUI.BeginChangeCheck();
							instance.paintSet[i].useHeightPaint = EditorGUILayout.Toggle("Use HeightMap Paint", instance.paintSet[i].useHeightPaint);
							if(EditorGUI.EndChangeCheck())
								ChangeValue(i, "Use Height Paint", p => p.useHeightPaint = instance.paintSet[i].useHeightPaint);
							if(instance.paintSet[i].useHeightPaint)
							{
								EditorGUI.indentLevel++;
								EditorGUI.BeginChangeCheck();
								instance.paintSet[i].heightTextureName = EditorGUILayout.TextField("HeightMap Property Name", instance.paintSet[i].heightTextureName);
								if(EditorGUI.EndChangeCheck())
									ChangeValue(i, "Height Texture Name", p => p.heightTextureName = instance.paintSet[i].heightTextureName);
								EditorGUI.indentLevel--;
							}

							EditorGUILayout.EndVertical();
							EditorGUI.indentLevel = 0;
						}
					}

				#endregion Property Setting
			}

			private void ChangeValue(int paintSetIndex, string recordName, Action<PaintSet> assign)
			{
				Undo.RecordObjects(targets, "Change " + recordName);
				foreach(var t in targets.Where(_t => _t is DynamicCanvas).Select(_t => _t as DynamicCanvas))
					if(t.paintSet.Count > paintSetIndex)
					{
						assign(t.paintSet[paintSetIndex]);
						EditorUtility.SetDirty(t);
					}
				EditorSceneManager.MarkSceneDirty(EditorSceneManager.GetActiveScene());
			}

			public bool Foldout(bool foldout, string content)
			{
				var style = new GUIStyle("ShurikenModuleTitle");
				style.font = new GUIStyle(EditorStyles.label).font;
				style.border = new RectOffset(1, 7, 4, 4);
				style.fixedHeight = 28;
				style.contentOffset = new Vector2(20f, -2f);

				var rect = GUILayoutUtility.GetRect(16f, 22f, style);
				GUI.Box(rect, content, style);

				var e = Event.current;

				var toggleRect = new Rect(rect.x + 4f, rect.y + 5f, 13f, 13f);
				if(e.type == EventType.Repaint)
				{
					EditorStyles.foldout.Draw(toggleRect, false, false, foldout, false);
				}

				if(e.type == EventType.MouseDown && rect.Contains(e.mousePosition))
				{
					foldout = !foldout;
					e.Use();
				}

				return foldout;
			}
		}

#endif

		#endregion CustomEditor
	}
}