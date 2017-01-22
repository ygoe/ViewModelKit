# This is an add-in for [Fody](https://github.com/Fody/Fody/)

Makes WPF ViewModel classes smart by default. Implements [INotifyPropertyChanged](https://msdn.microsoft.com/en-us/library/system.componentmodel.inotifypropertychanged.aspx) and DelegateCommands for auto-properties at compile time, recognises dependent properties, connects property changed handlers, triggers validation. Supports virtual properties with Entity Famework.

[Introduction to Fody](https://github.com/Fody/Fody/wiki/SampleUsage) 

Supported target frameworks: .NET 4.5 or newer

This library is intended to be used with WPF applications.

[Project website](http://unclassified.software/source/viewmodelkit)

# The NuGet package [![NuGet Status](http://img.shields.io/nuget/v/ViewModelKit.Fody.svg?style=flat-square)](https://www.nuget.org/packages/ViewModelKit.Fody/)

https://nuget.org/packages/ViewModelKit.Fody/

    PM> Install-Package ViewModelKit.Fody

# Features

For properties:

* Raises the <strong>PropertyChanged</strong> event when the value of an auto-implemented property changes.
* Also raises the PropertyChanged event for all <strong>dependent properties</strong> that access another notifying property or its backing field in the getter.
* Calls <strong>On<i>PropertyName</i>Changed</strong> methods when the property has changed.
* Calls <strong>On<i>PropertyName</i>Changing</strong> methods before the property has changed, providing the old and new value, with the option to reject or alter the new value.
* Sets the <strong>IsModified</strong> property to true when another property changes, except when <strong>IsLoaded</strong> is false.
* Raises the PropertyChanged event and calls other handler methods asynchronously (with `SynchronizationContext.Current.Post()`) if the property is <strong>virtual</strong> and the current instance is of a derived type. This allows Entity Framework to update foreign key/navigation properties in the dynamic proxy before the change events are raised.

For commands:

* Connects <strong>DelegateCommand</strong> properties with similar-named <strong>On<i>CommandName</i></strong> and <strong>Can<i>CommandName</i></strong> methods.
* Raises the <strong>CanExecuteChanged</strong> event of all DelegateCommands that depend on a property, i. e. read the property or its backing field in their Can<i>CommandName</i> method.

For validation:

* Provides a base class implementing <strong>INotifyDataErrorInfo</strong> with <strong>DataAnnotations</strong> support
* Raises the <strong>ErrorsChanged</strong> event when a property value has changed and validation gave a different result
* Validation attributes may be defined in the ViewModel or another (Model) class with the same property names

# Example

The following example demonstrates how your code is changed and extended at compile time.

### Your code

You declare all interfaces so you can use them in your own code. ViewModelKit will not add any interfaces to your classes, it will just provide additional implementation that affects behaviour but not interfaces. See below for additional notes.

```cs
[ViewModel]
public class Person : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler PropertyChanged;

    public string FirstName { get; set; }
    public string LastName { get; set; }
    public int Age { get; set; }

    public string FullName => $"{FirstName} {LastName} ({Age})";

    public void OnLastNameChanged()
    {
        // ...
    }

    public bool OnAgeChanging(int oldValue, int newValue)
    {
        // Age can only increase by 1 at a time, reject other changes
        return newValue - oldValue <= 1;
    }

    public DelegateCommand SaveCommand { get; }   // set is optional
    private bool CanSave() => !string.IsNullOrEmpty(LastName);
    private void OnSave() { /* ... */ }
}
```

### What gets compiled

The following representation is an approximation of the compiled code. The generated parts are described with a comment. You may look at its exact structure with a .NET decompiler like [ILSpy](http://ilspy.net). In general, all generated method names will have names that cannot conflict with regular C# or VB code, so don’t worry about that.

```cs
public class Person : INotifyPropertyChanged
{
    public Person()
    {
        // GENERATED: Initialising commands, connecting methods by naming convention
        TestCommand = new DelegateCommand(OnSave, CanSave);
    }

    public event PropertyChangedEventHandler PropertyChanged;

    // The backing field has already been there, it just wasn’t visible in C# code
    private string firstName;
    public string FirstName
    {
        get { return firstName; }
        set
        {
            // GENERATED: Equality check
            if (value != firstName)
            {
                firstName = value;
                // GENERATED: Raising the PropertyChanged events
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs("FirstName"));
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs("FullName"));
            }
        }
    }

    private string lastName;
    public string LastName
    {
        get { return lastName; }
        set
        {
            if (value != lastName)
            {
                lastName = value;
                // GENERATED: Calling other supported methods (described below)
                OnLastNameChanged();
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs("LastName"));
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs("FullName"));
                // GENERATED: Raising the commands’ CanExecuteChanged events
                TestCommand.RaiseCanExecuteChanged();
            }
        }
    }

    private int age;
    public int Age
    {
        get { return age; }
        set
        {
            if (value != age)
            {
                // GENERATED: Calling other supported methods (described below)
                if (!OnAgeChanging(age, value))
                    return;
                age = value;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs("Age"));
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs("FullName"));
            }
        }
    }

    public string FullName => $"{FirstName} {LastName} ({Age})";

    public void OnLastNameChanged()
    {
        // ...
    }

    public bool OnAgeChanging(int oldValue, int newValue)
    {
        // Age can only increase by 1 at a time, reject other changes
        return newValue - oldValue <= 1;
    }

    public DelegateCommand SaveCommand { get; }
    private bool CanSave() => !string.IsNullOrEmpty(LastName);
    private void OnSave() { /* ... */ }
}
```

# Attributes

These attributes are defined in the ViewModelKit assembly which is automatically referenced by the NuGet package. This reference and all attributes from it will be removed during compilation, so you do not distribute this assembly.

### ViewModelAttribute

Instructs the ViewModelKit processor to implement additional features in the class. No other classes will be enhanced.

See above for an example.

### DoNotNotifyAttribute

Denotes that changes to the property value should not raise the PropertyChanged event for this property or any dependent properties. No other features like OnChanged or OnChanging handlers will be added to properties with this attribute.

##### Example

```cs
[DoNotNotify]
public string InternalCode { get; set; }
```

### DependsOnAttribute

Denotes that the property depends on other properties and the PropertyChanged event should be raised for this property whenever it is raised for the other properties. You can specify multiple property names for the attribute, or add the attribute multiple times.

This is not supported for auto-properties because they have their own value and cannot depend on any other property.

##### Example

```cs
public string LastName { get; set; }

[DependsOn(nameof(LastName))]
public string FullName => GetFullName();

// Property access in separate method is not automatically discovered
private string GetFullName() => LastName;
```

You can also apply this attribute to DelegateCommand properties to have their CanExecuteChanged event raised.

##### Example

```cs
[DependsOn(nameof(LastName))]
public DelegateCommand SaveCommand { get; private set; }

private bool CanSave() => Validate();

// Property access in separate method is not automatically discovered
private bool Validate() { /* ... */ }
```

### NotModifyingAttribute

Denotes that changes to the property value should not set the IsModified value (see below).

##### Example

```cs
public bool IsModified { get; set; }

// Value shall not be saved, so no need to consider the object “unsaved”
[NotModifying]
public int Comment { get; set; }
```

### IgnoreUnsupportedSignatureAttribute

Denotes that the method should be ignored if its signature is unsupported for its special name.

##### Examples

```cs
public int Age { get; set; }

// Would be used for Age property if it had no return value or parameter
[IgnoreUnsupportedSignature]
private object OnAgeChanged(bool flag) { /* ... */ }

public DelegateCommand SaveCommand { get; private set; }

// Would be used for SaveCommand if it had a bool return value
[IgnoreUnsupportedSignature]
private void CanSave() { /* ... */ }
```

### DoNotValidateAttribute

Denotes that changes to the property value should not raise the ErrorsChanged event for this property or perform validation.

# Provided classes

These classes are defined in the ViewModelKit assembly which is automatically referenced by the NuGet package. This reference will be removed and all referenced classes will be copied into your assembly during compilation, so you do not distribute this assembly.

### ViewModelBase

Provides a base class for automatically implemented view model classes that implements the INotifyPropertyChanged interface. If your classes derive from this class, you don’t have to use the ViewModel attribute, and your class inherits an INotifyPropertyChanged implementation with an **OnPropertyChanged** method that you may call from your code to raise the PropertyChanged event manually.

##### Example

```cs
using ViewModelKit;

public class Person : ViewModelBase
{
    public string FirstName { get; set; }
    public string LastName { get; set; }
    public int Age { get; set; }
}
```

### DelegateCommand

Provides an [ICommand](https://msdn.microsoft.com/en-us/library/system.windows.input.icommand.aspx) implementation which relays the Execute and CanExecute method to the specified delegates.

[Introduction to the DelegateCommand class](http://unclassified.software/source/delegatecommand)

### ValidatingViewModelBase

Provides a base class for automatically implemented view model classes with data validation that implements the INotifyPropertyChanged and INotifyDataErrorInfo interfaces. This is derived from the ViewModelBase class. If your classes derive from this class, validation is performed whenever a property value changes. The base class has additional methods that you may call from your code to raise the ErrorsChanged event for single properties or the entire object manually.

##### Example

```cs
using ViewModelKit;

public class Person : ValidatingViewModelBase
{
    [Required]
    public string FirstName { get; set; }

    [Required]
    [MinLength(4)]
    public string LastName { get; set; }

    [Range(10, 120)]
    public int Age { get; set; }
}
```

In case the validation attributes are not defined in the ViewModel class but e. g. in a Model class, you need to provide this instance through overriding the **PropertyValidationSource** property. You can also override the **ValidatePropertyOverride** method to provide a custom validation implementation.

##### Example

```cs
using ViewModelKit;

public class Person
{
    [Required]
    public string FirstName { get; set; }

    [Required]
    [MinLength(4)]
    public string LastName { get; set; }

    [Range(10, 120)]
    public int Age { get; set; }
}

public class PersonViewModel : ValidatingViewModelBase
{
    private Person person;

    protected override object PropertyValidationInstance => person;

    public string FirstName { get; set; }
    public string LastName { get; set; }
    public string Age { get; set; }

    // Load and save methods to connect with the model instance...
}
```

# Supported methods

You can define additional methods in your class that will be picked up by ViewModelKit and called at appropriate times. All these methods are found by naming convention, there are no special attributes to recognise them. If a method with one of these names is found but does not have the required signature, and no other acceptable method exists with that name, a warning will be generated at compile time. You can suppress this warning by adding the IgnoreUnsupportedSignature attribute to the method (see above).

### On*PropertyName*Changed

This method may be defined for every property in your class. It will be called after the property value changes and before the PropertyChanged event is raised.

This also works for (transitively) dependent get-only properties, but their Changed method will always be called when the original source property has changed. That is, the Changed method of a get-only property may be called when its value has not actually changed (but possibly might have).

##### Example

```cs
private void OnFirstNameChanged()
{
    // ...
}
```

### On*PropertyName*Changing

This method may be defined for every property in your class. It will be called before the property value changes and has the chance to reject or alter the new value. If multiple methods with an acceptable signature exist, any one will be chosen and the others are ignored.

This does not work for dependent get-only properties because their value is computed and changing it cannot be prevented.

##### Examples

```cs
// Gets old and new value (simple form)
private void OnFirstNameChanging(string oldValue, string newValue)
{
    // ...
}

// Can alter the new value (ref parameter)
private void OnFirstNameChanging(string oldValue, ref string newValue)
{
    newValue = "...";
}

// Can reject the new value (bool return type)
private bool OnFirstNameChanging(string oldValue, string newValue)
{
    // return false to reject the new value and not raise any events
    return true;
}

// Can alter and reject the new value
private bool OnFirstNameChanging(string oldValue, ref string newValue)
...
```

### On*CommandName*

This method may be defined for every command property (of the provided type DelegateCommand) in your class. It will be called when the command should be executed.

If this method is not defined for a command, the command will be disabled (DelegateCommand.Disabled).

##### Examples

```cs
public DelegateCommand SaveCommand { get; private set; }

private void OnSave()
{
    // ...
}

private void OnSave(object state)
{
    // ...
}
```

### Can*CommandName*

This method may be defined for every command property (of the provided type DelegateCommand) in your class. It will be called to determine whether the command can be executed.

Properties or their backing fields read in this method will make the command depend on those properties automatically.

If this method is not defined for a command, the command will always be executable. You can still change the **IsEnabled** property of the command instance to explicitly enable or disable the command.

##### Examples

```cs
public DelegateCommand SaveCommand { get; private set; }

private bool CanSave()
{
    return true;
}

private bool CanSave(object state)
{
    return true;
}
```

# Supported properties

You can define additional properties in your class that will be picked up by ViewModelKit and used at appropriate times. All these properties are found by naming convention, there are no special attributes to recognise them. If a property with one of these names is found but does not have the required type, a warning will be generated at compile time. You can suppress this warning by adding the IgnoreUnsupportedSignature attribute to the property (see above).

### IsModified

If this property exists, it will be set to true when another property changes, except for properties with the NotModifying attribute (see above). (See also IsLoaded below.) You must set this property to false again at appropriate times, e. g. after persisting the object or reverting the changes.

ViewModelKit only requires the set accessor of this property. The property may be public or private and can be inherited from a base class. This does not work for dependent get-only properties.

##### Example

```cs
public bool IsModified { get; set; }
```

### IsLoaded

If this property exists, it will be read before setting IsModified (see above). If IsLoaded is false, IsModified will not be set to true. This can be used to prevent marking the object as modified when all properties are initially set while loading. You must set this property to true when the object has finished loading and future property changes should actually mark the object modified.

ViewModelKit only requires the get accessor of this property. The property may be public or private and can be inherited from a base class.

##### Example

```cs
public bool IsLoaded { get; private set; }
```
