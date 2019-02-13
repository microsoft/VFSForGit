#!/bin/bash

SOURCEDIRECTORY=$1
if [ -z $SOURCEDIRECTORY ]; then
  echo "Error: Source directory not specified"
  exit 1
fi

CONFIGURATION=$2
if [ -z $CONFIGURATION ]; then
  echo "Error: Build configuration not specified"
  exit 1
fi

PACKAGEVERSION=$3
if [ -z $PACKAGEVERSION ]; then
  echo "Error: Installer package version not specified"
  exit 1
fi

BUILDOUTPUTDIR=$4
if [ -z $BUILDOUTPUTDIR ]; then
  echo "Error: Build output directory not specified"
  exit 1
fi

if [ -z $VFS_OUTPUTDIR ]; then
  echo "Error: Missing environment variable. VFS_OUTPUTDIR is not set"
  exit 1
fi

if [ -z $VFS_PUBLISHDIR ]; then
  echo "Error: Missing environment variable. VFS_PUBLISHDIR is not set"
  exit 1
fi

STAGINGDIR=$BUILDOUTPUTDIR"Staging"
VFSFORGITDESTINATION="usr/local/vfsforgit"
LIBRARYEXTENSIONSDESTINATION="Library/Extensions"
INSTALLERPACKAGENAME="VFSForGit.$PACKAGEVERSION"
INSTALLERPACKAGEID="com.vfsforgit.pkg"
UNINSTALLERPATH="${SOURCEDIRECTORY}/uninstall_vfsforgit.sh"

function CheckBuildIsAvailable()
{
    if [ ! -d "$VFS_OUTPUTDIR" ] || [ ! -d "$VFS_PUBLISHDIR" ]; then
        echo "Error: Could not find VFSForGit Build to package."
        exit 1
    fi
}

function SetPermissions()
{
    chmodCommand="chmod -R 755 \"${STAGINGDIR}\""
    eval $chmodCommand || exit 1
}
 
function CreateInstallerRoot()
{
    mkdirVfsForGit="mkdir -p \"${STAGINGDIR}/$VFSFORGITDESTINATION\""
    eval $mkdirVfsForGit || exit 1

    mkdirBin="mkdir -p \"${STAGINGDIR}/usr/local/bin\""
    eval $mkdirBin || exit 1
    
    mkdirBin="mkdir -p \"${STAGINGDIR}/$LIBRARYEXTENSIONSDESTINATION\""
    eval $mkdirBin || exit 1
}

function CopyBinariesToInstall()
{
    copyPublishDirectory="cp -Rf \"${VFS_PUBLISHDIR}\"/* \"${STAGINGDIR}/${VFSFORGITDESTINATION}/.\""
    eval $copyPublishDirectory || exit 1
    
    removeTestAssemblies="find \"${STAGINGDIR}/${VFSFORGITDESTINATION}\" -name \"*GVFS.*Tests*\" -exec rm -f \"{}\" \";\""
    eval $removeTestAssemblies || exit 1
    
    removeDataDirectory="rm -Rf \"${STAGINGDIR}/${VFSFORGITDESTINATION}/Data\""
    eval $removeDataDirectory || exit 1
    
    copyPrjFS="cp -Rf \"${VFS_OUTPUTDIR}/ProjFS.Mac/Native/$CONFIGURATION\"/*.dylib \"${STAGINGDIR}/${VFSFORGITDESTINATION}/.\""
    eval $copyPrjFS || exit 1
    
    copyPrjFS="cp -Rf \"${VFS_OUTPUTDIR}/ProjFS.Mac/Native/$CONFIGURATION\"/prjfs-log \"${STAGINGDIR}/${VFSFORGITDESTINATION}/.\""
    eval $copyPrjFS || exit 1
    
    copyUnInstaller="cp -f \"${UNINSTALLERPATH}\" \"${STAGINGDIR}/${VFSFORGITDESTINATION}/.\""
    eval $copyUnInstaller || exit 1
    
    copyPrjFS="cp -Rf \"${VFS_OUTPUTDIR}/ProjFS.Mac/Native/$CONFIGURATION\"/PrjFSKext.kext \"${STAGINGDIR}/${LIBRARYEXTENSIONSDESTINATION}/.\""
    eval $copyPrjFS || exit 1
    
    currentDirectory=`pwd`
    cd "${STAGINGDIR}/usr/local/bin"
    linkCommand="ln -sf ../vfsforgit/gvfs gvfs"
    eval $linkCommand
    cd $currentDirectory
}

function CreateInstaller()
{
    pkgBuildCommand="/usr/bin/pkgbuild --identifier $INSTALLERPACKAGEID --root \"${STAGINGDIR}\" \"${BUILDOUTPUTDIR}\"$INSTALLERPACKAGENAME.pkg"
    eval $pkgBuildCommand || exit 1
}

function CreateMetaInstaller()
{
    GITVERSION="$($VFS_SCRIPTDIR/GetGitVersionNumber.sh)"
    GITDMGPATH="$(find $VFS_PACKAGESDIR/gitformac.gvfs.installer/$GITVERSION -type f -name *.dmg)" || exit 1
    GITDMGNAME="${GITDMGPATH##*/}"
    GITINSTALLERNAME="${GITDMGNAME%.dmg}"
    GITVERSIONSTRING=`echo $GITINSTALLERNAME | cut -d"-" -f2`
    
    if [[ -z "$GITVERSION" || -z "$GITVERSIONSTRING" ]]; then
        echo "Error creating metapackage: could not determine Git package version."
        exit 1
    fi
    
    if [ ! -f "$GITDMGPATH" ]; then
        echo "Error creating metapackage: could not find Git disk image."
        exit 1
    fi
    
    mountCmd="/usr/bin/hdiutil attach \"$GITDMGPATH\""
    echo "$mountCmd"
    eval $mountCmd || exit 1
    
    MOUNTEDVOLUME=`/usr/bin/find /Volumes -maxdepth 1 -type d -name "Git $GITVERSIONSTRING*"`
    GITINSTALLERPATH=`/usr/bin/find "$MOUNTEDVOLUME" -type f -name "git-$GITVERSIONSTRING*.pkg"`
    
    if [ ! -f "$GITINSTALLERPATH" ]; then
        echo "Error creating metapackage: could not find Git installer package."
        exit 1
    fi
    
    METAPACKAGENAME="$INSTALLERPACKAGENAME-Git.$GITVERSION.pkg"
    
    buildMetapkgCmd="/usr/bin/productbuild --package \"$GITINSTALLERPATH\" --package \"${BUILDOUTPUTDIR}\"$INSTALLERPACKAGENAME.pkg \"${BUILDOUTPUTDIR}\"$METAPACKAGENAME"
    echo $buildMetapkgCmd
    eval $buildMetapkgCmd || exit 1
    
    unmountCmd="/usr/bin/hdiutil detach \"$MOUNTEDVOLUME\""
    echo "$unmountCmd"
    eval $unmountCmd || exit 1
}

function Run()
{
    CheckBuildIsAvailable
    CreateInstallerRoot
    CopyBinariesToInstall
    SetPermissions
    CreateInstaller
    CreateMetaInstaller
}

Run
