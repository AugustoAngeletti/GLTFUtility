using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json;
using UnityEngine;

namespace Siccity.GLTFUtility
{
	/// <summary> API used for exporting .gltf and .glb files </summary>
	public static class Exporter
	{

		#region Public Export Methods

#if UNITY_EDITOR
		[UnityEditor.MenuItem("File/Export Selection/.glb")]
		public static void ExportSelectedGLB()
		{
			if (UnityEditor.Selection.activeGameObject != null)
			{
				string path = UnityEditor.EditorUtility.SaveFilePanel("Save GLB", "",
					UnityEditor.Selection.activeGameObject.name + ".glb", "glb");
				if (!string.IsNullOrEmpty(path))
				{
					ExportGLB(UnityEditor.Selection.activeGameObject, path);
				}
			}
		}

		[UnityEditor.MenuItem("File/Export Selection/.gltf")]
		public static void ExportSelectedGLTF()
		{
			if (UnityEditor.Selection.activeGameObject != null)
			{
				string path = UnityEditor.EditorUtility.SaveFilePanel("Save glTF", "",
					UnityEditor.Selection.activeGameObject.name + ".gltf", "gltf");
				if (!string.IsNullOrEmpty(path))
				{
					ExportGLTF(UnityEditor.Selection.activeGameObject, path);
				}
			}
		}
#endif

		/// <summary> Export GameObject to GLB format </summary>
		public static void ExportGLB(GameObject root, string filepath)
		{
			try
			{
				GLTFObject gltfObject = CreateGLTFObject(root.transform);

				// Create binary GLB file
				byte[] glbData = CreateGLBFile(gltfObject);
				File.WriteAllBytes(filepath, glbData);

				Debug.Log($"Successfully exported GLB to: {filepath}");
			}
			catch (Exception e)
			{
				Debug.LogError($"Failed to export GLB: {e.Message}\n{e.StackTrace}");
			}
		}

		/// <summary> Export GameObject to glTF format </summary>
		public static void ExportGLTF(GameObject root, string filepath)
		{
			try
			{
				GLTFObject gltfObject = CreateGLTFObject(root.transform);

				// Create JSON file and external resources
				string directory = Path.GetDirectoryName(filepath);
				string filename = Path.GetFileNameWithoutExtension(filepath);

				CreateGLTFFiles(gltfObject, directory, filename);

				Debug.Log($"Successfully exported glTF to: {filepath}");
			}
			catch (Exception e)
			{
				Debug.LogError($"Failed to export glTF: {e.Message}\n{e.StackTrace}");
			}
		}

		#endregion

		#region Core Export Logic

		public static GLTFObject CreateGLTFObject(Transform root)
		{
			GLTFObject gltfObject = new GLTFObject();

			// Asset info
			GLTFAsset asset = new GLTFAsset()
			{
				generator = "GLTFUtility Enhanced by Unity " + Application.unityVersion,
				version = "2.0"
			};
			gltfObject.asset = asset;

			// Initialize collections
			List<GLTFBuffer> buffers = new List<GLTFBuffer>();
			List<GLTFBufferView> bufferViews = new List<GLTFBufferView>();
			List<GLTFAccessor> accessors = new List<GLTFAccessor>();
			List<GLTFImage> images = new List<GLTFImage>();
			List<GLTFTexture> textures = new List<GLTFTexture>();
			List<GLTFMaterial> materials = new List<GLTFMaterial>();
			List<GLTFSampler> samplers = new List<GLTFSampler>();

			// Export nodes hierarchy
			List<GLTFNode.ExportResult> nodes = GLTFNode.Export(root);

			// Export meshes and collect materials
			List<GLTFMesh.ExportResult> meshes = ExportMeshes(nodes, buffers, bufferViews, accessors);

			// Export materials and textures
			ExportMaterialsAndTextures(nodes, materials, images, textures, samplers);

			// Update material indices in primitives
			UpdateMaterialIndices(meshes, nodes, materials);

			// Assign to glTF object
			gltfObject.scene = 0;
			gltfObject.scenes = new List<GLTFScene> {
				new GLTFScene {
					nodes = GetRootNodeIndices(nodes),
					name = root.name + "_scene"
				}
			};

			gltfObject.nodes = nodes.Cast<GLTFNode>().ToList();
			gltfObject.meshes = meshes.Cast<GLTFMesh>().ToList();
			gltfObject.materials = materials;
			gltfObject.textures = textures;
			gltfObject.images = images;
			if (samplers.Count > 0) gltfObject.samplers = samplers;
			gltfObject.accessors = accessors;
			gltfObject.bufferViews = bufferViews;
			gltfObject.buffers = buffers;

			return gltfObject;
		}

