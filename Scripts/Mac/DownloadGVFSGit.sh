. "$(dirname ${BASH_SOURCE[0]})/InitializeEnvironment.sh"

BUILDDIR=$VFS_OUTPUTDIR/GVFS.Build
GITVERSION="$($VFS_SCRIPTDIR/GetGitVersionNumber.sh)"
cp $VFS_SRCDIR/nuget.config $BUILDDIR
dotnet new classlib -n Restore.GitInstaller -o $BUILDDIR --force
dotnet add $BUILDDIR/Restore.GitInstaller.csproj package --package-directory $VFS_PACKAGESDIR GitForMac.GVFS.Installer --version $GITVERSION