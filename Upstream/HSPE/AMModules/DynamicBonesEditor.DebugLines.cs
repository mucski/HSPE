using System.Collections.Generic;
#if HONEYSELECT || PLAYHOME || KOIKATSU
#endif

namespace HSPE.AMModules
{
	public partial class DynamicBonesEditor
	{
		private class DebugLines
		{
			private readonly List<DebugDynamicBone> _debugLines = new List<DebugDynamicBone>();

			public void Draw(List<DynamicBone> dynamicBones, DynamicBone target, Dictionary<DynamicBone, DynamicBoneData> dirtyDynamicBones)
			{
				int i = 0;
				foreach (DynamicBone db in dynamicBones)
				{
					if (db.m_Root == null)
						continue;
					DebugDynamicBone debug;
					if (i < _debugLines.Count)
					{
						debug = _debugLines[i];
						debug.SetActive(true);
					}
					else
					{
						debug = new DebugDynamicBone(db);
						_debugLines.Add(debug);
					}
					debug.Draw(db, db == target, dirtyDynamicBones.ContainsKey(db));
					++i;
				}
				for (; i < _debugLines.Count; ++i)
				{
					DebugDynamicBone debug = _debugLines[i];
					if (debug.IsActive())
						debug.SetActive(false);
				}
			}

			public void Destroy()
			{
				if (_debugLines != null)
					for (int i = 0; i < _debugLines.Count; i++)
					{
						_debugLines[i].Destroy();
					}
			}

			public void SetActive(bool active, int limit = -1)
			{
				if (_debugLines == null)
					return;
				if (limit == -1)
					limit = _debugLines.Count;
				for (int i = 0; i < limit; i++)
				{
					_debugLines[i].SetActive(active);
				}
			}
		}
	}
}
