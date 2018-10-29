#!/bin/bash

CONFIGURATION=$1
if [ -z $CONFIGURATION ]; then
  echo "Build configuration not specified"
  exit 1
fi

PACKAGEVERSION=$2
if [ -z $PACKAGEVERSION ]; then
  echo "Installer package version not specified"
  exit 1
fi

BUILDOUTPUTDIR=$3
if [ -z $BUILDOUTPUTDIR ]; then
  echo "Build output directory not specified"
  exit 1
fi

STAGINGDIR=$BUILDOUTPUTDIR"Staging"
VFSFORGITDESTINATION="usr/local/vfsforgit"
PRJFSDESTINATION="usr/local/vfsforgit/prjfs"
INSTALLERPACKAGENAME="VFSForGit.$PACKAGEVERSION"
INSTALLERPACKAGEID="com.vfsforgit.pkg"

function CheckBuildIsAvailable()
{
    if [ ! -d $VFS_OUTPUTDIR ] || [ ! -d $VFS_PUBLISHDIR ]; then
        echo "Could not find VFSForGit Build to package."
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

    mkdirBin="mkdir -p \"${STAGINGDIR}/$PRJFSDESTINATION\""
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
    
    copyPrjFS="cp -Rf \"${VFS_OUTPUTDIR}/ProjFS.Mac/Native/Build/Products/$CONFIGURATION\"/*.dylib \"${STAGINGDIR}/${PRJFSDESTINATION}/.\""
    eval $copyPrjFS || exit 1
    
    copyPrjFS="cp -Rf \"${VFS_OUTPUTDIR}/ProjFS.Mac/Native/Build/Products/$CONFIGURATION\"/prjfs-log \"${STAGINGDIR}/${PRJFSDESTINATION}/.\""
    eval $copyPrjFS || exit 1
    
    copyPrjFS="cp -Rf \"${VFS_OUTPUTDIR}/ProjFS.Mac/Native/Build/Products/$CONFIGURATION\"/PrjFSKext.kext \"${STAGINGDIR}/${PRJFSDESTINATION}/.\""
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

function Run()
{
    CheckBuildIsAvailable
    CreateInstallerRoot
    CopyBinariesToInstall
    SetPermissions
    CreateInstaller
}

Run
