. "$(dirname ${BASH_SOURCE[0]})/InitializeEnvironment.sh"

GVFSPROPS=$VFS_SRCDIR/GVFS/GVFS.Build/GVFS.props
VERSIONNUMBER="$(cat $GVFSPROPS | grep GVFSVersion | grep -Eo '[0-9.]+(-\w+)?')"

cat >$VFS_OUTPUTDIR/CommonAssemblyVersion.cs <<TEMPLATE
using System.Reflection;

[assembly: AssemblyVersion("$VERSIONNUMBER")]
[assembly: AssemblyFileVersion("$VERSIONNUMBER")]
TEMPLATE
