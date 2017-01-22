using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Reflection;
using Mono.Cecil;
using Mono.Cecil.Cil;

namespace ViewModelKit.Fody
{
	internal static class CecilExtensions
	{
		#region Type information

		/// <summary>
		/// Determines whether the type inherits from the specified base type name or implements
		/// the specified interface name.
		/// </summary>
		/// <param name="typeDef">The type definition to check.</param>
		/// <param name="baseOrInterfaceFullName">The full name (Namespace.Name) of the base type or interface to look for.</param>
		/// <returns>true if <paramref name="typeDef"/> inherits from the base type or implements the interface; otherwise, false.</returns>
		public static bool Is(this TypeDefinition typeDef, string baseOrInterfaceFullName)
		{
			if (typeDef.FullName == baseOrInterfaceFullName)
			{
				return true;
			}
			if (typeDef.BaseType != null)
			{
				TypeDefinition baseTypeDef = typeDef.BaseType.Resolve();
				// TODO: What does it mean when baseTypeDef is null? Handle to avoid NullReferenceException. Also for interfaces below.
				if (Is(baseTypeDef, baseOrInterfaceFullName))
				{
					return true;
				}
			}
			foreach (var iface in typeDef.Interfaces)
			{
				TypeDefinition ifaceTypeDef = iface.Resolve();
				if (Is(ifaceTypeDef, baseOrInterfaceFullName))
				{
					return true;
				}
			}
			return false;
		}

		/// <summary>
		/// Determines whether the type is a PropertyChanged event handler.
		/// </summary>
		/// <param name="typeReference">The type to check.</param>
		/// <returns></returns>
		public static bool IsPropertyChangedEventHandler(this TypeReference typeReference)
		{
			return typeReference.FullName == "System.ComponentModel.PropertyChangedEventHandler";

			// Other possible type names (not for WPF):
			// * Windows.UI.Xaml.Data.PropertyChangedEventHandler
			// * System.Runtime.InteropServices.WindowsRuntime.EventRegistrationTokenTable`1<Windows.UI.Xaml.Data.PropertyChangedEventHandler>
		}

		#endregion Type information

		#region Property information

		/// <summary>
		/// Determines whether the property is an auto-implemented property.
		/// </summary>
		/// <param name="propDef">The property to check.</param>
		/// <returns></returns>
		public static bool IsAutoProperty(this PropertyDefinition propDef)
		{
			FieldReference unused;
			return IsAutoProperty(propDef, out unused);
		}

		/// <summary>
		/// Determines whether the property is an auto-implemented property.
		/// </summary>
		/// <param name="propDef">The property to check.</param>
		/// <param name="backingField">The backing field of the property.</param>
		/// <returns></returns>
		public static bool IsAutoProperty(this PropertyDefinition propDef, out FieldReference backingField)
		{
			backingField = null;
			if (propDef.GetMethod == null && propDef.SetMethod == null)
				return false;   // This should not be possible anyway

			if (propDef.GetMethod != null)
			{
				if (!propDef.GetMethod.HasCustomAttribute("System.Runtime.CompilerServices.CompilerGeneratedAttribute"))
					return false;

				var getBody = propDef.GetMethod.Body;
				int step = 0;
				foreach (var instr in getBody.Instructions)
				{
					if (step == 0 && instr.OpCode == OpCodes.Ldarg_0)
					{
						step++;
					}
					else if (step == 0 && instr.OpCode == OpCodes.Ldarg && instr.Operand is int && (int)instr.Operand == 0)
					{
						step++;
					}
					else if (step == 1 && instr.OpCode == OpCodes.Ldfld)
					{
						backingField = instr.Operand as FieldReference;
						if (backingField.Name != $"<{propDef.Name}>k__BackingField")
						{
							backingField = null;
							return false;
						}
						step++;
					}
					else if (instr.OpCode == OpCodes.Nop)
					{
					}
					else
					{
						break;
					}
				}
				if (step != 2) return false;
				// NOTE: VS 2013 generates more useless instructions in Debug configuration before
				//       the final 'ret'; VS 2015 never does that. We can't look at the complete
				//       method here for compatibility reasons.
			}

			if (propDef.SetMethod != null)
			{
				if (!propDef.SetMethod.HasCustomAttribute("System.Runtime.CompilerServices.CompilerGeneratedAttribute"))
					return false;

				var setBody = propDef.SetMethod.Body;
				int step = 0;
				foreach (var instr in setBody.Instructions)
				{
					if (step == 0 && instr.OpCode == OpCodes.Ldarg_0)
					{
						step++;
					}
					else if (step == 0 && instr.OpCode == OpCodes.Ldarg && instr.Operand is int && (int)instr.Operand == 0)
					{
						step++;
					}
					else if (step == 1 && instr.OpCode == OpCodes.Ldarg_1)
					{
						step++;
					}
					else if (step == 1 && instr.OpCode == OpCodes.Ldarg && instr.Operand is int && (int)instr.Operand == 1)
					{
						step++;
					}
					else if (step == 2 && instr.OpCode == OpCodes.Stfld)
					{
						if (instr.Operand as FieldReference != backingField)
						{
							backingField = null;
							return false;
						}
						step++;
					}
					else if (step == 3 && instr.OpCode == OpCodes.Ret)
					{
						step++;
					}
					else if (instr.OpCode == OpCodes.Nop)
					{
					}
					else
					{
						break;
					}
				}
				if (step != 4) return false;
			}

			return true;
		}