		private static List<GLTFMesh.ExportResult> ExportMeshes(List<GLTFNode.ExportResult> nodes,
			List<GLTFBuffer> buffers, List<GLTFBufferView> bufferViews, List<GLTFAccessor> accessors)
		{

			List<GLTFMesh.ExportResult> meshes = new List<GLTFMesh.ExportResult>();

			// Create main buffer
			BufferData bufferData = new BufferData();

			for (int i = 0; i < nodes.Count; i++)
			{
				if (nodes[i].filter && nodes[i].filter.sharedMesh)
				{
					Mesh mesh = nodes[i].filter.sharedMesh;
					Material[] nodeMaterials = nodes[i].renderer ? nodes[i].renderer.sharedMaterials : null;

					nodes[i].mesh = meshes.Count;
					GLTFMesh.ExportResult exportedMesh = ExportMesh(mesh, nodeMaterials, bufferData, bufferViews, accessors);
					meshes.Add(exportedMesh);
				}
			}

			// Add buffer to collection
			if (bufferData.data.Count > 0)
			{
				GLTFBuffer buffer = new GLTFBuffer();
				buffer.byteLength = bufferData.data.Count;
				buffer.data = bufferData.data.ToArray();
				buffers.Add(buffer);
			}

			return meshes;
		}

		private static GLTFMesh.ExportResult ExportMesh(Mesh mesh, Material[] materials,
			BufferData bufferData, List<GLTFBufferView> bufferViews, List<GLTFAccessor> accessors)
		{

			GLTFMesh.ExportResult result = new GLTFMesh.ExportResult();
			result.name = mesh.name;
			result.mesh = mesh;
			result.primitives = new List<GLTFPrimitive>();

			// Export each submesh as a primitive
			for (int submesh = 0; submesh < mesh.subMeshCount; submesh++)
			{
				GLTFPrimitive primitive = ExportPrimitive(mesh, submesh, bufferData, bufferViews, accessors);
				result.primitives.Add(primitive);
			}

			return result;
		}

		private static GLTFPrimitive ExportPrimitive(Mesh mesh, int submeshIndex,
			BufferData bufferData, List<GLTFBufferView> bufferViews, List<GLTFAccessor> accessors)
		{

			GLTFPrimitive primitive = new GLTFPrimitive();
			primitive.attributes = new GLTFPrimitive.GLTFAttributes();

			// Export vertices
			Vector3[] vertices = mesh.vertices;
			if (vertices != null && vertices.Length > 0)
			{
				// Convert Unity coordinates (left-handed) to glTF (right-handed)
				for (int i = 0; i < vertices.Length; i++)
				{
					vertices[i].x = -vertices[i].x;
				}
				primitive.attributes.POSITION = CreateAccessor(vertices, bufferData, bufferViews, accessors);
			}

			// Export normals
			Vector3[] normals = mesh.normals;
			if (normals != null && normals.Length > 0)
			{
				for (int i = 0; i < normals.Length; i++)
				{
					normals[i].x = -normals[i].x;
				}
				primitive.attributes.NORMAL = CreateAccessor(normals, bufferData, bufferViews, accessors);
			}

			// Export UVs
			Vector2[] uvs = mesh.uv;
			if (uvs != null && uvs.Length > 0)
			{
				// Flip Y coordinate for glTF
				for (int i = 0; i < uvs.Length; i++)
				{
					uvs[i].y = 1f - uvs[i].y;
				}
				primitive.attributes.TEXCOORD_0 = CreateAccessor(uvs, bufferData, bufferViews, accessors);
			}

			// Export vertex colors
			Color[] colors = mesh.colors;
			if (colors != null && colors.Length > 0)
			{
				primitive.attributes.COLOR_0 = CreateAccessor(colors, bufferData, bufferViews, accessors);
			}

			// Export indices
			int[] indices = mesh.GetTriangles(submeshIndex);
			if (indices != null && indices.Length > 0)
			{
				// Flip triangle winding order
				for (int i = 0; i < indices.Length; i += 3)
				{
					int temp = indices[i];
					indices[i] = indices[i + 2];
					indices[i + 2] = temp;
				}

				primitive.indices = CreateAccessor(indices, bufferData, bufferViews, accessors);
			}

			primitive.mode = RenderingMode.TRIANGLES;
			return primitive;
		}

