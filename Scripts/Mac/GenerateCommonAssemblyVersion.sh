. "$(dirname ${BASH_SOURCE[0]})/InitializeEnvironment.sh"

if [ -z $1 ]; then
  echo "Version Number not defined for CommonAssemblyVersion.cs"
fi

# Update the version number in GVFS.props for other consumers of GVFSVersion
sed -i "" -E "s@<GVFSVersion>[0-9]+(\.[0-9]+)*</GVFSVersion>@<GVFSVersion>$1</GVFSVersion>@g" $VFS_SRCDIR/GVFS/GVFS.Build/GVFS.props

# Then generate CommonAssemblyVersion.cs
cat >$VFS_OUTPUTDIR/CommonAssemblyVersion.cs <<TEMPLATE
using System.Reflection;

[assembly: AssemblyVersion("$1")]
[assembly: AssemblyFileVersion("$1")]
TEMPLATE
