using System;
using UnityEngine;
using UnityEngine.EventSystems;

namespace UILib
{
	public class MovableWindow : UIBehaviour, IPointerDownHandler, IDragHandler, IPointerUpHandler
	{
		private RectTransform _rectTransform;
		private readonly Vector3[] _draggableZoneCorners = new Vector3[4];
		private readonly Vector3[] _limitCorners = new Vector3[4];
		private Vector2 _cachedDragPosition;
		private Vector2 _cachedMousePosition;
		private bool _pointerDownCalled = false;

		public RectTransform toDrag;
		public RectTransform limit;

		protected override void Awake()
		{
			base.Awake();
			_rectTransform = GetComponent<RectTransform>();
		}

		public void OnPointerDown(PointerEventData eventData)
		{
			_pointerDownCalled = true;
			_cachedDragPosition = toDrag.position;
			_cachedMousePosition = eventData.position;
		}

		public void OnDrag(PointerEventData eventData)
		{
			if (_pointerDownCalled == false)
				return;
			toDrag.position = _cachedDragPosition + (eventData.position - _cachedMousePosition);
			Limit();
		}

		public void OnPointerUp(PointerEventData eventData)
		{
			if (_pointerDownCalled == false)
				return;
			toDrag.position = _cachedDragPosition + (eventData.position - _cachedMousePosition);
			Limit();
			_pointerDownCalled = false;
		}

		private void Limit()
		{
			if (limit != null)
			{
				_rectTransform.GetWorldCorners(_draggableZoneCorners);
				limit.GetWorldCorners(_limitCorners);
				if (_draggableZoneCorners[0].x < _limitCorners[0].x)
					toDrag.position += new Vector3(_limitCorners[0].x - _draggableZoneCorners[0].x, 0f, 0f);
				if (_draggableZoneCorners[0].y < _limitCorners[0].y)
					toDrag.position += new Vector3(0f, _limitCorners[0].y - _draggableZoneCorners[0].y, 0f);
				if (_draggableZoneCorners[2].x > _limitCorners[2].x)
					toDrag.position += new Vector3(_limitCorners[2].x - _draggableZoneCorners[2].x, 0f, 0f);
				if (_draggableZoneCorners[2].y > _limitCorners[2].y)
					toDrag.position += new Vector3(0f, _limitCorners[2].y - _draggableZoneCorners[2].y, 0f);
			}
		}
	}
}
