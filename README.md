## This is an add-in for [Fody](https://github.com/Fody/Fody/)

Makes WPF ViewModel classes smart by default. Implements [INotifyPropertyChanged](http://msdn.microsoft.com/en-us/library/system.componentmodel.inotifypropertychanged.aspx) and DelegateCommands for auto properties at compile time, recognises dependent properties, connects property changed handlers. Supports virtual properties with Entity Famework.

[Introduction to Fody](https://github.com/Fody/Fody/wiki/SampleUsage) 

Supported target frameworks: .NET 4.0 (Client profile) or newer

This library is intended to be used with WPF applications.

[Project website](http://unclassified.software/source/viewmodelkit)

## The NuGet package [![NuGet Status](http://img.shields.io/nuget/v/ViewModelKit.Fody.svg?style=flat-square)](https://www.nuget.org/packages/ViewModelKit.Fody/)

https://nuget.org/packages/ViewModelKit.Fody/

    PM> Install-Package ViewModelKit.Fody

## Features

For properties:

* Raises the **PropertyChanged** event when the value of an auto-implemented property changes.
* Also raises the PropertyChanged event for all **dependent properties** that access another notifying property or its backing field in the getter.
* Calls **On*PropertyName*Changed** methods when the property has changed.
* Calls **On*PropertyName*Changing** methods before the property has changed, providing the old and new value, with the option to reject or alter the new value.
* Sets the **IsModified** property to true when another property changes, except when **IsLoaded** is false.
* Raises the PropertyChanged event and calls other handler methods asynchronously (with SynchronizationContext.Current.Post()) if the property is **virtual** and the current instance is of a derived type. This allows Entity Framework to update foreign key/navigation properties in the dynamic proxy before the change events are raised.

For commands:

* Connects **DelegateCommand** properties with similar-named **On*CommandName*** and **Can*CommandName*** methods.
* Raises the **CanExecuteChanged** event of all DelegateCommands that depend on a property, i. e. read the property or its backing field in their Can*CommandName* method.

Future ideas are about adding data validation support once I figured out the way I want to use it.

## Example

The following example demonstrates how your code is changed and extended at compile time.

#### Your code

You declare all interfaces so you can use them in your own code. ViewModelKit will not add any interfaces to your classes, it will just provide additional implementation that affects behaviour but not interfaces. See below for additional notes.

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

#### What gets compiled

The following representation is an approximation of the compiled code. You may look at its exact structure with a .NET decompiler like [ILSpy](http://ilspy.net). In general, all generated method names will have names that cannot conflict with regular C# or VB code, so don’t worry about that.

    public class Person : INotifyPropertyChanged
    {
        public Person()
        {
            TestCommand = new DelegateCommand(OnSave, CanSave);
        }

        public event PropertyChangedEventHandler PropertyChanged;

        private string firstName;
        public string FirstName
        {
            get { return firstName; }
            set
            {
                if (value != firstName)
                {
                    firstName = value;
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
                    OnLastNameChanged();
                    PropertyChanged?.Invoke(this, new PropertyChangedEventArgs("LastName"));
                    PropertyChanged?.Invoke(this, new PropertyChangedEventArgs("FullName"));
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

## Attributes

These attributes are defined in the ViewModelKit assembly which is automatically referenced by the NuGet package. This reference and all attributes from it will be removed during compilation, so you do not distribute this assembly.

#### ViewModelAttribute

Instructs the ViewModelKit processor to implement additional features in the class. No other classes will be enhanced.

See above for an example.

#### DoNotNotifyAttribute

Denotes that changes to the property value should not raise the PropertyChanged event for this property or any dependent properties. No other features like OnChanged or OnChanging handlers will be added to properties with this attribute.

###### Example

    [DoNotNotify]
    public string InternalCode { get; set; }

#### DependsOnAttribute

Denotes that the property depends on other properties and the PropertyChanged event should be raised for this property whenever it is raised for the other properties. You can specify multiple property names for the attribute, or add the attribute multiple times.

###### Example

    public string LastName { get; set; }

    [DependsOn(nameof(LastName))]
    public string FullName => GetFullName();

    // Property access in separate method is not automatically discovered
    private string GetFullName() => LastName;

You can also apply this attribute to DelegateCommand properties to have their CanExecuteChanged event raised.

###### Example

    [DependsOn(nameof(LastName))]
    public DelegateCommand SaveCommand { get; private set; }

    private bool CanSave() => Validate();

    // Property access in separate method is not automatically discovered
    private bool Validate() { /* ... */ }

#### NotModifyingAttribute

Denotes that changes to the property value should not set the IsModified value (see below).

###### Example

    public bool IsModified { get; set; }

    // Value shall not be saved, so no need to consider the object “unsaved”
    [NotModifying]
    public int Comment { get; set; }

#### IgnoreUnsupportedSignatureAttribute

Denotes that the method should be ignored if its signature is unsupported for its special name.

###### Examples

    public int Age { get; set; }

    // Would be used for Age property if it had no return value or parameter
    [IgnoreUnsupportedSignature]
    private object OnAgeChanged(bool flag) { /* ... */ }

    public DelegateCommand SaveCommand { get; private set; }

    // Would be used for SaveCommand if it had a bool return value
    [IgnoreUnsupportedSignature]
    private void CanSave() { /* ... */ }

## Provided classes

These classes are defined in the ViewModelKit assembly which is automatically referenced by the NuGet package. This reference will be removed and all referenced classes will be copied into your assembly during compilation, so you do not distribute this assembly.

#### ViewModelBase

Provides a base class for automatically implemented view model classes that implements the INotifyPropertyChanged interface. If your classes derive from this class, you don’t have to use the ViewModel attribute, and your class inherits an INotifyPropertyChanged implementation with an **OnPropertyChanged** method that you may call from your code to raise the PropertyChanged event manually.

###### Example

    public class Person : ViewModelBase
    {
        public string FirstName { get; set; }
        public string LastName { get; set; }
        public int Age { get; set; }
    }

#### DelegateCommand

Provides an [ICommand](https://msdn.microsoft.com/en-us/library/system.windows.input.icommand.aspx) implementation which relays the Execute and CanExecute method to the specified delegates.

[Introduction to the DelegateCommand class](http://unclassified.software/source/delegatecommand)

## Supported methods

You can define additional methods in your class that will be picked up by ViewModelKit and called at appropriate times. All these methods are found by naming convention, there are no special attributes to recognise them. If a method with one of these names is found but does not have the required signature, and no other acceptable method exists with that name, a warning will be generated at compile time. You can suppress this warning by adding the IgnoreUnsupportedSignature attribute to the method (see above).

#### On*PropertyName*Changed

This method may be defined for every property in your class. It will be called after the property value changes and before the PropertyChanged event is raised.

###### Example

    private void OnFirstNameChanged()
    {
        // ...
    }

#### On*PropertyName*Changing

This method may be defined for every property in your class. It will be called before the property value changes and has the chance to reject or alter the new value. If multiple methods with an acceptable signature exist, any one will be chosen and the others are ignored.

###### Examples

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

#### On*CommandName*

This method may be defined for every command property (of the provided type DelegateCommand) in your class. It will be called when the command should be executed.

If this method is not defined for a command, the command will be disabled (DelegateCommand.Disabled).

###### Examples

    public DelegateCommand SaveCommand { get; private set; }

    private void OnSave()
    {
        // ...
    }

    private void OnSave(object state)
    {
        // ...
    }

#### Can*CommandName*

This method may be defined for every command property (of the provided type DelegateCommand) in your class. It will be called to determine whether the command can be executed.

Properties or their backing fields read in this method will make the command depend on those properties automatically.

If this method is not defined for a command, the command will always be executable. You can still change the **IsEnabled** property of the command instance to explicitly enable or disable the command.

###### Examples

    public DelegateCommand SaveCommand { get; private set; }

    private bool CanSave()
    {
        return true;
    }

    private bool CanSave(object state)
    {
        return true;
    }

## Supported properties

You can define additional properties in your class that will be picked up by ViewModelKit and used at appropriate times. All these properties are found by naming convention, there are no special attributes to recognise them. If a property with one of these names is found but does not have the required type, a warning will be generated at compile time. You can suppress this warning by adding the IgnoreUnsupportedSignature attribute to the property (see above).

#### IsModified

If this property exists, it will be set to true when another property changes, except for properties with the NotModifying attribute (see above). (See also IsLoaded below.) You must set this property to false again at appropriate times, e. g. after persisting the object or reverting the changes.

ViewModelKit only requires the set accessor of this property. The property may be public or private.

###### Example

    public bool IsModified { get; set; }

#### IsLoaded

If this property exists, it will be read before setting IsModified (see above). If IsLoaded is false, IsModified will not be set to true. This can be used to prevent marking the object as modified when all properties are initially set while loading. You must set this property to true when the object has finished loading and future property changes should actually mark the object modified.

ViewModelKit only requires the get accessor of this property. The property may be public or private.

###### Example

    public bool IsLoaded { get; private set; }

