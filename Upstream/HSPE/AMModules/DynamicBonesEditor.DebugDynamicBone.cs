using ToolBox;
using ToolBox.Extensions;
using UnityEngine;
using Vectrosity;
#if HONEYSELECT || PLAYHOME || KOIKATSU
#endif

namespace HSPE.AMModules
{
	public partial class DynamicBonesEditor
	{
		private class DebugDynamicBone
		{
			private VectorLine _gravity;
			private VectorLine _force;
			private VectorLine _both;
			private VectorLine _circle;

			public DebugDynamicBone(DynamicBone db)
			{
				Transform end = (db.m_Root ?? db.transform).GetFirstLeaf();

				float scale = 10f;
				Vector3 origin = end.position;
				Vector3 final = origin + (db.m_Gravity) * scale;

				_gravity = VectorLine.SetLine(_redColor, origin, final);
				_gravity.endCap = "vector";
				_gravity.lineWidth = 4f;

				origin = final;
				final += db.m_Force * scale;

				_force = VectorLine.SetLine(_blueColor, origin, final);
				_force.endCap = "vector";
				_force.lineWidth = 4f;

				origin = end.position;

				_both = VectorLine.SetLine(_greenColor, origin, final);
				_both.endCap = "vector";
				_both.lineWidth = 4f;

				_circle = VectorLine.SetLine(_greenColor, new Vector3[37]);
				_circle.lineWidth = 4f;
				_circle.MakeCircle(end.position, Camera.main.transform.forward, _dynamicBonesDragRadius);
			}

			public void Draw(DynamicBone db, bool isTarget, bool isDirty)
			{
				Transform end = (db.m_Root ?? db.transform).GetFirstLeaf();

				float scale = 10f;
				Vector3 origin = end.position;
				Vector3 final = origin + (db.m_Gravity) * scale;

				_gravity.points3[0] = origin;
				_gravity.points3[1] = final;

				origin = final;
				final += db.m_Force * scale;

				_force.points3[0] = origin;
				_force.points3[1] = final;

				origin = end.position;

				Color c;
				if (isTarget)
					c = ColorPresets.Selected;
				else if (isDirty)
					c = ColorPresets.Changed;
				else
					c = _greenColor;

				_both.points3[0] = origin;
				_both.points3[1] = final;

				_circle.MakeCircle(end.position, Camera.main.transform.forward, _dynamicBonesDragRadius);

				float distance = 1 - (Mathf.Clamp((end.position - Camera.main.transform.position).sqrMagnitude, 5, 20) - 5) / 15;
				if (distance < 0.2f)
					distance = 0.2f;
				c.a = distance;

				_both.color = c;
				_circle.color = c;
				Color gravityColor = _gravity.color;
				gravityColor.a = distance;
				_gravity.color = gravityColor;
				Color forceColor = _force.color;
				forceColor.a = distance;
				_force.color = forceColor;

				_gravity.Draw();
				_force.Draw();
				_both.Draw();
				_circle.Draw();
			}

			public void Destroy()
			{
				VectorLine.Destroy(ref _gravity);
				VectorLine.Destroy(ref _force);
				VectorLine.Destroy(ref _both);
				VectorLine.Destroy(ref _circle);
			}

			public void SetActive(bool active)
			{
				_gravity.active = active;
				_force.active = active;
				_both.active = active;
				_circle.active = active;
			}

			public bool IsActive()
			{
				return _gravity.active;
			}
		}
	}
}
