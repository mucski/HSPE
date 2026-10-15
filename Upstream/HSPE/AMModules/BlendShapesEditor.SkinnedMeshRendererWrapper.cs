using System.Collections.Generic;
using UnityEngine;

namespace HSPE.AMModules
{
	public partial class BlendShapesEditor
	{
		private class SkinnedMeshRendererWrapper
		{
			public SkinnedMeshRenderer renderer;
			public List<SkinnedMeshRendererWrapper> links;
		}
	}
}