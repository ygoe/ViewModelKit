using System;
using System.ComponentModel;

namespace ViewModelKit
{
	/// <summary>
	/// Provides a base class for automatically implemented view model classes that implements the
	/// <see cref="INotifyPropertyChanged"/> interface.
	/// </summary>
	[ViewModel]
	public abstract class ViewModelBase : INotifyPropertyChanged
	{
		/// <summary>
		/// Occurs when a property value changes.
		/// </summary>
		public event PropertyChangedEventHandler PropertyChanged;

		/// <summary>
		/// Raises the <see cref="PropertyChanged"/> event for a single property. This method will
		/// not trigger any additional functionality like notifying dependent properties or calling
		/// property changed handler methods, so it should only be used if automatic notification
		/// cannot be used. This method is not called by generated property implementations.
		/// </summary>
		/// <param name="propertyName">The name of the property that changed.</param>
		protected void OnPropertyChanged(string propertyName)
		{
			PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
		}
	}
}
