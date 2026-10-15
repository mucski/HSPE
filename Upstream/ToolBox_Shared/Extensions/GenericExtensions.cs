using System;
using System.Collections.Generic;
using System.Text;
using UnityEngine;
using static ToolBox.Logger;

namespace ToolBox.Extensions
{
	internal static class GenericExtensions
	{
		/// <summary>
		/// Checks if given text is present in source text; comparison is case-insensitive.
		/// </summary>
		/// <param name="src">The source text</param>
		/// <param name="text">The text to search</param>
		/// <returns>True if text is not null and is is found, otherwise False. If text is an empty string, method returns True.</returns>
		public static bool HasText(this string src, string text)
		{
			return text != null && src.IndexOf(text, StringComparison.OrdinalIgnoreCase) != -1;
		}

		/// <summary>
		/// Check if a <see cref="UnityEngine.Object"/>'s name contains a given text.
		/// </summary>
		/// <param name="obj">The material whose name to check</param>
		/// <param name="text">The text to look for</param>
		/// <returns>True if <see cref="UnityEngine.Object"/> exists and its name contains the given text, otherwise False.</returns>
		public static bool NameContains(this UnityEngine.Object obj, string text)
		{
			return obj && obj.name.HasText(text);
		}



		private static MaterialPropertyBlock _materialProperty;
		private static MaterialPropertyBlock MaterialPropertyBlock => _materialProperty ?? (_materialProperty = new MaterialPropertyBlock());

		/*
		/// <summary>
		/// Set material property using PropertyBlock. Applies to all of renderer's materials.
		/// </summary>
		/// <typeparam name="T"></typeparam>
		/// <param name="renderer">The renderer whose properties should be edited.</param>
		/// <param name="propertyHash">Shader Property Id, can be obtained using <see cref="Shader.PropertyToID"/></param>
		/// <param name="value"></param>
		public static void SetProperty<T>(this Renderer renderer, int propertyHash, T value)
		{
			renderer.GetPropertyBlock(MaterialPropertyBlock);

			if (value is Color c)
			{
				MaterialPropertyBlock.SetColor(propertyHash, c);
			}
			else if (value is float f)
			{
				MaterialPropertyBlock.SetFloat(propertyHash, f);
			}
			else if (value is Matrix4x4 m)
			{
				MaterialPropertyBlock.SetMatrix(propertyHash, m);
			}
			else if (value is Texture t)
			{
				MaterialPropertyBlock.SetTexture(propertyHash, t);
			}
			else if (value is Vector4 v)
			{
				MaterialPropertyBlock.SetVector(propertyHash, v);
			}
			renderer.SetPropertyBlock(MaterialPropertyBlock);
		}*/

		public static void SetProperty<T>(this Renderer renderer, int propertyHash, T value)
		{
			if (value is Color c)
			{
				renderer.SetProperty(propertyHash, c);
			}
			else if (value is float f)
			{
				renderer.SetProperty(propertyHash, f);
			}
			else if (value is Matrix4x4 m)
			{
				renderer.SetProperty(propertyHash, m);
			}
			else if (value is Texture t)
			{
				renderer.SetProperty(propertyHash, t);
			}
			else if (value is Vector4 v)
			{
				renderer.SetProperty(propertyHash, v);
			}
			else throw new NotSupportedException($"Unsupported value type: {typeof(T)} is not a valid shader property type!");
		}

		public static void SetProperty(this Renderer renderer, int propertyHash, Color value)
		{
			renderer.GetPropertyBlock(MaterialPropertyBlock);
			MaterialPropertyBlock.SetColor(propertyHash, value);
			renderer.SetPropertyBlock(MaterialPropertyBlock);
		}
		public static void SetProperty(this Renderer renderer, int propertyHash, float value)
		{
			renderer.GetPropertyBlock(MaterialPropertyBlock);
			MaterialPropertyBlock.SetFloat(propertyHash, value);
			renderer.SetPropertyBlock(MaterialPropertyBlock);
		}
		public static void SetProperty(this Renderer renderer, int propertyHash, Matrix4x4 value)
		{
			renderer.GetPropertyBlock(MaterialPropertyBlock);
			MaterialPropertyBlock.SetMatrix(propertyHash, value);
			renderer.SetPropertyBlock(MaterialPropertyBlock);
		}
		public static void SetProperty(this Renderer renderer, int propertyHash, Texture value)
		{
			renderer.GetPropertyBlock(MaterialPropertyBlock);
			MaterialPropertyBlock.SetTexture(propertyHash, value);
			renderer.SetPropertyBlock(MaterialPropertyBlock);
		}
		public static void SetProperty(this Renderer renderer, int propertyHash, Vector4 value)
		{
			renderer.GetPropertyBlock(MaterialPropertyBlock);
			MaterialPropertyBlock.SetVector(propertyHash, value);
			renderer.SetPropertyBlock(MaterialPropertyBlock);
		}

		public static void Destroy(this UnityEngine.Object o)
		{
			if (o)
			{
				UnityEngine.Object.Destroy(o);
			}
		}

		/// <summary>
		/// Try setting weight for the blend shape at the specified index; returns False if unsuccessful.
		/// </summary>
		/// <param name="r">Renderer for which to apply the blend shape changes</param>
		/// <param name="index">BlendShape index whose value to change</param>
		/// <param name="weight">The BlendShape value to apply</param>
		/// <returns>False if <paramref name="index"/> is out of bounds or renderer is null, otherwise True.</returns>
		public static bool TrySetBlendShapeWeight(this SkinnedMeshRenderer r, int index, float weight)
		{
			if (index >= 0 && index < r.sharedMesh.blendShapeCount && r)
			{
				r.SetBlendShapeWeight(index, weight);
				return true;
			}
			return false;
		}
	}
}
