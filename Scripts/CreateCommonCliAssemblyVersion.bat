mkdir %2\BuildOutput
echo #include "stdafx.h" > %2\BuildOutput\CommonAssemblyVersion.h
echo using namespace System::Reflection; [assembly:AssemblyVersion("%1")];[assembly:AssemblyFileVersion("%1")]; >> %2\BuildOutput\CommonAssemblyVersion.h