		/// <summary>
		/// Determines whether the property is an ICommand property.
		/// </summary>
		/// <param name="propertyDef">The property to check.</param>
		/// <returns></returns>
		public static bool IsICommandProperty(this PropertyDefinition propertyDef)
		{
			return propertyDef.PropertyType.Resolve().Is("System.Windows.Input.ICommand");
		}

		#endregion Property information

		#region Custom attributes

		/// <summary>
		/// Determines whether the entity (or any base type) has the specified custom attribute
		/// assigned.
		/// </summary>
		/// <param name="source"></param>
		/// <param name="attrTypeFullName">The full attribute type name.</param>
		/// <param name="inherit">true to search this member's inheritance chain to find the attribute; otherwise, false.</param>
		/// <returns></returns>
		public static bool HasCustomAttribute(this Mono.Cecil.ICustomAttributeProvider source, string attrTypeFullName, bool inherit = false)
		{
			return GetCustomAttribute(source, attrTypeFullName, inherit) != null;
		}

		/// <summary>
		/// Returns the custom attribute from the entity (or any base type).
		/// </summary>
		/// <param name="source"></param>
		/// <param name="attrTypeFullName">The full attribute type name.</param>
		/// <param name="inherit">true to search this member's inheritance chain to find the attribute; otherwise, false.</param>
		/// <returns></returns>
		public static CustomAttribute GetCustomAttribute(this Mono.Cecil.ICustomAttributeProvider source, string attrTypeFullName, bool inherit = false)
		{
			do
			{
				foreach (var attr in source.CustomAttributes)
				{
					if (attr.AttributeType.FullName == attrTypeFullName)
					{
						return attr;
					}
				}
				if (!inherit)
				{
					break;
				}
				var typeDef = source as TypeDefinition;
				source = typeDef?.BaseType?.Resolve();
			}
			while (source != null);
			return null;
		}

		/// <summary>
		/// Removes all custom attributes matching the predicate from all types and members in the
		/// module.
		/// </summary>
		/// <param name="moduleDef">The module to process.</param>
		/// <param name="predicate">The predicate to check.</param>
		public static void RemoveCustomAttributes(this ModuleDefinition moduleDef, Predicate<CustomAttribute> predicate)
		{
			foreach (var typeDef in moduleDef.Types)
			{
				RemoveCustomAttributes(typeDef, predicate);
			}
		}

		/// <summary>
		/// Removes all custom attributes matching the predicate from the type and all members.
		/// </summary>
		/// <param name="typeDef">The type to process.</param>
		/// <param name="predicate">The predicate to check.</param>
		public static void RemoveCustomAttributes(this TypeDefinition typeDef, Predicate<CustomAttribute> predicate)
		{
			RemoveCustomAttributesInternal(typeDef, predicate);

			foreach (var fieldDef in typeDef.Fields)
			{
				RemoveCustomAttributesInternal(fieldDef, predicate);
			}
			foreach (var methodDef in typeDef.Methods)
			{
				RemoveCustomAttributesInternal(methodDef, predicate);
			}
			foreach (var propDef in typeDef.Properties)
			{
				RemoveCustomAttributesInternal(propDef, predicate);
			}
			foreach (var eventDef in typeDef.Events)
			{
				RemoveCustomAttributesInternal(eventDef, predicate);
			}

			foreach (var nestedTypeDef in typeDef.NestedTypes)
			{
				RemoveCustomAttributesInternal(nestedTypeDef, predicate);
			}
		}

