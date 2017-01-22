using System;

namespace ViewModelKit
{
	/// <summary>
	/// Instructs the ViewModelKit processor to implement additional features in the class.
	/// </summary>
	[AttributeUsage(AttributeTargets.Class)]
	public class ViewModelAttribute : Attribute
	{
	}

	/// <summary>
	/// Denotes that changes to the property value should not raise the PropertyChanged event for
	/// this property or any dependent properties.
	/// </summary>
	[AttributeUsage(AttributeTargets.Property)]
	public class DoNotNotifyAttribute : Attribute
	{
	}

	/// <summary>
	/// Denotes that the property depends on other properties and the PropertyChanged event should
	/// be raised for this property whenever it is raised for the other properties.
	/// </summary>
	[AttributeUsage(AttributeTargets.Property, AllowMultiple = true)]
	public class DependsOnAttribute : Attribute
	{
		/// <summary>
		/// Denotes that the property depends on other properties and the PropertyChanged event
		/// should be raised for this property whenever it is raised for the other properties.
		/// </summary>
		/// <param name="propertyNames">The other properties that this property depends on.</param>
		public DependsOnAttribute(params string[] propertyNames)
		{
		}
	}

	/// <summary>
	/// Denotes that changes to the property value should not set the IsModified value.
	/// </summary>
	[AttributeUsage(AttributeTargets.Property)]
	public class NotModifyingAttribute : Attribute
	{
	}

	/// <summary>
	/// Denotes that the method should be ignored if its signature is unsupported for its special
	/// name.
	/// </summary>
	[AttributeUsage(AttributeTargets.Method | AttributeTargets.Property)]
	public class IgnoreUnsupportedSignatureAttribute : Attribute
	{
	}

	/// <summary>
	/// Denotes that changes to the property value should not raise the ErrorsChanged event for
	/// this property or perform validation.
	/// </summary>
	[AttributeUsage(AttributeTargets.Property)]
	public class DoNotValidateAttribute : Attribute
	{
	}

	/// <summary>
	/// Denotes that the new value of the property shall be cleaned up when it's set.
	/// </summary>
	[AttributeUsage(AttributeTargets.Property)]
	public class CleanupAttribute : Attribute
	{
		/// <summary>
		/// Initialises a new instance of the <see cref="CleanupAttribute"/> class.
		/// </summary>
		/// <param name="methodName">The cleanup method name.</param>
		public CleanupAttribute(string methodName)
		{
		}
	}
}