		private static int CreateAccessor<T>(T[] data, BufferData bufferData,
			List<GLTFBufferView> bufferViews, List<GLTFAccessor> accessors)
		{

			byte[] serializedData = SerializeData(data);

			// Create buffer view
			GLTFBufferView bufferView = new GLTFBufferView();
			bufferView.buffer = 0; // Single buffer
			bufferView.byteOffset = bufferData.data.Count;
			bufferView.byteLength = serializedData.Length;

			// Add data to buffer with padding
			bufferData.AddData(serializedData);

			int bufferViewIndex = bufferViews.Count;
			bufferViews.Add(bufferView);

			// Create accessor
			GLTFAccessor accessor = new GLTFAccessor();
			accessor.bufferView = bufferViewIndex;
			accessor.byteOffset = 0;
			accessor.count = data.Length;

			// Set accessor type based on data type
			if (typeof(T) == typeof(Vector3))
			{
				accessor.componentType = GLType.FLOAT;
				accessor.type = AccessorType.VEC3;

				Vector3[] vec3Data = data as Vector3[];
				Vector3 min = vec3Data[0];
				Vector3 max = vec3Data[0];
				for (int i = 1; i < vec3Data.Length; i++)
				{
					min = Vector3.Min(min, vec3Data[i]);
					max = Vector3.Max(max, vec3Data[i]);
				}
				accessor.min = new float[] { min.x, min.y, min.z };
				accessor.max = new float[] { max.x, max.y, max.z };
			}
			else if (typeof(T) == typeof(Vector2))
			{
				accessor.componentType = GLType.FLOAT;
				accessor.type = AccessorType.VEC2;
			}
			else if (typeof(T) == typeof(Color))
			{
				accessor.componentType = GLType.FLOAT;
				accessor.type = AccessorType.VEC4;
			}
			else if (typeof(T) == typeof(int))
			{
				accessor.componentType = data.Length > 65535 ? GLType.UNSIGNED_INT : GLType.UNSIGNED_SHORT;
				accessor.type = AccessorType.SCALAR;
			}

			int accessorIndex = accessors.Count;
			accessors.Add(accessor);
			return accessorIndex;
		}

