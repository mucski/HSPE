using System.Collections.Generic;

namespace HSPE.AMModules
{
	public partial class BlendShapesEditor
	{
		private class SkinnedMeshRendererData
		{
			public readonly Dictionary<int, BlendShapeData> dirtyBlendShapes = new Dictionary<int, BlendShapeData>();
			public string path;

			public SkinnedMeshRendererData()
			{
			}

			public SkinnedMeshRendererData(SkinnedMeshRendererData other)
			{
				foreach (KeyValuePair<int, BlendShapeData> kvp in other.dirtyBlendShapes)
				{
					dirtyBlendShapes.Add(kvp.Key, new BlendShapeData() { weight = kvp.Value.weight, originalWeight = kvp.Value.originalWeight });
				}
			}
		}
	}
}