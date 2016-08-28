using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using Mono.Cecil;
using Mono.Cecil.Cil;
using Mono.Cecil.Rocks;

namespace ViewModelKit.Fody
{
	public class ModuleWeaver
	{
		private const string viewModelKitNamespace = "ViewModelKit";
		private const string viewModelAttributeName = "ViewModelKit.ViewModelAttribute";
		private const string doNotNotifyAttributeName = "ViewModelKit.DoNotNotifyAttribute";
		private const string dependsOnAttributeName = "ViewModelKit.DependsOnAttribute";
		private const string notModifyingAttributeName = "ViewModelKit.NotModifyingAttribute";
		private const string ignoreUnsupportedSignatureAttributeName = "ViewModelKit.IgnoreUnsupportedSignatureAttribute";

		private const string viewModelBaseTypeName = "ViewModelKit.ViewModelBase";
		private const string delegateCommandTypeName = "ViewModelKit.DelegateCommand";

		private const string isModifiedPropertyName = "IsModified";
		private const string isLoadedPropertyName = "IsLoaded";

		private References refs;
		private bool addCompilerGeneratedAttribute = true;
		private HashSet<TypeDefinition> unsupportedIsModifiedTypeReportedForTypes = new HashSet<TypeDefinition>();
		private HashSet<TypeDefinition> unsupportedIsLoadedTypeReportedForTypes = new HashSet<TypeDefinition>();

		/// <summary>
		/// Inits logging delegates to make testing easier.
		/// </summary>
		public ModuleWeaver()
		{
			LogWarning = m => { };
			LogError = m => { };
		}

		public ModuleDefinition ModuleDefinition { get; set; }

		/// <summary>
		/// Will log an warning message to MSBuild.
		/// </summary>
		public Action<string> LogWarning { get; set; }

		/// <summary>
		/// Will log an error message to MSBuild.
		/// </summary>
		public Action<string> LogError { get; set; }

		/// <summary>
		/// Fody entry method.
		/// </summary>
		public void Execute()
		{
			refs = new References(ModuleDefinition);

			// Find and process types with the ViewModel attribute
			foreach (var typeDef in ModuleDefinition.Types.ToList())
			{
				ProcessType(typeDef);
			}
			Debug.WriteLine("All types processed");

			FixReferences();
		}

		/// <summary>
		/// Processes a ViewModel type if marked, also checks nested types (recursively).
		/// </summary>
		/// <param name="typeDef">The type to process.</param>
		private void ProcessType(TypeDefinition typeDef)
		{
			if (typeDef.HasCustomAttribute(viewModelAttributeName, true))
			{
				Debug.WriteLine($"Processing type: {typeDef}");

				PrepareViewModelType(typeDef);

				var dependencies = new CollectionDictionary<string, string>();
				var backingFields = new Dictionary<string, FieldReference>();
				FindAutoProperties(typeDef, dependencies, backingFields);
				FindDependentProperties(typeDef, dependencies, backingFields);
				FindDependentCommands(typeDef, dependencies, backingFields);

				ProcessProperties(typeDef, dependencies, backingFields);
			}

			foreach (var nestedTypeDef in typeDef.NestedTypes)
			{
				ProcessType(nestedTypeDef);
			}
		}

		/// <summary>
		/// Corrects the ViewModelBase base type reference.
		/// </summary>
		/// <param name="typeDef">The type to process.</param>
		private void PrepareViewModelType(TypeDefinition typeDef)
		{
			if (typeDef.BaseType.FullName == viewModelBaseTypeName)
			{
				foreach (var constructor in typeDef.GetConstructors())
				{
					foreach (var instr in constructor.Body.Instructions)
					{
						var methodRef = instr.Operand as MethodReference;
						if (methodRef?.DeclaringType == typeDef.BaseType)
						{
							// Replace base class constructor call with copied base class constructor
							instr.Operand = refs.ViewModelBaseType.GetConstructors()
								.Where(c => c.HasParameters == methodRef.HasParameters)
								.Where(c => !c.HasParameters ||
									c.Parameters.Select(p => p.ParameterType.FullName).Aggregate((a, b) => a + ";" + b) ==
										methodRef.Parameters.Select(p => p.ParameterType.FullName).Aggregate((a, b) => a + ";" + b))
								.First();
							break;
						}
					}
				}
				typeDef.BaseType = refs.ViewModelBaseType;
			}
		}

