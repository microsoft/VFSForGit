SCRIPTDIR="$(dirname ${BASH_SOURCE[0]})"
SRCDIR=$SCRIPTDIR/../..
BUILDDIR=$SRCDIR/../BuildOutput/GVFS.Build
PACKAGESDIR=$SRCDIR/../packages
GITVERSION="$($SCRIPTDIR/GetGitVersionNumber.sh)"
cp $SRCDIR/nuget.config $BUILDDIR
dotnet new classlib -n GVFS.Restore -o $BUILDDIR --force
dotnet add $BUILDDIR/GVFS.Restore.csproj package --package-directory $PACKAGESDIR GitForMac.GVFS.Installer --version $GITVERSION