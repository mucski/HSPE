using System;
using System.Collections;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;

namespace UILib
{
	// Already included in ToolBox, maybe this should be removed and reference be updated to the ToolBox version
	internal static class Extensions
	{
		internal static void ExecuteDelayed_(this MonoBehaviour self, Action action, int waitCount = 1)
		{
			self.StartCoroutine(ExecuteDelayed_Routine(action, waitCount));
		}

		private static IEnumerator ExecuteDelayed_Routine(Action action, int waitCount)
		{
			for (int i = 0; i < waitCount; ++i)
				yield return null;
			action();
		}
	}
}