		/// <summary>
		/// Finds all auto-properties in the type, adds them to a list of known properties and
		/// remembers their backing fields.
		/// </summary>
		/// <param name="typeDef">The type to process.</param>
		/// <param name="dependencies">A dictionary that all found dependencies will be added to.</param>
		/// <param name="backingFields">A dictionary that all property backing fields will be added to.</param>
		private void FindAutoProperties(
			TypeDefinition typeDef,
			CollectionDictionary<string, string> dependencies,
			Dictionary<string, FieldReference> backingFields)
		{
			foreach (var propDef in typeDef.Properties)
			{
				if (propDef.HasCustomAttribute(doNotNotifyAttributeName))
					continue;

				FieldReference backingField;
				if (propDef.IsAutoProperty(out backingField))
				{
					backingFields[propDef.Name] = backingField;
					if (propDef.IsICommandProperty())
					{
						dependencies.Add("*", "C:" + propDef.Name);
					}
					else
					{
						dependencies.Add("*", "P:" + propDef.Name);
					}
				}
			}
		}

		/// <summary>
		/// Scans properties and finds get-references to other properties or their backing fields.
		/// </summary>
		/// <param name="typeDef">The type to process.</param>
		/// <param name="dependencies">A dictionary that all found dependencies will be added to.</param>
		/// <param name="backingFields">A dictionary containing all property backing fields.</param>
		private void FindDependentProperties(
			TypeDefinition typeDef,
			CollectionDictionary<string, string> dependencies,
			Dictionary<string, FieldReference> backingFields)
		{
			foreach (var propDef in typeDef.Properties)
			{
				if (propDef.HasCustomAttribute(doNotNotifyAttributeName))
					continue;
				if (propDef.PropertyType.Resolve().Is(delegateCommandTypeName))
					continue;
				Debug.WriteLine($"Testing dependent property {propDef}");

				var getBody = propDef.GetMethod.Body;
				foreach (var instr in getBody.Instructions)
				{
					if (instr.OpCode == OpCodes.Call || instr.OpCode == OpCodes.Callvirt)
					{
						var calledGetMethod = instr.Operand as MethodReference;
						// Only consider if the previous instruction was ldarg.0 (this.)
						if (calledGetMethod != null && instr.Previous?.OpCode == OpCodes.Ldarg_0)
						{
							foreach (var calledPropDef in typeDef.Properties.Where(p => p != propDef))
							{
								if (calledGetMethod == calledPropDef.GetMethod)
								{
									Debug.WriteLine($"Property {propDef.Name} depends on property {calledPropDef} by call");
									dependencies.Add(calledPropDef.Name, "P:" + propDef.Name);
									break;
								}
							}
						}
					}
					if (instr.OpCode == OpCodes.Ldfld)
					{
						var loadedBackingField = instr.Operand as FieldReference;
						// Only consider if the previous instruction was ldarg.0 (this.)
						if (loadedBackingField != null && instr.Previous?.OpCode == OpCodes.Ldarg_0)
						{
							foreach (var calledPropDef in typeDef.Properties.Where(p => p != propDef))
							{
								FieldReference propBackingField;
								if (backingFields.TryGetValue(calledPropDef.Name, out propBackingField) &&
									loadedBackingField == propBackingField)
								{
									Debug.WriteLine($"Property {propDef.Name} depends on property {calledPropDef} by ldfld");
									dependencies.Add(calledPropDef.Name, "P:" + propDef.Name);
									break;
								}
							}
						}
					}
				}

				foreach (var attr in propDef.CustomAttributes)
				{
					if (attr.AttributeType.FullName == dependsOnAttributeName)
					{
						string[] sourcePropNames = attr.ConstructorArguments.FirstOrDefault().Value as string[];
						if (sourcePropNames != null)
						{
							foreach (string sourcePropName in sourcePropNames)
							{
								if (typeDef.Properties.Any(p => p.Name == sourcePropName))
								{
									Debug.WriteLine($"Property {propDef.Name} depends on property {sourcePropName} by attribute");
									dependencies.Add(sourcePropName, "P:" + propDef.Name);
								}
								else
								{
									LogWarning($"Property {propDef.FullName} depends on non-existing property {sourcePropName}.");
								}
							}
						}

						propDef.CustomAttributes.Remove(attr);
					}
				}
			}
		}