		/// <summary>
		/// Removes all custom attributes matching the predicate from a type or member.
		/// </summary>
		/// <param name="source">The type or member.</param>
		/// <param name="predicate">The predicate to check.</param>
		private static void RemoveCustomAttributesInternal(Mono.Cecil.ICustomAttributeProvider source, Predicate<CustomAttribute> predicate)
		{
			foreach (var attr in source.CustomAttributes.ToList())
			{
				if (predicate(attr))
				{
					source.CustomAttributes.Remove(attr);
				}
			}
		}

		#endregion Custom attributes

		#region Code copy

		/// <summary>
		/// Copies a type with all members to another module.
		/// </summary>
		/// <param name="sourceType"></param>
		/// <param name="targetModule"></param>
		/// <param name="newName"></param>
		/// <param name="newNamespace"></param>
		/// <returns>The copied type in the target module.</returns>
		public static TypeDefinition CopyToModule(
			this TypeDefinition sourceType,
			ModuleDefinition targetModule,
			string newName = null,
			string newNamespace = null)
		{
			var memberMap = new Dictionary<IMemberDefinition, IMemberDefinition>();
			return CopyToModule(sourceType, targetModule, ref memberMap, newName, newNamespace);
		}

		/// <summary>
		/// Copies a type with all members to another module.
		/// </summary>
		/// <param name="sourceType"></param>
		/// <param name="targetModule"></param>
		/// <param name="memberMap"></param>
		/// <param name="newName"></param>
		/// <param name="newNamespace"></param>
		/// <returns>The copied type in the target module.</returns>
		public static TypeDefinition CopyToModule(
			this TypeDefinition sourceType,
			ModuleDefinition targetModule,
			ref Dictionary<IMemberDefinition, IMemberDefinition> memberMap,
			string newName = null,
			string newNamespace = null)
		{
			// TODO: Add support for generic types and methods

			Trace.WriteLine($"Copy type {sourceType} to module {targetModule}");

			var newType = new TypeDefinition(
				newNamespace ?? sourceType.Namespace,
				newName ?? sourceType.Name,
				sourceType.Attributes);
			targetModule.Types.Add(newType);
			memberMap.Add(sourceType, newType);

			if (sourceType.BaseType != null)
			{
				IMemberDefinition baseTypeMemberDef;
				if (memberMap.TryGetValue(sourceType.BaseType.Resolve(), out baseTypeMemberDef))
				{
					newType.BaseType = (TypeDefinition)baseTypeMemberDef;
					Trace.WriteLine($"- Mapped base type: {newType.BaseType.FullName}");
				}
				else
				{
					//CopyGenericParameters(sourceType.GenericParameters, newType.GenericParameters, newType, targetModule, ref memberMap);
					//newType.BaseType = targetModule.ImportReference(sourceType.BaseType, newType);
					newType.BaseType = targetModule.ImportReference(sourceType.BaseType);
				}
			}

			// Copy nested types tree
			CopyTypes(sourceType, newType, memberMap);

			// Copy empty type members
			CopyTypeMembers(sourceType, newType, memberMap);

			// Copy method bodies and property/event methods
			CopyTypeMembers2(sourceType, newType, memberMap);

			Trace.WriteLine($"Type {sourceType} copied. Methods: " + memberMap.Keys.Select(k => k.ToString()).Aggregate((a, b) => a + " || " + b));

			return newType;
		}

		private static void CopyTypes(
			TypeDefinition sourceType,
			TypeDefinition targetType,
			Dictionary<IMemberDefinition, IMemberDefinition> memberMap)
		{
			var targetModule = targetType.Module;

			foreach (var subtypeDef in sourceType.NestedTypes)
			{
				var newSubtypeDef = new TypeDefinition(
					subtypeDef.Namespace,
					subtypeDef.Name,
					subtypeDef.Attributes,
					memberMap.Find(subtypeDef.BaseType) as TypeReference ?? targetModule.ImportReference(subtypeDef.BaseType));
				memberMap.Add(subtypeDef, newSubtypeDef);
				targetType.NestedTypes.Add(newSubtypeDef);

				CopyTypes(subtypeDef, newSubtypeDef, memberMap);
			}
		}

