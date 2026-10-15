using System;
using UnityEngine.EventSystems;

namespace UILib.EventHandlers
{
	public class PointerEnterHandler : UIBehaviour, IPointerEnterHandler, IPointerExitHandler
	{
		public Action<PointerEventData> onPointerEnter;

		public void OnPointerEnter(PointerEventData eventData)
		{
			onPointerEnter?.Invoke(eventData);
		}

		public Action<PointerEventData> onPointerExit;

		public void OnPointerExit(PointerEventData eventData)
		{
			onPointerExit?.Invoke(eventData);
		}
	}
}