		/// <summary>
		/// Finds command properties that depend on another property.
		/// </summary>
		/// <param name="typeDef">The type to process.</param>
		/// <param name="dependencies">A dictionary that all found dependencies will be added to.</param>
		/// <param name="backingFields">A dictionary containing all property backing fields.</param>
		private void FindDependentCommands(
			TypeDefinition typeDef,
			CollectionDictionary<string, string> dependencies,
			Dictionary<string, FieldReference> backingFields)
		{
			foreach (var propDef in typeDef.Properties)
			{
				if (propDef.PropertyType.Resolve().Is(delegateCommandTypeName))
				{
					propDef.PropertyType = refs.DelegateCommandType;
					backingFields[propDef.Name].FieldType = refs.DelegateCommandType;
					if (propDef.GetMethod != null)
						propDef.GetMethod.ReturnType = refs.DelegateCommandType;
					if (propDef.SetMethod != null)
						propDef.SetMethod.Parameters[0].ParameterType = refs.DelegateCommandType;

					string commandName = propDef.Name;
					if (commandName.EndsWith("Command"))
					{
						commandName = commandName.Substring(0, commandName.Length - "Command".Length);
					}
					var canExecuteMethod = typeDef.Methods
						.FirstOrDefault(m => m.Name == $"Can{commandName}" &&
							!m.IsStatic &&
							m.ReturnType == typeDef.Module.TypeSystem.Boolean &&
							(!m.HasParameters || m.Parameters.Count == 1 && m.Parameters[0].ParameterType == typeDef.Module.TypeSystem.Object) &&
							!m.HasGenericParameters);
					if (canExecuteMethod != null)
					{
						foreach (var instr in canExecuteMethod.Body.Instructions)
						{
							if (instr.OpCode == OpCodes.Call || instr.OpCode == OpCodes.Callvirt)
							{
								var calledGetMethod = instr.Operand as MethodReference;
								// Only consider if the previous instruction was ldarg.0 (this.)
								if (calledGetMethod != null && instr.Previous?.OpCode == OpCodes.Ldarg_0)
								{
									foreach (var calledPropDef in typeDef.Properties.Where(p => p != propDef))
									{
										if (calledGetMethod == calledPropDef.GetMethod)
										{
											Debug.WriteLine($"Command {propDef.Name} depends on property {calledPropDef} by call");
											dependencies.Add(calledPropDef.Name, "C:" + propDef.Name);
											break;
										}
									}
								}
							}
							if (instr.OpCode == OpCodes.Ldfld)
							{
								var loadedBackingField = instr.Operand as FieldReference;
								// Only consider if the previous instruction was ldarg.0 (this.)
								if (loadedBackingField != null && instr.Previous?.OpCode == OpCodes.Ldarg_0)
								{
									foreach (var calledPropDef in typeDef.Properties.Where(p => p != propDef))
									{
										FieldReference propBackingField;
										if (backingFields.TryGetValue(calledPropDef.Name, out propBackingField) &&
											loadedBackingField == propBackingField)
										{
											Debug.WriteLine($"Command {propDef.Name} depends on property {calledPropDef} by ldfld");
											dependencies.Add(calledPropDef.Name, "C:" + propDef.Name);
											break;
										}
									}
								}
							}
						}
					}

					foreach (var attr in propDef.CustomAttributes.ToList())
					{
						if (attr.AttributeType.FullName == dependsOnAttributeName)
						{
							string[] sourcePropNames = attr.ConstructorArguments.FirstOrDefault().Value as string[];
							if (sourcePropNames != null)
							{
								foreach (string sourcePropName in sourcePropNames)
								{
									if (typeDef.Properties.Any(p => p.Name == sourcePropName))
									{
										Debug.WriteLine($"Command {propDef.Name} depends on property {sourcePropName} by attribute");
										dependencies.Add(sourcePropName, "C:" + propDef.Name);
									}
									else
									{
										LogWarning($"Command {propDef.FullName} depends on non-existing property {sourcePropName}.");
									}
								}
							}

							propDef.CustomAttributes.Remove(attr);
						}
					}
				}
			}
		}

		/// <summary>
		/// Processes properties in a class.
		/// </summary>
		/// <param name="typeDef">The type to process.</param>
		/// <param name="dependencies">A dictionary containing all found dependencies.</param>
		/// <param name="backingFields">A dictionary containing all property backing fields.</param>
		private void ProcessProperties(
			TypeDefinition typeDef,
			CollectionDictionary<string, string> dependencies,
			Dictionary<string, FieldReference> backingFields)
		{
			var knownProperties = new HashSet<string>(dependencies.GetValuesOrEmpty("*").Where(n => n.StartsWith("P:")).Select(n => n.Substring(2)));
			var knownCommands = new HashSet<string>(dependencies.GetValuesOrEmpty("*").Where(n => n.StartsWith("C:")).Select(n => n.Substring(2)));

			foreach (var propDef in typeDef.Properties)
			{
				if (knownProperties.Contains(propDef.Name) ||
					knownCommands.Contains(propDef.Name))
				{
					ProcessViewModelProperty(typeDef, propDef, dependencies, backingFields);
				}
			}

			var commandProperties = typeDef.Properties.Where(p => knownCommands.Contains(p.Name)).ToList();
			if (commandProperties.Any())
			{
				ProcessCommandProperties(typeDef, commandProperties, backingFields);
			}
		}

