using System;
using System.Collections;
using System.Collections.Generic;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Linq;

namespace ViewModelKit
{
	/// <summary>
	/// Provides a base class for automatically implemented view model classes with data validation
	/// that implements the <see cref="INotifyPropertyChanged"/> interface.
	/// </summary>
	public abstract class ValidatingViewModelBase : ViewModelBase, INotifyDataErrorInfo
	{
		/// <summary>
		/// Contains error messages for each property. Object-level errors are stored with an empty
		/// string as key. This instance is only set when there are errors available, to save memory
		/// when no errors exist or validation is not used.
		/// </summary>
		private Dictionary<string, ICollection<string>> validationErrors;

		/// <summary>
		/// Gets a value that indicates whether the entity has validation errors.
		/// </summary>
		public virtual bool HasErrors => validationErrors != null && validationErrors.Count > 0;

		/// <summary>
		/// Gets the instance that provides the property validation attributes. Defaults to the
		/// current instance. Deriving classes may return another instance.
		/// </summary>
		protected virtual object PropertyValidationSource => this;

		/// <summary>
		/// Occurs when the validation errors have changed for a property or for the entire entity.
		/// </summary>
		public event EventHandler<DataErrorsChangedEventArgs> ErrorsChanged;

		/// <summary>
		/// Gets the validation errors for a specified property or for the entire entity.
		/// </summary>
		/// <param name="propertyName">The name of the property to retrieve validation errors for;
		///   or null or empty, to retrieve entity-level errors.</param>
		/// <returns>The validation errors for the property or entity.</returns>
		public IEnumerable GetErrors(string propertyName)
		{
			if (propertyName == null)
				propertyName = "";   // Object-level errors (dictionary key cannot be null)

			if (validationErrors == null)
			{
				return new string[0];
			}
			ICollection<string> errors = null;
			validationErrors.TryGetValue(propertyName, out errors);
			return errors;
		}

		/// <summary>
		/// Gets the validation errors for each property.
		/// </summary>
		public virtual IDictionary<string, ICollection<string>> PropertyErrors
		{
			get
			{
				if (validationErrors == null)
				{
					validationErrors = new Dictionary<string, ICollection<string>>();
				}
				return validationErrors;
			}
		}

		/// <summary>
		/// Gets all validation errors in a single collection.
		/// </summary>
		public virtual ICollection<string> AllErrors
		{
			get
			{
				if (validationErrors == null)
				{
					return new List<string>();
				}
				return validationErrors.SelectMany(kvp => kvp.Value).ToList();
			}
		}

		/// <summary>
		/// Validates a value for the specified property.
		/// </summary>
		/// <param name="value">The value to validate.</param>
		/// <param name="propertyName">The name of the property.</param>
		protected void ValidateProperty(object value, string propertyName)
		{
			if (propertyName == null)
				throw new ArgumentNullException(nameof(propertyName));

			bool needNotify = validationErrors != null && validationErrors.Remove(propertyName);

			var errorMessages = ValidatePropertyOverride(value, propertyName);
			if (errorMessages != null && errorMessages.Any())
			{
				if (validationErrors == null)
				{
					validationErrors = new Dictionary<string, ICollection<string>>();
				}
				validationErrors.Add(propertyName, errorMessages.ToList());
				needNotify = true;
			}
			if (!HasErrors)
			{
				validationErrors = null;
			}

			// Raise the ErrorsChanged event if an error for the property was removed and/or added
			if (needNotify)
			{
				OnErrorsChanged(propertyName);
			}
		}

		/// <summary>
		/// When overridden in a derived class, performs the validation of a property and returns
		/// error messages.
		/// </summary>
		/// <param name="value">The value to validate.</param>
		/// <param name="propertyName">The name of the property.</param>
		/// <returns>A collection of error messages; or null or empty if there are no errors.</returns>
		protected virtual IEnumerable<string> ValidatePropertyOverride(object value, string propertyName)
		{
			var results = new List<ValidationResult>();
			var context = new ValidationContext(PropertyValidationSource)
			{
				MemberName = propertyName
			};
			if (!Validator.TryValidateProperty(value, context, results))
			{
				return results.Select(r => r.ErrorMessage);
			}
			return null;
		}

		/// <summary>
		/// Validates the current value of all properties.
		/// </summary>
		public virtual void ValidateAllProperties()
		{
			// Iterate all properties from the class that defines the validation attributes
			foreach (PropertyDescriptor property in TypeDescriptor.GetProperties(PropertyValidationSource))
			{
				// Skip validation interface properties
				if (property.Name == nameof(HasErrors)) continue;

				// Find the property in the current instance to get its value.
				// Skip properties that don't exist here.
				var myProperty = TypeDescriptor.GetProperties(this)[property.Name];
				if (myProperty != null)
				{
					object value = myProperty.GetValue(this);
					ValidateProperty(value, property.Name);
				}
			}
		}

		/// <summary>
		/// Raises the <see cref="ErrorsChanged"/> event for a single property or for the entire
		/// entity.
		/// </summary>
		/// <param name="propertyName">The name of the property that was validated; or null, if the
		///   entire entity was validated.</param>
		protected virtual void OnErrorsChanged(string propertyName)
		{
			ErrorsChanged?.Invoke(this, new DataErrorsChangedEventArgs(propertyName));
			OnPropertyChanged(nameof(HasErrors));
			OnPropertyChanged(nameof(PropertyErrors));
			OnPropertyChanged(nameof(AllErrors));
		}
	}
}
