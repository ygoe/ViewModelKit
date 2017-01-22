using System;
using System.Collections.Generic;
using System.Linq;
using Mono.Cecil;

namespace ViewModelKit.Fody
{
	/// <summary>
	/// Locates and provides references to commonly used types and members.
	/// </summary>
	internal class References
	{
		public AssemblyNameReference ViewModelKitAssemblyNameReference;
		public TypeReference PropChangedInterfaceReference;
		public TypeReference PropChangedHandlerReference;
		public MethodReference ComponentModelPropertyChangedEventHandlerInvokeReference;
		public MethodReference ComponentModelPropertyChangedEventConstructorReference;
		public MethodReference CompilerGeneratedAttributeConstructorReference;

		public TypeDefinition ViewModelBaseType;
		public TypeDefinition DelegateCommandType;
		public TypeDefinition ValidatingViewModelBaseType;
		//public TypeDefinition ValidatingObservableCollectionType;   // TODO: Generic types can't be copied yet, see CecilExtensions.CopyToModule
		public TypeDefinition InputCleanupType;
		public Dictionary<IMemberDefinition, IMemberDefinition> MemberMap = new Dictionary<IMemberDefinition, IMemberDefinition>();

		private ModuleDefinition moduleDef;
		private TypeDefinition equalityComparerDefinition;

		/// <summary>
		/// Initialises a new instance of the <see cref="References"/> class.
		/// </summary>
		/// <param name="moduleDef">The target module.</param>
		public References(ModuleDefinition moduleDef)
		{
			this.moduleDef = moduleDef;

			var systemDefinition = moduleDef.AssemblyResolver.Resolve("System");
			var systemTypes = systemDefinition.MainModule.Types;

			var propChangedInterfaceDefinition = systemTypes.First(x => x.Name == "INotifyPropertyChanged");
			PropChangedInterfaceReference = moduleDef.ImportReference(propChangedInterfaceDefinition);
			var propChangedHandlerDefinition = systemTypes.First(x => x.Name == "PropertyChangedEventHandler");
			PropChangedHandlerReference = moduleDef.ImportReference(propChangedHandlerDefinition);
			ComponentModelPropertyChangedEventHandlerInvokeReference = moduleDef.ImportReference(propChangedHandlerDefinition.Methods.First(x => x.Name == "Invoke"));
			var propChangedArgsDefinition = systemTypes.First(x => x.Name == "PropertyChangedEventArgs");
			ComponentModelPropertyChangedEventConstructorReference = moduleDef.ImportReference(propChangedArgsDefinition.Methods.First(x => x.IsConstructor));

			var msCoreLibDefinition = moduleDef.AssemblyResolver.Resolve("mscorlib");
			var msCoreTypes = msCoreLibDefinition.MainModule.Types;

			var compilerGeneratedAttributeDefinition = msCoreTypes.First(x => x.Name == "CompilerGeneratedAttribute");
			CompilerGeneratedAttributeConstructorReference = moduleDef.ImportReference(compilerGeneratedAttributeDefinition.Methods.First(x => x.IsConstructor));

			equalityComparerDefinition = msCoreTypes.First(x => x.FullName == "System.Collections.Generic.EqualityComparer`1");

			ViewModelKitAssemblyNameReference = moduleDef.AssemblyReferences.FirstOrDefault(r => r.Name == "ViewModelKit");
			if (ViewModelKitAssemblyNameReference == null)
				throw new Exception("ViewModelKit assembly is not referenced from the processed assembly.");
			var vmkModuleDef = moduleDef.AssemblyResolver.Resolve(ViewModelKitAssemblyNameReference).MainModule;
			var oldViewModelBaseType = vmkModuleDef.Types.First(t => t.FullName == "ViewModelKit.ViewModelBase");
			var oldDelegateCommandType = vmkModuleDef.Types.First(t => t.FullName == "ViewModelKit.DelegateCommand");
			var oldValidatingViewModelBaseType = vmkModuleDef.Types.First(t => t.FullName == "ViewModelKit.ValidatingViewModelBase");
			//var oldValidatingObservableCollectionType = vmkModuleDef.Types.First(t => t.FullName == "ViewModelKit.ValidatingObservableCollection`1");
			var oldInputCleanupType = vmkModuleDef.Types.First(t => t.FullName == "ViewModelKit.InputCleanup");
			ViewModelBaseType = oldViewModelBaseType.CopyToModule(moduleDef, ref MemberMap, "<VMK>ViewModelBase", "");
			DelegateCommandType = oldDelegateCommandType.CopyToModule(moduleDef, ref MemberMap, "<VMK>DelegateCommand", "");
			ValidatingViewModelBaseType = oldValidatingViewModelBaseType.CopyToModule(moduleDef, ref MemberMap, "<VMK>ValidatingViewModelBase", "");
			//ValidatingObservableCollectionType = oldValidatingObservableCollectionType.CopyToModule(moduleDef, ref MemberMap, "<VMK>ValidatingObservableCollection`1", "");
			InputCleanupType = oldInputCleanupType.CopyToModule(moduleDef, ref MemberMap, "<VMK>InputCleanup", "");
		}

		public MethodReference EqualityComparerDefaultReference(TypeReference genericType)
		{
			var genType = new GenericInstanceType(equalityComparerDefinition);
			genType.GenericArguments.Add(genericType);
			var importedGenType = moduleDef.ImportReference(genType);
			var getDefaultMethodDefinition = importedGenType.Resolve()
				.Methods.First(x =>
					x.Name == "get_Default" &&
					x.IsStatic);
			var equalityComparerDefaultReference = moduleDef.ImportReference(getDefaultMethodDefinition);
			equalityComparerDefaultReference.DeclaringType = importedGenType;
			return equalityComparerDefaultReference;
		}

		public MethodReference EqualityComparerEqualsReference(TypeReference genericType)
		{
			var genType = new GenericInstanceType(equalityComparerDefinition);
			genType.GenericArguments.Add(genericType);
			var importedGenType = moduleDef.ImportReference(genType);
			var equalsMethodDefinition = importedGenType.Resolve()
				.Methods.First(x =>
					x.Name == "Equals" &&
					!x.IsStatic);
			var equalityComparerEqualsReference = moduleDef.ImportReference(equalsMethodDefinition);
			equalityComparerEqualsReference.DeclaringType = importedGenType;
			return equalityComparerEqualsReference;
		}
	}
}