		/// <summary>
		/// Implements INotifyPropertyChanged for a property.
		/// </summary>
		/// <param name="typeDef">The type being processed.</param>
		/// <param name="propDef">The property to process.</param>
		/// <param name="dependencies">A dictionary containing all found dependencies.</param>
		/// <param name="backingFields">A dictionary containing all property backing fields.</param>
		private void ProcessViewModelProperty(
			TypeDefinition typeDef,
			PropertyDefinition propDef,
			CollectionDictionary<string, string> dependencies,
			Dictionary<string, FieldReference> backingFields)
		{
			// No need to rewrite the non-existent setter, the property can't change anyway
			if (propDef.SetMethod == null)
				return;

			// Rewrite setter
			var backingField = backingFields[propDef.Name];
			var setBody = propDef.SetMethod.Body;
			setBody.Instructions.Clear();
			setBody.Variables.Clear();
			var il = setBody.GetILProcessor();
			var retInstr = il.Create(OpCodes.Ret);

			var changingMethod = typeDef.Methods
				.FirstOrDefault(m => m.Name == $"On{propDef.Name}Changing" &&
					!m.IsStatic &&
					(m.ReturnType == typeDef.Module.TypeSystem.Void || m.ReturnType == typeDef.Module.TypeSystem.Boolean) &&
					m.Parameters.Count == 2 &&
					m.Parameters[0].ParameterType == propDef.PropertyType &&
					(m.Parameters[1].ParameterType == propDef.PropertyType || (m.Parameters[1].ParameterType as ByReferenceType)?.ElementType == propDef.PropertyType) &&
					!m.HasGenericParameters);
			if (changingMethod == null &&
				typeDef.Methods.Any(m => m.Name == $"On{propDef.Name}Changing" &&
					!m.HasCustomAttribute(ignoreUnsupportedSignatureAttributeName)))
			{
				LogWarning($"Method signature of {typeDef.FullName}.On{propDef.Name}Changing method is unsupported, method is ignored. Use the IgnoreUnsupportedSignature attribute to avoid this warning.");
			}
			if (changingMethod != null)
			{
				// Store old property value
				setBody.Variables.Add(new VariableDefinition(propDef.PropertyType));
				setBody.InitLocals = true;
				il.Append(il.Create(OpCodes.Ldarg_0));
				il.Append(il.Create(OpCodes.Ldfld, backingField));
				il.Append(il.Create(OpCodes.Stloc_0));
			}

			// Call EqualityComparer<TProperty>.Default.Equals(newValue, GetValue<TProperty>(propertyName)); return if false
			// call class [mscorlib]System.Collections.Generic.EqualityComparer`1<!0> class [mscorlib]System.Collections.Generic.EqualityComparer`1<!!T>::get_Default()
			il.Append(il.Create(OpCodes.Call, refs.EqualityComparerDefaultReference(propDef.PropertyType)));
			il.Append(il.Create(OpCodes.Ldarg_1));
			il.Append(il.Create(OpCodes.Ldarg_0));
			il.Append(il.Create(OpCodes.Ldfld, backingField));
			// callvirt instance bool class [mscorlib]System.Collections.Generic.EqualityComparer`1<!!T>::Equals(!0, !0)
			var equals = refs.EqualityComparerEqualsReference(propDef.PropertyType);
			il.Append(il.Create(OpCodes.Callvirt, equals));
			il.Append(il.Create(OpCodes.Ldc_I4_0));
			il.Append(il.Create(OpCodes.Ceq));
			il.Append(il.Create(OpCodes.Brfalse, retInstr));

			// Call property changing handler method
			if (changingMethod != null)
			{
				il.Append(il.Create(OpCodes.Ldarg_0));   // this
				il.Append(il.Create(OpCodes.Ldloc_0));   // oldValue
				if (changingMethod.Parameters[1].ParameterType.IsByReference)
					il.Append(il.Create(OpCodes.Ldarga, 1));   // value parameter = newValue
				else
					il.Append(il.Create(OpCodes.Ldarg_1));   // value parameter = newValue
				if (changingMethod.IsVirtual)
					il.Append(il.Create(OpCodes.Callvirt, changingMethod));
				else
					il.Append(il.Create(OpCodes.Call, changingMethod));

				if (changingMethod.ReturnType == ModuleDefinition.TypeSystem.Boolean)
				{
					// Evaluate return value, cancel setter if false
					il.Append(il.Create(OpCodes.Brfalse, retInstr));
				}
			}

			// Assign value to backingField
			il.Append(il.Create(OpCodes.Ldarg_0));
			il.Append(il.Create(OpCodes.Ldarg_1));
			il.Append(il.Create(OpCodes.Stfld, backingField));

			// Set IsModified = true (if IsLoaded is not false)
			var attr = propDef.GetCustomAttribute(notModifyingAttributeName);
			if (attr != null)
			{
				propDef.CustomAttributes.Remove(attr);
			}
			else if (propDef.Name != isModifiedPropertyName && propDef.Name != isLoadedPropertyName)
			{
				var isModifiedProperty = typeDef.Properties.FirstOrDefault(p => p.Name == isModifiedPropertyName);
				var isLoadedProperty = typeDef.Properties.FirstOrDefault(p => p.Name == isLoadedPropertyName);

				if (isModifiedProperty != null &&
					(isModifiedProperty.PropertyType != ModuleDefinition.TypeSystem.Boolean ||
					isModifiedProperty.SetMethod == null ||
					isModifiedProperty.SetMethod.IsStatic))
				{
					if (!isModifiedProperty.HasCustomAttribute(ignoreUnsupportedSignatureAttributeName) &&
						unsupportedIsModifiedTypeReportedForTypes.Add(typeDef))
					{
						LogWarning($"Property type of {typeDef.FullName}.{isModifiedPropertyName} property is unsupported, property is ignored. Use the IgnoreUnsupportedSignature attribute to avoid this warning.");
					}
					isModifiedProperty = null;
				}
				if (isLoadedProperty != null &&
					(isLoadedProperty.PropertyType != ModuleDefinition.TypeSystem.Boolean ||
					isLoadedProperty.GetMethod == null ||
					isLoadedProperty.GetMethod.IsStatic))
				{
					if (!isLoadedProperty.HasCustomAttribute(ignoreUnsupportedSignatureAttributeName) &&
						unsupportedIsLoadedTypeReportedForTypes.Add(typeDef))
					{
						LogWarning($"Property type of {typeDef.FullName}.{isLoadedPropertyName} property is unsupported, property is ignored. Use the IgnoreUnsupportedSignature attribute to avoid this warning.");
					}
					isLoadedProperty = null;
				}

				if (isModifiedProperty?.SetMethod != null)
				{
					Instruction continueInstr = null;
					if (isLoadedProperty?.GetMethod != null)
					{
						// if !IsLoaded, skip following code
						il.Append(il.Create(OpCodes.Ldarg_0));
						il.Append(il.Create(OpCodes.Call, isLoadedProperty.GetMethod));
						continueInstr = il.Create(OpCodes.Nop);
						il.Append(il.Create(OpCodes.Brfalse, continueInstr));
					}
					// IsModified = true
					il.Append(il.Create(OpCodes.Ldarg_0));
					il.Append(il.Create(OpCodes.Ldc_I4_1));
					il.Append(il.Create(OpCodes.Call, isModifiedProperty.SetMethod));
					if (continueInstr != null)
					{
						il.Append(continueInstr);
					}
				}
			}

			// If the property is virtual and this.GetType is not typeDef, then raise the events asynchronously
			// (Supports Entity Framework automatic foreign key/navigation property updating)
			var propertyChangedMethod = CreatePropertyChangedMethod(typeDef, propDef, dependencies, backingFields);
			if (propDef.SetMethod.IsVirtual)
			{
				var asyncCallInstr = il.Create(OpCodes.Nop);
				var syncCallInstr = il.Create(OpCodes.Nop);

				// SynchronizationContext.Current
				var getCurrentMethod = typeof(System.Threading.SynchronizationContext)
					.GetMethod("get_Current", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static);
				il.Append(il.Create(OpCodes.Call, ModuleDefinition.ImportReference(getCurrentMethod)));

				// if null, goto sync call
				il.Append(il.Create(OpCodes.Dup));
				il.Append(il.Create(OpCodes.Brfalse, syncCallInstr));

				// GetType() == typeof(propDef)
				il.Append(il.Create(OpCodes.Ldarg_0));
				var getTypeMethod = typeof(object)
					.GetMethod("GetType", new Type[0]);
				il.Append(il.Create(OpCodes.Call, ModuleDefinition.ImportReference(getTypeMethod)));
				il.Append(il.Create(OpCodes.Ldtoken, typeDef));
				var getTypeFromHandleMethod = typeof(Type)
					.GetMethod("GetTypeFromHandle", new Type[] { typeof(RuntimeTypeHandle) });
				il.Append(il.Create(OpCodes.Call, ModuleDefinition.ImportReference(getTypeFromHandleMethod)));

				// if equal, goto sync call
				var typeEqualityMethod = typeof(Type)
					.GetMethod("op_Equality", new Type[] { typeof(Type), typeof(Type) });
				il.Append(il.Create(OpCodes.Call, ModuleDefinition.ImportReference(typeEqualityMethod)));
				il.Append(il.Create(OpCodes.Brtrue, syncCallInstr));

				// Post(...PropertyChanged)
				il.Append(il.Create(OpCodes.Ldarg_0));
				il.Append(il.Create(OpCodes.Ldftn, propertyChangedMethod));
				var constrMethod = typeof(System.Threading.SendOrPostCallback)
					.GetConstructors()
					.First();
				il.Append(il.Create(OpCodes.Newobj, ModuleDefinition.ImportReference(constrMethod)));
				il.Append(il.Create(OpCodes.Ldnull));
				var postMethod = typeof(System.Threading.SynchronizationContext)
					.GetMethod("Post", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance);
				il.Append(il.Create(OpCodes.Callvirt, ModuleDefinition.ImportReference(postMethod)));

				il.Append(il.Create(OpCodes.Br, asyncCallInstr));

				// sync call:
				il.Append(syncCallInstr);

				// Remove SynchronizationContext.Current from stack
				il.Append(il.Create(OpCodes.Pop));
				
				// ...PropertyChanged(null)
				il.Append(il.Create(OpCodes.Ldarg_0));
				il.Append(il.Create(OpCodes.Ldnull));
				il.Append(il.Create(OpCodes.Call, propertyChangedMethod));

				// async call (done):
				il.Append(asyncCallInstr);
			}
			else
			{
				il.Append(il.Create(OpCodes.Ldarg_0));
				il.Append(il.Create(OpCodes.Ldnull));
				il.Append(il.Create(OpCodes.Call, propertyChangedMethod));
			}

			il.Append(retInstr);
			setBody.OptimizeMacros();
		}

