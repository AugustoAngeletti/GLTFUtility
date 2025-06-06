using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Newtonsoft.Json;
using UnityEngine;
using UnityEngine.Networking;
using UnityEngine.Scripting;

namespace Siccity.GLTFUtility
{
	// https://github.com/KhronosGroup/glTF/blob/master/specification/2.0/README.md#image
	[Preserve]
	public class GLTFImage
	{
		/// <summary>
		/// The uri of the image.
		/// Relative paths are relative to the .gltf file.
		/// Instead of referencing an external file, the uri can also be a data-uri.
		/// The image format must be jpg or png.
		/// </summary>
		public string uri;
		/// <summary> Either "image/jpeg" or "image/png" </summary>
		public string mimeType;
		public int? bufferView;
		public string name;

		public class ImportResult
		{
			public byte[] bytes;
			public string path;

			public ImportResult(byte[] bytes, string path = null)
			{
				this.bytes = bytes;
				this.path = path;
			}

			public IEnumerator CreateTextureAsync(bool linear, Action<Texture2D> onFinish, Action<float> onProgress = null)
			{
				if (!string.IsNullOrEmpty(path))
				{
#if UNITY_EDITOR
					// Load textures from asset database if we can
					Texture2D assetTexture = UnityEditor.AssetDatabase.LoadAssetAtPath(path, typeof(Texture2D)) as Texture2D;
					if (assetTexture != null)
					{
						onFinish(assetTexture);
						if (onProgress != null) onProgress(1f);
						yield break;
					}
#endif

#if !UNITY_EDITOR && (UNITY_ANDROID || UNITY_IOS)
					path = "File://" + path;
#endif
					// TODO: Support linear/sRGB textures
					using (UnityWebRequest uwr = UnityWebRequestTexture.GetTexture(path, true))
					{
						UnityWebRequestAsyncOperation operation = uwr.SendWebRequest();
						float progress = 0;
						while (!operation.isDone)
						{
							if (progress != uwr.downloadProgress)
							{
								if (onProgress != null) onProgress(uwr.downloadProgress);
							}
							yield return null;
						}
						if (onProgress != null) onProgress(1f);

#if UNITY_2020_2_OR_NEWER
						if(uwr.result == UnityWebRequest.Result.ConnectionError ||
							uwr.result == UnityWebRequest.Result.ProtocolError)
#else
						if (uwr.isNetworkError || uwr.isHttpError)
#endif
						{
							Debug.LogError("GLTFImage.cs ToTexture2D() ERROR: " + uwr.error);
						}
						else
						{
							Texture2D tex = DownloadHandlerTexture.GetContent(uwr);
							tex.name = Path.GetFileNameWithoutExtension(path);
							onFinish(tex);
						}
						uwr.Dispose();
					}
				}
				else
				{
					Texture2D tex = new Texture2D(2, 2, TextureFormat.ARGB32, true, linear);
					if (!tex.LoadImage(bytes))
					{
						Debug.Log("mimeType not supported");
						yield break;
					}
					else onFinish(tex);
				}
			}
		}

		public class ImportTask : Importer.ImportTask<ImportResult[]>
		{
			public ImportTask(List<GLTFImage> images, string directoryRoot, GLTFBufferView.ImportTask bufferViewTask) : base(bufferViewTask)
			{
				task = new Task(() => {
					// No images
					if (images == null) return;

					Result = new ImportResult[images.Count];
					for (int i = 0; i < images.Count; i++)
					{
						string fullUri = directoryRoot + images[i].uri;
						if (!string.IsNullOrEmpty(images[i].uri))
						{
							if (File.Exists(fullUri))
							{
								// If the file is found at fullUri, read it
								byte[] bytes = File.ReadAllBytes(fullUri);
								Result[i] = new ImportResult(bytes, fullUri);
							}
							else if (images[i].uri.StartsWith("data:"))
							{
								// If the image is embedded, find its Base64 content and save as byte array
								string content = images[i].uri.Split(',').Last();
								byte[] imageBytes = Convert.FromBase64String(content);
								Result[i] = new ImportResult(imageBytes);
							}
						}
						else if (images[i].bufferView.HasValue && !string.IsNullOrEmpty(images[i].mimeType))
						{
							GLTFBufferView.ImportResult view = bufferViewTask.Result[images[i].bufferView.Value];
							byte[] bytes = new byte[view.byteLength];
							view.stream.Position = view.byteOffset;
							view.stream.Read(bytes, 0, view.byteLength);
							Result[i] = new ImportResult(bytes);
						}
						else
						{
							Debug.Log("Couldn't find texture at " + fullUri);
						}
					}
				});
			}
		}