		private static byte[] SerializeData<T>(T[] data)
		{
			if (typeof(T) == typeof(Vector3))
			{
				Vector3[] vec3Data = data as Vector3[];
				byte[] result = new byte[vec3Data.Length * 12];
				for (int i = 0; i < vec3Data.Length; i++)
				{
					Buffer.BlockCopy(BitConverter.GetBytes(vec3Data[i].x), 0, result, i * 12, 4);
					Buffer.BlockCopy(BitConverter.GetBytes(vec3Data[i].y), 0, result, i * 12 + 4, 4);
					Buffer.BlockCopy(BitConverter.GetBytes(vec3Data[i].z), 0, result, i * 12 + 8, 4);
				}
				return result;
			}
			else if (typeof(T) == typeof(Vector2))
			{
				Vector2[] vec2Data = data as Vector2[];
				byte[] result = new byte[vec2Data.Length * 8];
				for (int i = 0; i < vec2Data.Length; i++)
				{
					Buffer.BlockCopy(BitConverter.GetBytes(vec2Data[i].x), 0, result, i * 8, 4);
					Buffer.BlockCopy(BitConverter.GetBytes(vec2Data[i].y), 0, result, i * 8 + 4, 4);
				}
				return result;
			}
			else if (typeof(T) == typeof(Color))
			{
				Color[] colorData = data as Color[];
				byte[] result = new byte[colorData.Length * 16];
				for (int i = 0; i < colorData.Length; i++)
				{
					Buffer.BlockCopy(BitConverter.GetBytes(colorData[i].r), 0, result, i * 16, 4);
					Buffer.BlockCopy(BitConverter.GetBytes(colorData[i].g), 0, result, i * 16 + 4, 4);
					Buffer.BlockCopy(BitConverter.GetBytes(colorData[i].b), 0, result, i * 16 + 8, 4);
					Buffer.BlockCopy(BitConverter.GetBytes(colorData[i].a), 0, result, i * 16 + 12, 4);
				}
				return result;
			}
			else if (typeof(T) == typeof(int))
			{
				int[] intData = data as int[];
				bool use32Bit = intData.Any(x => x > ushort.MaxValue);

				if (use32Bit)
				{
					byte[] result = new byte[intData.Length * 4];
					for (int i = 0; i < intData.Length; i++)
					{
						Buffer.BlockCopy(BitConverter.GetBytes((uint)intData[i]), 0, result, i * 4, 4);
					}
					return result;
				}
				else
				{
					byte[] result = new byte[intData.Length * 2];
					for (int i = 0; i < intData.Length; i++)
					{
						Buffer.BlockCopy(BitConverter.GetBytes((ushort)intData[i]), 0, result, i * 2, 2);
					}
					return result;
				}
			}

			throw new NotSupportedException($"Data type {typeof(T)} is not supported");
		}

		private static void ExportMaterialsAndTextures(List<GLTFNode.ExportResult> nodes,
			List<GLTFMaterial> materials, List<GLTFImage> images, List<GLTFTexture> textures, List<GLTFSampler> samplers)
		{

			// Collect unique materials from nodes
			HashSet<Material> uniqueMaterials = new HashSet<Material>();
			foreach (var node in nodes)
			{
				if (node.renderer && node.renderer.sharedMaterials != null)
				{
					foreach (Material mat in node.renderer.sharedMaterials)
					{
						if (mat != null) uniqueMaterials.Add(mat);
					}
				}
			}

			// Convert Unity materials to glTF materials
			Dictionary<Texture, int> textureToIndex = new Dictionary<Texture, int>();

			foreach (Material unityMat in uniqueMaterials)
			{
				GLTFMaterial gltfMat = GLTFMaterial.CreateFromUnityMaterial(unityMat);

				// Process textures for this material
				ProcessMaterialTextures(unityMat, gltfMat, textureToIndex, images, textures, samplers);

				materials.Add(gltfMat);
			}
		}

		private static void ProcessMaterialTextures(Material unityMat, GLTFMaterial gltfMat,
			Dictionary<Texture, int> textureToIndex, List<GLTFImage> images, List<GLTFTexture> textures, List<GLTFSampler> samplers)
		{

			// Main texture
			if (unityMat.HasProperty("_MainTex") && unityMat.GetTexture("_MainTex") != null)
			{
				Texture mainTex = unityMat.GetTexture("_MainTex");
				int textureIndex = GetOrCreateTexture(mainTex, textureToIndex, images, textures, samplers);

				if (gltfMat.pbrMetallicRoughness.baseColorTexture == null)
				{
					gltfMat.pbrMetallicRoughness.baseColorTexture = new GLTFMaterial.TextureInfo();
				}
				gltfMat.pbrMetallicRoughness.baseColorTexture.index = textureIndex;
			}

			// Normal map
			if (unityMat.HasProperty("_BumpMap") && unityMat.GetTexture("_BumpMap") != null)
			{
				Texture normalTex = unityMat.GetTexture("_BumpMap");
				int textureIndex = GetOrCreateTexture(normalTex, textureToIndex, images, textures, samplers);

				if (gltfMat.normalTexture == null)
				{
					gltfMat.normalTexture = new GLTFMaterial.TextureInfo();
				}
				gltfMat.normalTexture.index = textureIndex;
			}

			// Metallic/Roughness map
			if (unityMat.HasProperty("_MetallicGlossMap") && unityMat.GetTexture("_MetallicGlossMap") != null)
			{
				Texture metallicTex = unityMat.GetTexture("_MetallicGlossMap");
				int textureIndex = GetOrCreateTexture(metallicTex, textureToIndex, images, textures, samplers);

				if (gltfMat.pbrMetallicRoughness.metallicRoughnessTexture == null)
				{
					gltfMat.pbrMetallicRoughness.metallicRoughnessTexture = new GLTFMaterial.TextureInfo();
				}
				gltfMat.pbrMetallicRoughness.metallicRoughnessTexture.index = textureIndex;
			}

			// Emission map
			if (unityMat.HasProperty("_EmissionMap") && unityMat.GetTexture("_EmissionMap") != null)
			{
				Texture emissionTex = unityMat.GetTexture("_EmissionMap");
				int textureIndex = GetOrCreateTexture(emissionTex, textureToIndex, images, textures, samplers);

				if (gltfMat.emissiveTexture == null)
				{
					gltfMat.emissiveTexture = new GLTFMaterial.TextureInfo();
				}
				gltfMat.emissiveTexture.index = textureIndex;
			}
		}

