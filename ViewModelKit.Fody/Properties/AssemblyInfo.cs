using System.Reflection;
using System.Runtime.InteropServices;

[assembly: AssemblyProduct("ViewModelKit.Fody")]
[assembly: AssemblyTitle("ViewModelKit.Fody")]
[assembly: AssemblyDescription("Makes WPF ViewModel classes smart by default. Implements INotifyPropertyChanged and DelegateCommands for auto properties, recognises dependent properties, connects property changed handlers, triggers validation. Supports virtual properties with Entity Famework.")]
[assembly: AssemblyCopyright(AssemblyInfo.Copyright)]
[assembly: AssemblyCompany("unclassified software development")]

// Assembly identity version. Must be a dotted-numeric version.
[assembly: AssemblyVersion(AssemblyInfo.Version)]

// Repeat for Win32 file version resource because the assembly version is expanded to 4 parts.
[assembly: AssemblyFileVersion(AssemblyInfo.Version)]

#if DEBUG
[assembly: AssemblyConfiguration("Debug")]
#else
[assembly: AssemblyConfiguration("Release")]
#endif

// Other attributes
[assembly: ComVisible(false)]

public static class AssemblyInfo
{
	public const string Version = "1.2.2";
	public const string Copyright = "© 2016–2018 Yves Goergen";
	// NOTE: Also update copyright year in the LICENSE.txt file
}