		private static void CopyTypeMembers(
			TypeDefinition sourceType,
			TypeDefinition targetType,
			Dictionary<IMemberDefinition, IMemberDefinition> memberMap)
		{
			var targetModule = targetType.Module;

			foreach (var attr in sourceType.CustomAttributes)
			{
				var newAttr = new CustomAttribute(targetModule.ImportReference(attr.Constructor), attr.GetBlob());
				targetType.CustomAttributes.Add(newAttr);
			}
			foreach (var ifaceRef in sourceType.Interfaces)
			{
				targetType.Interfaces.Add(targetModule.ImportReference(ifaceRef));
			}
			foreach (var fieldDef in sourceType.Fields)
			{
				var newFieldDef = new FieldDefinition(
					fieldDef.Name,
					fieldDef.Attributes,
					memberMap.Find(fieldDef.FieldType) as TypeReference ?? targetModule.ImportReference(fieldDef.FieldType));
				memberMap.Add(fieldDef, newFieldDef);
				targetType.Fields.Add(newFieldDef);
			}
			foreach (var methodDef in sourceType.Methods)
			{
				var newMethodDef = new MethodDefinition(
					methodDef.Name,
					methodDef.Attributes,
					memberMap.Find(methodDef.ReturnType) as TypeReference ?? targetModule.ImportReference(methodDef.ReturnType));
				memberMap.Add(methodDef, newMethodDef);
				targetType.Methods.Add(newMethodDef);
			}
			foreach (var propDef in sourceType.Properties)
			{
				var newPropDef = new PropertyDefinition(
					propDef.Name,
					propDef.Attributes,
					memberMap.Find(propDef.PropertyType) as TypeReference ?? targetModule.ImportReference(propDef.PropertyType));
				memberMap.Add(propDef, newPropDef);
				targetType.Properties.Add(newPropDef);
			}
			foreach (var eventDef in sourceType.Events)
			{
				var newEventDef = new EventDefinition(
					eventDef.Name,
					eventDef.Attributes,
					memberMap.Find(eventDef.EventType) as TypeReference ?? targetModule.ImportReference(eventDef.EventType));
				memberMap.Add(eventDef, newEventDef);
				targetType.Events.Add(newEventDef);
			}
			foreach (var nestedTypeDef in sourceType.NestedTypes)
			{
				var newNestedTypeDef = (TypeDefinition)memberMap[nestedTypeDef];
				CopyTypeMembers(nestedTypeDef, newNestedTypeDef, memberMap);
			}
		}

		private static void CopyTypeMembers2(
			TypeDefinition sourceType,
			TypeDefinition targetType,
			Dictionary<IMemberDefinition, IMemberDefinition> memberMap)
		{
			foreach (var propDef in sourceType.Properties)
			{
				var newPropDef = (PropertyDefinition)memberMap[propDef];
				if (propDef.GetMethod != null)
					newPropDef.GetMethod = (MethodDefinition)memberMap[propDef.GetMethod];
				if (propDef.SetMethod != null)
					newPropDef.SetMethod = (MethodDefinition)memberMap[propDef.SetMethod];
			}
			foreach (var eventDef in sourceType.Events)
			{
				var newEventDef = (EventDefinition)memberMap[eventDef];
				newEventDef.AddMethod = (MethodDefinition)memberMap[eventDef.AddMethod];
				newEventDef.RemoveMethod = (MethodDefinition)memberMap[eventDef.RemoveMethod];
			}
			foreach (var methodDef in sourceType.Methods)
			{
				Trace.WriteLine($"Copy method body {methodDef} to type {targetType}");
				var newMethodDef = (MethodDefinition)memberMap[methodDef];
				CopyMethodContents(methodDef, newMethodDef, memberMap);
			}
			foreach (var nestedTypeDef in sourceType.NestedTypes)
			{
				var newNestedTypeDef = (TypeDefinition)memberMap[nestedTypeDef];
				CopyTypeMembers2(nestedTypeDef, newNestedTypeDef, memberMap);
			}
		}