		/// <summary>
		/// Creates the method that calls all change handlers and raises events for a property.
		/// </summary>
		/// <param name="typeDef">The type being processed.</param>
		/// <param name="propDef">The property to process.</param>
		/// <param name="dependencies">A dictionary containing all found dependencies.</param>
		/// <param name="backingFields">A dictionary containing all property backing fields.</param>
		/// <returns></returns>
		private MethodDefinition CreatePropertyChangedMethod(
			TypeDefinition typeDef,
			PropertyDefinition propDef,
			CollectionDictionary<string, string> dependencies,
			Dictionary<string, FieldReference> backingFields)
		{
			var methodDef = new MethodDefinition(
				$"<{propDef.Name}>PropertyChanged",
				MethodAttributes.Private,
				typeDef.Module.TypeSystem.Void);
			typeDef.Methods.Add(methodDef);
			AddCompilerGeneratedAttribute(methodDef);

			// Add unused object parameter to make signature compatible with SynchronizationContext.Post
			methodDef.Parameters.Add(new ParameterDefinition(ModuleDefinition.TypeSystem.Object));

			var il = methodDef.Body.GetILProcessor();

			// Call property changed handler method
			var changedMethod = typeDef.Methods
				.FirstOrDefault(m => m.Name == $"On{propDef.Name}Changed" &&
					!m.IsStatic &&
					m.ReturnType == typeDef.Module.TypeSystem.Void &&
					!m.HasParameters &&
					!m.HasGenericParameters);
			if (changedMethod == null &&
				typeDef.Methods.Any(m => m.Name == $"On{propDef.Name}Changed" &&
					!m.HasCustomAttribute(ignoreUnsupportedSignatureAttributeName)))
			{
				LogWarning($"Method signature of {typeDef.FullName}.On{propDef.Name}Changed method is unsupported, method is ignored. Use the IgnoreUnsupportedSignature attribute to avoid this warning.");
			}
			if (changedMethod != null)
			{
				il.Append(il.Create(OpCodes.Ldarg_0));
				if (changedMethod.IsVirtual)
					il.Append(il.Create(OpCodes.Callvirt, changedMethod));
				else
					il.Append(il.Create(OpCodes.Call, changedMethod));
			}

			// Call PropertyChanged event handler for property and all dependent properties
			il.Append(il.Create(OpCodes.Ldarg_0));
			// ldfld class [System]System.ComponentModel.PropertyChangedEventHandler {typeDef}::PropertyChanged
			FieldReference eventField = typeDef.Fields.FirstOrDefault(x => x.FieldType.IsPropertyChangedEventHandler());
			TypeDefinition baseType = typeDef;
			while (eventField == null && baseType.BaseType != null)
			{
				baseType = baseType.BaseType.Resolve();
				eventField = baseType.Fields.FirstOrDefault(x => x.FieldType.IsPropertyChangedEventHandler());
				if (eventField != null)
				{
					var eventFieldDef = eventField.Resolve();
					eventFieldDef.IsFamily = true;
				}
			}
			il.Append(il.Create(OpCodes.Ldfld, eventField));
			il.Append(il.Create(OpCodes.Dup));
			var trueLabel = il.Create(OpCodes.Nop);
			il.Append(il.Create(OpCodes.Brtrue, trueLabel));

			il.Append(il.Create(OpCodes.Pop));
			var falseLabel = il.Create(OpCodes.Nop);
			il.Append(il.Create(OpCodes.Br, falseLabel));

			il.Append(trueLabel);
			var notifyPropNames = dependencies.GetValuesOrEmpty(propDef.Name).Where(n => n.StartsWith("P:")).Select(n => n.Substring(2)).ToList();
			notifyPropNames.Insert(0, propDef.Name);
			notifyPropNames = notifyPropNames.Distinct().ToList();
			for (int i = 0; i < notifyPropNames.Count; i++)
			{
				string notifyPropName = notifyPropNames[i];
				if (i < notifyPropNames.Count - 1)
					il.Append(il.Create(OpCodes.Dup));
				il.Append(il.Create(OpCodes.Ldarg_0));
				il.Append(il.Create(OpCodes.Ldstr, notifyPropName));
				// newobj instance void [System]System.ComponentModel.PropertyChangedEventArgs::.ctor(string)
				il.Append(il.Create(OpCodes.Newobj, refs.ComponentModelPropertyChangedEventConstructorReference));
				// callvirt instance void [System]System.ComponentModel.PropertyChangedEventHandler::Invoke(object, class [System]System.ComponentModel.PropertyChangedEventArgs)
				il.Append(il.Create(OpCodes.Callvirt, refs.ComponentModelPropertyChangedEventHandlerInvokeReference));
			}

			il.Append(falseLabel);

			// Update dependent commands
			var notifyCommandNames = dependencies.GetValuesOrEmpty(propDef.Name).Where(n => n.StartsWith("C:")).Select(n => n.Substring(2));
			foreach (string notifyCommandName in notifyCommandNames.Distinct())
			{
				il.Append(il.Create(OpCodes.Ldarg_0));
				il.Append(il.Create(OpCodes.Ldfld, backingFields[notifyCommandName]));
				il.Append(il.Create(OpCodes.Call, refs.DelegateCommandType.Methods.First(m => m.Name == "RaiseCanExecuteChanged")));
			}

			il.Append(il.Create(OpCodes.Ret));
			methodDef.Body.OptimizeMacros();
			return methodDef;
		}

