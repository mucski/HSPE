using System;
using UnityEngine.EventSystems;

namespace UILib.EventHandlers
{
	public class DragHandler : UIBehaviour, IBeginDragHandler, IInitializePotentialDragHandler, IDragHandler, IEndDragHandler
	{
		public Action<PointerEventData> onBeginDrag;
		public void OnBeginDrag(PointerEventData eventData)
		{
			onBeginDrag?.Invoke(eventData);
		}

		public Action<PointerEventData> onInitializePotentialDrag;
		public void OnInitializePotentialDrag(PointerEventData eventData)
		{
			onInitializePotentialDrag?.Invoke(eventData);
		}

		public Action<PointerEventData> onDrag;
		public void OnDrag(PointerEventData eventData)
		{
			onDrag?.Invoke(eventData);
		}

		public Action<PointerEventData> onEndDrag;
		public void OnEndDrag(PointerEventData eventData)
		{
			onEndDrag?.Invoke(eventData);
		}
	}
}
