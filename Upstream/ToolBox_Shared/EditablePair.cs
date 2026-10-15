using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace ToolBox
{
	public struct EditablePair<T>
	{
		private T _originalValue;
		private bool _hasValue;

		public T currentValue;

		public T originalValue
		{
			get => _originalValue;
			set
			{
				_originalValue = value;
				_hasValue = true;
			}
		}

		public bool hasValue => _hasValue;

		public EditablePair(T currentValue, T originalValue)
		{
			this.currentValue = currentValue;
			_originalValue = originalValue;
			_hasValue = true;
		}

		public void Reset()
		{
			_hasValue = false;
		}

		//public static implicit operator T(EditablePair<T> v)
		//{
		//    return v.currentValue;
		//}
	}

	public struct EditablePair<T, TOriginal>
	{
		private TOriginal _originalValue;
		private bool _hasValue;

		public T currentValue;

		public TOriginal originalValue
		{
			get => _originalValue;
			set
			{
				_originalValue = value;
				_hasValue = true;
			}
		}

		public bool hasValue => _hasValue;

		public EditablePair(T currentValue, TOriginal originalValue)
		{
			this.currentValue = currentValue;
			_originalValue = originalValue;
			_hasValue = true;
		}

		public void Reset()
		{
			_hasValue = false;
		}

		//public static implicit operator T(EditablePair<T, TOriginal> v)
		//{
		//    return v.currentValue;
		//}
	}
}