		private static IMemberDefinition Find(
			this Dictionary<IMemberDefinition, IMemberDefinition> memberMap,
			object key)
		{
			if (memberMap == null) return null;

			IMemberDefinition value;
			TypeReference typeRef;
			FieldReference fieldRef;
			MethodReference methodRef;
			PropertyReference propertyRef;
			EventReference eventRef;
			if ((typeRef = key as TypeReference) != null)
			{
				return memberMap.TryGetValue(typeRef.Resolve(), out value) ? value : null;
			}
			if ((fieldRef = key as FieldReference) != null)
			{
				return memberMap.TryGetValue(fieldRef.Resolve(), out value) ? value : null;
			}
			if ((methodRef = key as MethodReference) != null)
			{
				return memberMap.TryGetValue(methodRef.Resolve(), out value) ? value : null;
			}
			if ((propertyRef = key as PropertyReference) != null)
			{
				return memberMap.TryGetValue(propertyRef.Resolve(), out value) ? value : null;
			}
			if ((eventRef = key as EventReference) != null)
			{
				return memberMap.TryGetValue(eventRef.Resolve(), out value) ? value : null;
			}
			return null;
		}

		/// <summary>
		/// Copies a method to another type.
		/// </summary>
		/// <param name="sourceMethod"></param>
		/// <param name="memberMap">If set, called methods from the same type will also be copied.</param>
		/// <returns>The copied method in the target type.</returns>
		public static MethodDefinition CopyToType(
			this MethodDefinition sourceMethod,
			TypeDefinition targetType,
			Dictionary<IMemberDefinition, IMemberDefinition> memberMap)
		{
			var targetModule = targetType.Module;

			var newMethod = new MethodDefinition(
				sourceMethod.Name,
				sourceMethod.Attributes,
				memberMap.Find(sourceMethod.ReturnType) as TypeReference ?? targetModule.ImportReference(sourceMethod.ReturnType));
			CopyMethodContents(sourceMethod, newMethod, memberMap);
			targetType.Methods.Add(newMethod);
			return newMethod;
		}

