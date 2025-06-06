using System;
using Newtonsoft.Json;
using UnityEngine;
using UnityEngine.Scripting;

namespace Siccity.GLTFUtility
{
	// https://github.com/KhronosGroup/glTF/blob/master/specification/2.0/README.md#sampler
	[Preserve]
	public class GLTFSampler
	{
		public int magFilter = 9729; // LINEAR
		public int minFilter = 9987; // LINEAR_MIPMAP_LINEAR  
		public int wrapS = 10497;    // REPEAT
		public int wrapT = 10497;    // REPEAT
		public string name;

		#region Export
		/// <summary> Create sampler from Unity texture settings </summary>
		public static GLTFSampler CreateFromTexture(Texture texture)
		{
			GLTFSampler sampler = new GLTFSampler();

			if (texture is Texture2D)
			{
				Texture2D tex2D = texture as Texture2D;

				// Set filter mode
				SetFilterMode(sampler, tex2D.filterMode);

				// Set wrap mode
				SetWrapMode(sampler, tex2D.wrapMode);
			}
			else
			{
				// Default values for other texture types
				sampler.magFilter = 9729; // LINEAR
				sampler.minFilter = 9987; // LINEAR_MIPMAP_LINEAR
				sampler.wrapS = 10497;    // REPEAT
				sampler.wrapT = 10497;    // REPEAT
			}

			return sampler;
		}

		private static void SetFilterMode(GLTFSampler sampler, FilterMode filterMode)
		{
			switch (filterMode)
			{
				case FilterMode.Point:
					sampler.magFilter = 9728; // NEAREST
					sampler.minFilter = 9984; // NEAREST_MIPMAP_NEAREST
					break;
				case FilterMode.Bilinear:
					sampler.magFilter = 9729; // LINEAR
					sampler.minFilter = 9985; // LINEAR_MIPMAP_NEAREST
					break;
				case FilterMode.Trilinear:
				default:
					sampler.magFilter = 9729; // LINEAR
					sampler.minFilter = 9987; // LINEAR_MIPMAP_LINEAR
					break;
			}
		}

		private static void SetWrapMode(GLTFSampler sampler, TextureWrapMode wrapMode)
		{
			int glWrapMode;
			switch (wrapMode)
			{
				case TextureWrapMode.Clamp:
					glWrapMode = 33071; // CLAMP_TO_EDGE
					break;
				case TextureWrapMode.Mirror:
					glWrapMode = 33648; // MIRRORED_REPEAT
					break;
				case TextureWrapMode.Repeat:
				default:
					glWrapMode = 10497; // REPEAT
					break;
			}

			sampler.wrapS = glWrapMode;
			sampler.wrapT = glWrapMode;
		}
		#endregion
	}
}