		/// <summary>
		/// Implements a command property and connects it with handler methods.
		/// </summary>
		/// <param name="typeDef">The type being processed.</param>
		/// <param name="propDefs">A collection containing all command properties.</param>
		/// <param name="backingFields">A dictionary containing all property backing fields.</param>
		private void ProcessCommandProperties(
			TypeDefinition typeDef,
			IEnumerable<PropertyDefinition> propDefs,
			Dictionary<string, FieldReference> backingFields)
		{
			var methodDef = new MethodDefinition(
				"<VMK>InitializeCommands",
				MethodAttributes.Private,
				typeDef.Module.TypeSystem.Void);
			typeDef.Methods.Add(methodDef);
			AddCompilerGeneratedAttribute(methodDef);

			var il = methodDef.Body.GetILProcessor();

			foreach (var propDef in propDefs)
			{
				// Find command handler methods by name (Can... and On... - without "Command" suffix)
				string commandName = propDef.Name;
				if (commandName.EndsWith("Command"))
				{
					commandName = commandName.Substring(0, commandName.Length - "Command".Length);
				}
				var executeMethod = typeDef.Methods
					.FirstOrDefault(m => m.Name == $"On{commandName}" &&
						!m.IsStatic &&
						m.ReturnType == typeDef.Module.TypeSystem.Void &&
						(!m.HasParameters || m.Parameters.Count == 1 && m.Parameters[0].ParameterType == typeDef.Module.TypeSystem.Object) &&
						!m.HasGenericParameters);
				if (executeMethod == null &&
					typeDef.Methods.Any(m => m.Name == $"On{commandName}" &&
						!m.HasCustomAttribute(ignoreUnsupportedSignatureAttributeName)))
				{
					LogWarning($"Method signature of {typeDef.FullName}.On{commandName} method is unsupported, method is ignored. Use the IgnoreUnsupportedSignature attribute to avoid this warning.");
				}
				var canExecuteMethod = typeDef.Methods
					.FirstOrDefault(m => m.Name == $"Can{commandName}" &&
						!m.IsStatic &&
						m.ReturnType == typeDef.Module.TypeSystem.Boolean &&
						(!m.HasParameters || m.Parameters.Count == 1 && m.Parameters[0].ParameterType == typeDef.Module.TypeSystem.Object) &&
						!m.HasGenericParameters);
				if (canExecuteMethod == null &&
					typeDef.Methods.Any(m => m.Name == $"Can{commandName}" &&
						!m.HasCustomAttribute(ignoreUnsupportedSignatureAttributeName)))
				{
					LogWarning($"Method signature of {typeDef.FullName}.Can{commandName} method is unsupported, method is ignored. Use the IgnoreUnsupportedSignature attribute to avoid this warning.");
				}

				// Assign command property a new DelegateCommand instance
				il.Append(il.Create(OpCodes.Ldarg_0));   // For property setter
				IEnumerable<MethodDefinition> constructors = refs.DelegateCommandType.GetConstructors().Where(c => !c.IsStatic);
				if (executeMethod == null)
				{
					constructors = null;

					il.Append(il.Create(OpCodes.Ldsfld, refs.DelegateCommandType.Fields.First(f => f.Name == "Disabled")));
				}
				else if (!executeMethod.HasParameters)
				{
					// First parameter is Action
					constructors = constructors.Where(c => !c.Parameters[0].ParameterType.ToString().Contains("System.Object"));

					il.Append(il.Create(OpCodes.Ldarg_0));
					il.Append(il.Create(OpCodes.Ldftn, executeMethod));
					il.Append(il.Create(OpCodes.Newobj, ModuleDefinition.ImportReference(typeof(Action).GetConstructors().First())));
				}
				else if (executeMethod.HasParameters)
				{
					// First parameter is Action<object>
					constructors = constructors.Where(c => c.Parameters[0].ParameterType.ToString().Contains("System.Object"));

					il.Append(il.Create(OpCodes.Ldarg_0));
					il.Append(il.Create(OpCodes.Ldftn, executeMethod));
					il.Append(il.Create(OpCodes.Newobj, ModuleDefinition.ImportReference(typeof(Action<object>).GetConstructors().First())));
				}

				if (constructors != null)
				{
					if (canExecuteMethod == null)
					{
						// No second parameter
					}
					else if (!canExecuteMethod.HasParameters)
					{
						// Second parameter is Func<bool>
						constructors = constructors.Where(c => c.Parameters.Count == 2);
						constructors = constructors.Where(c => !c.Parameters[1].ParameterType.ToString().Contains("System.Object"));

						il.Append(il.Create(OpCodes.Ldarg_0));
						il.Append(il.Create(OpCodes.Ldftn, canExecuteMethod));
						il.Append(il.Create(OpCodes.Newobj, ModuleDefinition.ImportReference(typeof(Func<bool>).GetConstructors().First())));
					}
					else if (canExecuteMethod.HasParameters)
					{
						// Second parameter is Func<object, bool>
						constructors = constructors.Where(c => c.Parameters.Count == 2);
						constructors = constructors.Where(c => c.Parameters[1].ParameterType.ToString().Contains("System.Object"));

						il.Append(il.Create(OpCodes.Ldarg_0));
						il.Append(il.Create(OpCodes.Ldftn, canExecuteMethod));
						il.Append(il.Create(OpCodes.Newobj, ModuleDefinition.ImportReference(typeof(Func<object, bool>).GetConstructors().First())));
					}
				}

				if (constructors != null)
				{
					il.Append(il.Create(OpCodes.Newobj, constructors.First()));
				}

				// Directly set the backing field
				var backingField = backingFields[propDef.Name].Resolve();
				backingField.IsInitOnly = false;
				il.Append(il.Create(OpCodes.Stfld, backingField));
			}

			il.Append(il.Create(OpCodes.Ret));
			methodDef.Body.OptimizeMacros();

			// Call method at the beginning of any constructor
			foreach (var constructor in typeDef.GetConstructors().Where(c => !c.IsStatic))
			{
				var firstCall = constructor.Body.Instructions
					.First(i => i.OpCode == OpCodes.Call && (i.Operand as MethodReference).Resolve().IsConstructor);
				il = constructor.Body.GetILProcessor();
				// Insert instructions in reverse order each after the base constructor call
				il.InsertAfter(firstCall, il.Create(OpCodes.Call, methodDef));
				il.InsertAfter(firstCall, il.Create(OpCodes.Ldarg_0));
			}
		}

		/// <summary>
		/// Adds the CompilerGeneratedAttribute to a type or method.
		/// </summary>
		/// <param name="source">The object to add the attribute to.</param>
		private void AddCompilerGeneratedAttribute(ICustomAttributeProvider source)
		{
			if (addCompilerGeneratedAttribute)
			{
				source.CustomAttributes.Add(new CustomAttribute(refs.CompilerGeneratedAttributeConstructorReference));
			}
		}

		/// <summary>
		/// Fixes all ViewModelKit references in the module.
		/// </summary>
		private void FixReferences()
		{
			// Scans entire module for references to ViewModelKit types and fix them to the copied type
			ModuleDefinition.UpdateReferences(refs.MemberMap);

			// Remove all custom attributes defined in ViewModelKit from an object
			ModuleDefinition.RemoveCustomAttributes(attr => attr.AttributeType.Namespace == viewModelKitNamespace);

			// Remove ViewModelKit assembly reference from target module
			ModuleDefinition.AssemblyReferences.Remove(refs.ViewModelKitAssemblyNameReference);
		}
	}
}