		private static void CopyMethodContents(
			MethodDefinition sourceMethod,
			MethodDefinition targetMethod,
			Dictionary<IMemberDefinition, IMemberDefinition> memberMap)
		{
			var targetModule = targetMethod.Module;

			foreach (var paramDef in sourceMethod.Parameters)
			{
				targetMethod.Parameters.Add(new ParameterDefinition(memberMap.Find(paramDef.ParameterType) as TypeReference ?? targetModule.ImportReference(paramDef.ParameterType)));
			}
			foreach (var varDef in sourceMethod.Body.Variables)
			{
				targetMethod.Body.Variables.Add(new VariableDefinition(memberMap.Find(varDef.VariableType) as TypeReference ?? targetModule.ImportReference(varDef.VariableType)));
				targetMethod.Body.InitLocals = true;
			}
			foreach (var instr in sourceMethod.Body.Instructions)
			{
				//var newInstruction = new Instruction(instr.OpCode, instr.Operand);
				var constructorInfo = typeof(Instruction).GetConstructor(BindingFlags.NonPublic | BindingFlags.Instance, null, new[] { typeof(OpCode), typeof(object) }, null);
				var newInstr = (Instruction)constructorInfo.Invoke(new[] { instr.OpCode, instr.Operand });

				var fieldRef = newInstr.Operand as FieldReference;
				if (fieldRef != null)
				{
					newInstr.Operand = memberMap.Find(fieldRef) as FieldReference ?? targetModule.ImportReference(fieldRef);
				}
				var methodRef = newInstr.Operand as MethodReference;
				if (methodRef != null)
				{
					newInstr.Operand = memberMap.Find(methodRef) as MethodReference ?? targetModule.ImportReference(methodRef);
				}
				var typeRef = newInstr.Operand as TypeReference;
				if (typeRef != null)
				{
					newInstr.Operand = memberMap.Find(typeRef) as TypeReference ?? targetModule.ImportReference(typeRef);
				}

				targetMethod.Body.Instructions.Add(newInstr);
			}
			foreach (var handler in sourceMethod.Body.ExceptionHandlers)
			{
				var newHandler = new ExceptionHandler(handler.HandlerType);
				if (handler.CatchType != null)
					newHandler.CatchType = memberMap.Find(handler.CatchType) as TypeReference ?? targetModule.ImportReference(handler.CatchType);
				if (handler.FilterStart != null)
					newHandler.FilterStart = targetMethod.Body.Instructions[sourceMethod.Body.Instructions.IndexOf(handler.FilterStart)];
				if (handler.HandlerEnd != null)
					newHandler.HandlerEnd = targetMethod.Body.Instructions[sourceMethod.Body.Instructions.IndexOf(handler.HandlerEnd)];
				if (handler.HandlerStart != null)
					newHandler.HandlerStart = targetMethod.Body.Instructions[sourceMethod.Body.Instructions.IndexOf(handler.HandlerStart)];
				if (handler.TryEnd != null)
					newHandler.TryEnd = targetMethod.Body.Instructions[sourceMethod.Body.Instructions.IndexOf(handler.TryEnd)];
				if (handler.TryStart != null)
					newHandler.TryStart = targetMethod.Body.Instructions[sourceMethod.Body.Instructions.IndexOf(handler.TryStart)];
				targetMethod.Body.ExceptionHandlers.Add(newHandler);
			}

			// HACK: maxstack fix - https://github.com/Fody/Fody/issues/257
			// Fake an OpCode to set a StackBehaviour of "push" instead of "pop" to trick stack size
			// computation into thinking there is more stack size used when it really isn't.
			OpCode pushingPop = (OpCode)typeof(OpCode).GetConstructors(BindingFlags.Instance | BindingFlags.NonPublic).First().Invoke(new object[]
			{
				0xff << 0 | 0x26 << 8 | (byte) Code.Pop << 16 | (byte) FlowControl.Next << 24,
				(byte) OpCodeType.Primitive << 0 | (byte) OperandType.InlineNone << 8 | (byte) StackBehaviour.Pop0 << 16 | (byte) StackBehaviour.Push1 << 24
			});
			targetMethod.Body.Instructions.Insert(targetMethod.Body.Instructions.Count - 1, Instruction.Create(OpCodes.Ldnull));
			targetMethod.Body.Instructions.Insert(targetMethod.Body.Instructions.Count - 1, Instruction.Create(OpCodes.Ldnull));
			targetMethod.Body.Instructions.Insert(targetMethod.Body.Instructions.Count - 1, Instruction.Create(OpCodes.Ldnull));
			targetMethod.Body.Instructions.Insert(targetMethod.Body.Instructions.Count - 1, Instruction.Create(OpCodes.Ldnull));
			targetMethod.Body.Instructions.Insert(targetMethod.Body.Instructions.Count - 1, Instruction.Create(pushingPop));
			targetMethod.Body.Instructions.Insert(targetMethod.Body.Instructions.Count - 1, Instruction.Create(pushingPop));
			targetMethod.Body.Instructions.Insert(targetMethod.Body.Instructions.Count - 1, Instruction.Create(pushingPop));
			targetMethod.Body.Instructions.Insert(targetMethod.Body.Instructions.Count - 1, Instruction.Create(pushingPop));
			// Fix handlers for inserted instructions
			var lastInstr = targetMethod.Body.Instructions[targetMethod.Body.Instructions.Count - 1];
			foreach (var handler in targetMethod.Body.ExceptionHandlers.Where(h => h.HandlerEnd == lastInstr).ToList())
			{
				handler.HandlerEnd = targetMethod.Body.Instructions[targetMethod.Body.Instructions.Count - 9];
			}
		}