		private static int GetOrCreateTexture(Texture unityTexture, Dictionary<Texture, int> textureToIndex,
			List<GLTFImage> images, List<GLTFTexture> textures, List<GLTFSampler> samplers)
		{

			if (textureToIndex.ContainsKey(unityTexture))
			{
				return textureToIndex[unityTexture];
			}

			// Create image
			GLTFImage image = GLTFImage.CreateFromTexture(unityTexture);
			images.Add(image);
			int imageIndex = images.Count - 1;

			// Create sampler
			GLTFSampler sampler = GLTFSampler.CreateFromTexture(unityTexture);
			samplers.Add(sampler);
			int samplerIndex = samplers.Count - 1;

			// Create texture
			GLTFTexture texture = new GLTFTexture();
			texture.source = imageIndex;
			texture.sampler = samplerIndex;
			texture.name = unityTexture.name;
			textures.Add(texture);

			int textureIndex = textures.Count - 1;
			textureToIndex[unityTexture] = textureIndex;

			return textureIndex;
		}

		private static void UpdateMaterialIndices(List<GLTFMesh.ExportResult> meshes,
			List<GLTFNode.ExportResult> nodes, List<GLTFMaterial> materials)
		{

			Dictionary<string, int> materialNameToIndex = new Dictionary<string, int>();

			// Build material index mapping by name
			for (int i = 0; i < materials.Count; i++)
			{
				if (!string.IsNullOrEmpty(materials[i].name))
				{
					materialNameToIndex[materials[i].name] = i;
				}
			}

			// Update primitive material indices
			foreach (var node in nodes)
			{
				if (node.mesh.HasValue && node.renderer && node.renderer.sharedMaterials != null)
				{
					GLTFMesh.ExportResult mesh = meshes[node.mesh.Value];
					Material[] nodeMaterials = node.renderer.sharedMaterials;

					for (int i = 0; i < mesh.primitives.Count && i < nodeMaterials.Length; i++)
					{
						Material mat = nodeMaterials[i];
						if (mat != null && materialNameToIndex.ContainsKey(mat.name))
						{
							mesh.primitives[i].material = materialNameToIndex[mat.name];
						}
					}
				}
			}
		}

		private static List<int> GetRootNodeIndices(List<GLTFNode.ExportResult> nodes)
		{
			List<int> rootIndices = new List<int>();

			for (int i = 0; i < nodes.Count; i++)
			{
				bool isRoot = true;
				for (int j = 0; j < nodes.Count; j++)
				{
					if (nodes[j].children != null && nodes[j].children.Contains(i))
					{
						isRoot = false;
						break;
					}
				}

				if (isRoot)
				{
					rootIndices.Add(i);
				}
			}

			return rootIndices;
		}

		#endregion

		#region File Creation