		#region Export
		/// <summary> Export image from Unity Texture </summary>
		public static GLTFImage CreateFromTexture(Texture texture)
		{
			GLTFImage gltfImage = new GLTFImage();
			gltfImage.name = texture.name;

			try
			{
				// Convert texture to Texture2D if needed
				Texture2D texture2D = ConvertToTexture2D(texture);

				if (texture2D != null)
				{
					// Ensure texture is readable
					Texture2D readableTexture = MakeTextureReadable(texture2D);

					// Encode to PNG (better quality, supports alpha)
					byte[] textureData = readableTexture.EncodeToPNG();
					gltfImage.mimeType = "image/png";

					// Create data URI for embedded texture
					string base64 = Convert.ToBase64String(textureData);
					gltfImage.uri = $"data:{gltfImage.mimeType};base64,{base64}";

					// Clean up temporary texture if created
					if (readableTexture != texture2D)
					{
						if (Application.isPlaying)
						{
							UnityEngine.Object.Destroy(readableTexture);
						}
						else
						{
							UnityEngine.Object.DestroyImmediate(readableTexture);
						}
					}
				}
			}
			catch (Exception e)
			{
				Debug.LogError($"Failed to convert texture {texture.name}: {e.Message}");
				CreateFallbackTexture(gltfImage);
			}

			return gltfImage;
		}

		private static Texture2D ConvertToTexture2D(Texture texture)
		{
			if (texture is Texture2D)
			{
				return texture as Texture2D;
			}

			if (texture is RenderTexture)
			{
				return ConvertRenderTexture(texture as RenderTexture);
			}

			// For other texture types, try to create a basic representation
			return CreateTextureFromSource(texture);
		}

		private static Texture2D ConvertRenderTexture(RenderTexture renderTexture)
		{
			// Save current active render texture
			RenderTexture currentActive = RenderTexture.active;

			try
			{
				// Set the render texture as active
				RenderTexture.active = renderTexture;

				// Create new Texture2D and read pixels
				Texture2D texture2D = new Texture2D(renderTexture.width, renderTexture.height,
					TextureFormat.RGBA32, false);
				texture2D.ReadPixels(new Rect(0, 0, renderTexture.width, renderTexture.height), 0, 0);
				texture2D.Apply();

				return texture2D;
			}
			finally
			{
				// Restore previous active render texture
				RenderTexture.active = currentActive;
			}
		}

		private static Texture2D CreateTextureFromSource(Texture source)
		{
			// Create a simple colored texture as fallback
			Texture2D fallback = new Texture2D(256, 256, TextureFormat.RGBA32, false);
			Color[] pixels = new Color[256 * 256];

			// Fill with magenta to indicate missing texture
			for (int i = 0; i < pixels.Length; i++)
			{
				pixels[i] = Color.magenta;
			}

			fallback.SetPixels(pixels);
			fallback.Apply();
			fallback.name = source.name + "_fallback";

			return fallback;
		}

		private static Texture2D MakeTextureReadable(Texture2D texture)
		{
			// Check if texture is already readable
			try
			{
				texture.GetPixel(0, 0);
				return texture; // Already readable
			}
			catch
			{
				// Texture is not readable, need to copy it
			}

			// Create a temporary RenderTexture
			RenderTexture renderTexture = RenderTexture.GetTemporary(
				texture.width, texture.height, 0, RenderTextureFormat.ARGB32);

			// Save current active render texture
			RenderTexture currentActive = RenderTexture.active;

			try
			{
				// Copy the texture to render texture
				Graphics.Blit(texture, renderTexture);
				RenderTexture.active = renderTexture;

				// Create readable texture and read pixels
				Texture2D readableTexture = new Texture2D(texture.width, texture.height,
					TextureFormat.RGBA32, false);
				readableTexture.ReadPixels(new Rect(0, 0, texture.width, texture.height), 0, 0);
				readableTexture.Apply();
				readableTexture.name = texture.name;

				return readableTexture;
			}
			finally
			{
				// Clean up
				RenderTexture.active = currentActive;
				RenderTexture.ReleaseTemporary(renderTexture);
			}
		}

		private static void CreateFallbackTexture(GLTFImage gltfImage)
		{
			// Create a 1x1 white texture as fallback
			Texture2D fallback = new Texture2D(1, 1, TextureFormat.RGBA32, false);
			fallback.SetPixel(0, 0, Color.white);
			fallback.Apply();

			byte[] textureData = fallback.EncodeToPNG();
			gltfImage.mimeType = "image/png";

			string base64 = Convert.ToBase64String(textureData);
			gltfImage.uri = $"data:{gltfImage.mimeType};base64,{base64}";

			if (Application.isPlaying)
			{
				UnityEngine.Object.Destroy(fallback);
			}
			else
			{
				UnityEngine.Object.DestroyImmediate(fallback);
			}
		}
		#endregion
	}
}