		// TODO: Currently unused. This is not enough to copy ValidatingObservableCollection`1 to another module.
		private static void CopyGenericParameters(
			Mono.Collections.Generic.Collection<GenericParameter> input,
			Mono.Collections.Generic.Collection<GenericParameter> output,
			IGenericParameterProvider nt,
			ModuleDefinition targetModule,
			ref Dictionary<IMemberDefinition, IMemberDefinition> memberMap)
		{
			foreach (GenericParameter gp in input)
			{
				GenericParameter ngp = new GenericParameter(gp.Name, nt);
				ngp.Attributes = gp.Attributes;
				output.Add(ngp);
			}
			// Defer copy to ensure all generic parameters are already present
			for (int i = 0; i < input.Count; i++)
			{
				foreach (TypeReference constraintTypeRef in input[i].Constraints)
				{
					IMemberDefinition constraintTypeMemberDef;
					if (memberMap.TryGetValue(constraintTypeRef.Resolve(), out constraintTypeMemberDef))
					{
						output[i].Constraints.Add((TypeDefinition)constraintTypeMemberDef);
						Trace.WriteLine($"- Mapped generic constraint type: {constraintTypeMemberDef.FullName}");
					}
					else
					{
						output[i].Constraints.Add(targetModule.ImportReference(constraintTypeRef));
					}
				}
			}
		}

		#endregion Code copy

		#region Reference updating

		/// <summary>
		/// Updates all references in the module from the map.
		/// </summary>
		/// <param name="moduleDef">The module to process.</param>
		/// <param name="memberMap">A dictionary mapping old to new references.</param>
		public static void UpdateReferences(this ModuleDefinition moduleDef, Dictionary<IMemberDefinition, IMemberDefinition> memberMap)
		{
			foreach (var typeDef in moduleDef.Types)
			{
				UpdateReferences(typeDef, memberMap);
			}
		}

		/// <summary>
		/// Updates all references in the type from the map.
		/// </summary>
		/// <param name="typeDef">The type to process.</param>
		/// <param name="memberMap">A dictionary mapping old to new references.</param>
		public static void UpdateReferences(this TypeDefinition typeDef, Dictionary<IMemberDefinition, IMemberDefinition> memberMap)
		{
			IMemberDefinition newMemberDef;

			foreach (var fieldDef in typeDef.Fields)
			{
				var fieldTypeDef = fieldDef.FieldType.Resolve();
				if (fieldTypeDef != null)
				{
					if (memberMap.TryGetValue(fieldTypeDef, out newMemberDef))
					{
						fieldDef.FieldType = (TypeReference)newMemberDef;
					}
				}
				UpdateGenericInstance(fieldDef.FieldType as IGenericInstance, memberMap);
			}
			foreach (var methodDef in typeDef.Methods)
			{
				UpdateMethodSignature(methodDef, memberMap);

				if (methodDef.Body?.Instructions != null)
				{
					foreach (var v in methodDef.Body.Variables)
					{
						TypeDefinition opTypeDef;
						if ((opTypeDef = (v.VariableType as TypeReference)?.Resolve()) != null &&
							memberMap.TryGetValue(opTypeDef, out newMemberDef))
						{
							v.VariableType = (TypeReference)newMemberDef;
						}
						if (v.VariableType is IGenericInstance)
						{
							UpdateGenericInstance(v.VariableType as IGenericInstance, memberMap);
						}
					}
					foreach (var instr in methodDef.Body.Instructions)
					{
						TypeDefinition opTypeDef;
						FieldDefinition opFieldDef;
						MethodDefinition opMethodDef;
						PropertyDefinition opPropDef;
						EventDefinition opEventDef;
						if ((opTypeDef = (instr.Operand as TypeReference)?.Resolve()) != null &&
							memberMap.TryGetValue(opTypeDef, out newMemberDef))
						{
							instr.Operand = newMemberDef;
						}
						else if ((opFieldDef = (instr.Operand as FieldReference)?.Resolve()) != null &&
							memberMap.TryGetValue(opFieldDef, out newMemberDef))
						{
							instr.Operand = newMemberDef;
						}
						else if ((opMethodDef = (instr.Operand as MethodReference)?.Resolve()) != null &&
							memberMap.TryGetValue(opMethodDef, out newMemberDef))
						{
							instr.Operand = newMemberDef;
						}
						else if ((opPropDef = (instr.Operand as PropertyReference)?.Resolve()) != null &&
							memberMap.TryGetValue(opPropDef, out newMemberDef))
						{
							instr.Operand = newMemberDef;
						}
						else if ((opEventDef = (instr.Operand as EventReference)?.Resolve()) != null &&
							memberMap.TryGetValue(opEventDef, out newMemberDef))
						{
							instr.Operand = newMemberDef;
						}

						if (instr.Operand is MethodReference)
						{
							UpdateMethodSignature(instr.Operand as MethodReference, memberMap);
						}

						if (instr.Operand is IGenericInstance)
						{
							UpdateGenericInstance(instr.Operand as IGenericInstance, memberMap);
						}
						if ((instr.Operand as MemberReference)?.DeclaringType is IGenericInstance)
						{
							UpdateGenericInstance((instr.Operand as MemberReference).DeclaringType as IGenericInstance, memberMap);
						}
						if ((instr.Operand as FieldReference)?.FieldType is IGenericInstance)
						{
							UpdateGenericInstance((instr.Operand as FieldReference).FieldType as IGenericInstance, memberMap);
						}
						if ((instr.Operand as PropertyReference)?.PropertyType is IGenericInstance)
						{
							UpdateGenericInstance((instr.Operand as PropertyReference).PropertyType as IGenericInstance, memberMap);
						}
						if ((instr.Operand as EventReference)?.EventType is IGenericInstance)
						{
							UpdateGenericInstance((instr.Operand as EventReference).EventType as IGenericInstance, memberMap);
						}
					}
				}
			}
			foreach (var propDef in typeDef.Properties)
			{
				var propTypeDef = propDef.PropertyType.Resolve();
				if (propTypeDef != null)
				{
					if (memberMap.TryGetValue(propTypeDef, out newMemberDef))
					{
						propDef.PropertyType = (TypeReference)newMemberDef;
					}
				}
				UpdateGenericInstance(propDef.PropertyType as IGenericInstance, memberMap);
			}
			foreach (var eventDef in typeDef.Events)
			{
				var eventTypeDef = eventDef.EventType.Resolve();
				if (eventTypeDef != null)
				{
					if (memberMap.TryGetValue(eventTypeDef, out newMemberDef))
					{
						eventDef.EventType = (TypeReference)newMemberDef;
					}
				}
				UpdateGenericInstance(eventDef.EventType as IGenericInstance, memberMap);
			}

			foreach (var nestedTypeDef in typeDef.NestedTypes)
			{
				UpdateReferences(nestedTypeDef, memberMap);
			}
		}