		private static byte[] CreateGLBFile(GLTFObject gltfObject)
		{
			JsonSerializerSettings settings = new JsonSerializerSettings
			{
				NullValueHandling = NullValueHandling.Ignore,
				Formatting = Formatting.None
			};

			string json = JsonConvert.SerializeObject(gltfObject, settings);
			byte[] jsonBytes = System.Text.Encoding.UTF8.GetBytes(json);

			// Get binary data
			byte[] binaryData = new byte[0];
			if (gltfObject.buffers != null && gltfObject.buffers.Count > 0)
			{
				GLTFBuffer buffer = gltfObject.buffers[0];
				if (buffer.data != null)
				{
					binaryData = buffer.data;
				}
			}

			using (MemoryStream glbStream = new MemoryStream())
			{
				using (BinaryWriter writer = new BinaryWriter(glbStream))
				{
					// GLB header
					writer.Write(0x46546C67); // "glTF" magic
					writer.Write(2); // version

					// Calculate total length
					int jsonPadding = (4 - (jsonBytes.Length % 4)) % 4;
					int binaryPadding = (4 - (binaryData.Length % 4)) % 4;
					int totalLength = 12 + 8 + jsonBytes.Length + jsonPadding;
					if (binaryData.Length > 0)
					{
						totalLength += 8 + binaryData.Length + binaryPadding;
					}

					writer.Write(totalLength);

					// JSON chunk
					writer.Write(jsonBytes.Length + jsonPadding);
					writer.Write(0x4E4F534A); // "JSON"
					writer.Write(jsonBytes);

					// JSON padding
					for (int i = 0; i < jsonPadding; i++)
					{
						writer.Write((byte)0x20);
					}

					// Binary chunk
					if (binaryData.Length > 0)
					{
						writer.Write(binaryData.Length + binaryPadding);
						writer.Write(0x004E4942); // "BIN\0"
						writer.Write(binaryData);

						for (int i = 0; i < binaryPadding; i++)
						{
							writer.Write((byte)0);
						}
					}
				}

				return glbStream.ToArray();
			}
		}

		private static void CreateGLTFFiles(GLTFObject gltfObject, string directory, string filename)
		{
			if (!Directory.Exists(directory))
			{
				Directory.CreateDirectory(directory);
			}

			// Export textures to separate files if needed
			if (gltfObject.images != null)
			{
				for (int i = 0; i < gltfObject.images.Count; i++)
				{
					GLTFImage image = gltfObject.images[i];
					if (!string.IsNullOrEmpty(image.uri) && image.uri.StartsWith("data:"))
					{
						// Convert embedded image to external file
						string base64Data = image.uri.Split(',')[1];
						byte[] imageData = Convert.FromBase64String(base64Data);

						string extension = image.mimeType == "image/jpeg" ? ".jpg" : ".png";
						string imagePath = Path.Combine(directory, $"{filename}_texture_{i}{extension}");
						File.WriteAllBytes(imagePath, imageData);

						// Update URI to reference external file
						image.uri = $"{filename}_texture_{i}{extension}";
					}
				}
			}

			// Export binary buffer if exists
			if (gltfObject.buffers != null && gltfObject.buffers.Count > 0)
			{
				GLTFBuffer buffer = gltfObject.buffers[0];
				if (buffer.data != null && buffer.data.Length > 0)
				{
					string binPath = Path.Combine(directory, filename + ".bin");
					File.WriteAllBytes(binPath, buffer.data);
					buffer.uri = filename + ".bin";
					buffer.data = null; // Remove data from JSON
				}
			}

			JsonSerializerSettings settings = new JsonSerializerSettings
			{
				NullValueHandling = NullValueHandling.Ignore,
				Formatting = Formatting.Indented
			};

			string json = JsonConvert.SerializeObject(gltfObject, settings);
			string gltfPath = Path.Combine(directory, filename + ".gltf");
			File.WriteAllText(gltfPath, json);
		}

		#endregion
	}

	// Helper class for buffer management
	public class BufferData
	{
		public List<byte> data = new List<byte>();

		public void AddData(byte[] newData)
		{
			// Ensure 4-byte alignment
			while (data.Count % 4 != 0)
			{
				data.Add(0);
			}
			data.AddRange(newData);
		}
	}
}