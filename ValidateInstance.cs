/// <summary>
/// Validates the current value of all properties.
/// </summary>
protected void ValidateInstance()
{
	if (PropertyValidationSource != this)
		throw new NotSupportedException("Instance validation is only supported if PropertyValidationInstance does not return another instance.");

	HashSet<string> affectedPropertyNames = new HashSet<string>();
	if (validationErrors != null)
	{
		foreach (string propertyName in validationErrors.Keys)
		{
			affectedPropertyNames.Add(propertyName);
		}
		validationErrors.Clear();
	}

	var results = new List<ValidationResult>();
	var context = new ValidationContext(this);
	if (!Validator.TryValidateObject(this, context, results, true))
	{
		if (validationErrors == null)
		{
			validationErrors = new Dictionary<string, ICollection<string>>();
		}
		foreach (var group in results.GroupBy(r => r.MemberNames.FirstOrDefault() ?? ""))
		{
			validationErrors.Add(group.Key, group.Select(r => r.ErrorMessage).ToList());
			affectedPropertyNames.Add(group.Key);
		}
	}
	if (!HasErrors)
	{
		validationErrors = null;
	}

	// Raise the ErrorsChanged event for all affected properties
	foreach (var propertyName in affectedPropertyNames)
	{
		OnErrorsChanged(propertyName);
	}
}