		/// <summary>
		/// Updates references in a method signature.
		/// </summary>
		/// <param name="methodRef">The method to process.</param>
		/// <param name="memberMap">A dictionary mapping old to new references.</param>
		private static void UpdateMethodSignature(this MethodReference methodRef, Dictionary<IMemberDefinition, IMemberDefinition> memberMap)
		{
			IMemberDefinition newMemberDef;

			foreach (var param in methodRef.Parameters)
			{
				var paramTypeDef = param.ParameterType.Resolve();
				if (paramTypeDef != null)
				{
					if (memberMap.TryGetValue(paramTypeDef, out newMemberDef))
					{
						param.ParameterType = (TypeReference)newMemberDef;
					}
				}
				UpdateGenericInstance(param.ParameterType as IGenericInstance, memberMap);
			}
			var returnTypeDef = methodRef.ReturnType.Resolve();
			if (returnTypeDef != null)
			{
				if (memberMap.TryGetValue(returnTypeDef, out newMemberDef))
				{
					methodRef.ReturnType = (TypeReference)newMemberDef;
				}
			}
			UpdateGenericInstance(methodRef.ReturnType as IGenericInstance, memberMap);
		}

		/// <summary>
		/// Updates references in a generic instance.
		/// </summary>
		/// <param name="genInst">The generic instance to process.</param>
		/// <param name="memberMap">A dictionary mapping old to new references.</param>
		private static void UpdateGenericInstance(this IGenericInstance genInst, Dictionary<IMemberDefinition, IMemberDefinition> memberMap)
		{
			if (genInst != null)
			{
				for (int i = 0; i < genInst.GenericArguments.Count; i++)
				{
					var arg = genInst.GenericArguments[i];
					if (arg is IGenericInstance)
					{
						UpdateGenericInstance(arg as IGenericInstance, memberMap);
					}
					else
					{
						var argDef = arg.Resolve();
						if (argDef != null)
						{
							IMemberDefinition newArg;
							if (memberMap.TryGetValue(argDef, out newArg))
							{
								genInst.GenericArguments[i] = (TypeReference)newArg;
							}
						}
					}
				}
			}
		}

		#endregion Reference updating
	}
}
