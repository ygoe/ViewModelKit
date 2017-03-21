using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;

namespace ViewModelKit
{
	/// <summary>
	/// An <see cref="ObservableCollection{T}"/> that maintains the
	/// <see cref="INotifyDataErrorInfo.ErrorsChanged"/> event for all items that are added to or
	/// removed from the collection.
	/// </summary>
	/// <typeparam name="T">The type of elements in the collection. This must implement the
	///   <see cref="INotifyDataErrorInfo"/> interface and may also derive from the
	///   <see cref="ValidatingViewModelBase"/> class.</typeparam>
	public class ValidatingObservableCollection<T> : ObservableCollection<T>
		where T : INotifyDataErrorInfo
	{
		/// <summary>
		/// Initialises a new instance of the <see cref="ValidatingObservableCollection{T}"/> class.
		/// </summary>
		public ValidatingObservableCollection()
		{
		}

		/// <summary>
		/// Initialises a new instance of the <see cref="ValidatingObservableCollection{T}"/> class
		/// that contains elements copied from the specified collection.
		/// </summary>
		/// <param name="collection">The collection from which the elements are copied.</param>
		public ValidatingObservableCollection(IEnumerable<T> collection)
			: base(collection)
		{
			InitializeItems();
		}

		/// <summary>
		/// Initialises a new instance of the <see cref="ValidatingObservableCollection{T}"/> class
		/// that contains elements copied from the specified list.
		/// </summary>
		/// <param name="list">The list from which the elements are copied.</param>
		public ValidatingObservableCollection(List<T> list)
			: base(list)
		{
			InitializeItems();
		}

		/// <summary>
		/// Occurs when the validation errors have changed for an item in the collection.
		/// </summary>
		public event EventHandler<DataErrorsChangedEventArgs> ErrorsChanged;

		private void OnErrorsChanged(object sender, DataErrorsChangedEventArgs args)
		{
			ErrorsChanged?.Invoke(this, new DataErrorsChangedEventArgs(null));
		}

		private void InitializeItems()
		{
			foreach (T item in Items)
			{
				if (item != null)
				{
					item.ErrorsChanged += OnErrorsChanged;
				}
			}
		}

		/// <inheritdoc/>
		protected override void ClearItems()
		{
			foreach (T item in Items)
			{
				if (item != null)
				{
					item.ErrorsChanged -= OnErrorsChanged;
				}
			}
			base.ClearItems();
			OnErrorsChanged(this, null);
		}

		/// <inheritdoc/>
		protected override void InsertItem(int index, T item)
		{
			base.InsertItem(index, item);
			if (item != null)
			{
				item.ErrorsChanged += OnErrorsChanged;
			}
		}

		/// <inheritdoc/>
		protected override void RemoveItem(int index)
		{
			T item = Items[index];
			if (item != null)
			{
				item.ErrorsChanged -= OnErrorsChanged;
			}
			base.RemoveItem(index);
			OnErrorsChanged(this, null);
		}

		/// <inheritdoc/>
		protected override void SetItem(int index, T item)
		{
			T oldItem = Items[index];
			if (oldItem != null)
			{
				oldItem.ErrorsChanged -= OnErrorsChanged;
			}
			base.SetItem(index, item);
			if (item != null)
			{
				item.ErrorsChanged += OnErrorsChanged;
			}
			OnErrorsChanged(this, null);
		}

		/// <summary>
		/// Gets a value that indicates whether an item in the collection has validation errors.
		/// </summary>
		public bool HasErrors => Items.Any(i => i.HasErrors);

		/// <summary>
		/// Gets all validation errors from items in the collection in a single collection. This is
		/// only supported if <typeparamref name="T"/> is derived from
		/// <see cref="ValidatingViewModelBase"/>.
		/// </summary>
		public ICollection<string> AllErrors =>
			Items.Cast<ValidatingViewModelBase>().SelectMany(i => i.AllErrors).ToList();

		/// <summary>
		/// Validates the current value of all properties of the items in the collection. This is
		/// only supported if <typeparamref name="T"/> is derived from
		/// <see cref="ValidatingViewModelBase"/>.
		/// </summary>
		public void ValidateItems()
		{
			foreach (var item in Items.Cast<ValidatingViewModelBase>())
			{
				item.ValidateAllProperties();
			}
		}
	}
}
