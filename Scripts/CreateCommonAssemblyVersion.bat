mkdir %2\BuildOutput
echo using System.Reflection; [assembly: AssemblyVersion("%1")][assembly: AssemblyFileVersion("%1")] > %2\BuildOutput\CommonAssemblyVersion.cs