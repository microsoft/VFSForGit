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

BUILDOUTPUTDIR=${4%/}
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

STAGINGDIR="${BUILDOUTPUTDIR}/Staging"
PACKAGESTAGINGDIR="${BUILDOUTPUTDIR}/Packages"
VFSFORGITDESTINATION="usr/local/vfsforgit"
DAEMONPLISTDESTINATION="Library/LaunchDaemons"
AGENTPLISTDESTINATION="Library/LaunchAgents"
LIBRARYEXTENSIONSDESTINATION="Library/Extensions"
LIBRARYAPPSUPPORTDESTINATION="Library/Application Support/VFS For Git"
INSTALLERPACKAGENAME="VFSForGit.$PACKAGEVERSION"
INSTALLERPACKAGEID="com.vfsforgit.pkg"
UNINSTALLERPATH="${SOURCEDIRECTORY}/uninstall_vfsforgit.sh"
SCRIPTSPATH="${SOURCEDIRECTORY}/scripts"
COMPONENTSPLISTPATH="${SOURCEDIRECTORY}/vfsforgit_components.plist"
DIST_FILE_NAME="Distribution.updated.xml"

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
    
    mkdirPkgStaging="mkdir -p \"${PACKAGESTAGINGDIR}\""
    eval $mkdirPkgStaging || exit 1

    mkdirBin="mkdir -p \"${STAGINGDIR}/usr/local/bin\""
    eval $mkdirBin || exit 1
    
    mkdirBin="mkdir -p \"${STAGINGDIR}/$LIBRARYEXTENSIONSDESTINATION\""
    eval $mkdirBin || exit 1
    
    mkdirBin="mkdir -p \"${STAGINGDIR}/$LIBRARYAPPSUPPORTDESTINATION\""
    eval $mkdirBin || exit 1
    
    mkdirBin="mkdir -p \"${STAGINGDIR}/$DAEMONPLISTDESTINATION\""
    eval $mkdirBin || exit 1
    
    mkdirBin="mkdir -p \"${STAGINGDIR}/$AGENTPLISTDESTINATION\""
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
    
    copyPrjFS="cp -Rf \"${VFS_OUTPUTDIR}/ProjFS.Mac/Native/$CONFIGURATION\"/PrjFSKextLogDaemon \"${STAGINGDIR}/${VFSFORGITDESTINATION}/.\""
    eval $copyPrjFS || exit 1
    
    copyUnInstaller="cp -f \"${UNINSTALLERPATH}\" \"${STAGINGDIR}/${VFSFORGITDESTINATION}/.\""
    eval $copyUnInstaller || exit 1
    
    copyPrjFS="cp -Rf \"${VFS_OUTPUTDIR}/ProjFS.Mac/Native/$CONFIGURATION\"/PrjFSKext.kext \"${STAGINGDIR}/${LIBRARYEXTENSIONSDESTINATION}/.\""
    eval $copyPrjFS || exit 1
    
    copyPrjFS="cp -Rf \"${VFS_OUTPUTDIR}/ProjFS.Mac/Native/$CONFIGURATION/org.vfsforgit.prjfs.PrjFSKextLogDaemon.plist\" \"${STAGINGDIR}/${DAEMONPLISTDESTINATION}/.\""
    eval $copyPrjFS || exit 1
    
    copyNotificationApp="cp -Rf \"${VFS_OUTPUTDIR}/GVFS.Notifications/VFSForGit.Mac/Build/Products/$CONFIGURATION/VFS For Git.app\" \"${STAGINGDIR}/${LIBRARYAPPSUPPORTDESTINATION}/.\""
    eval $copyNotificationApp || exit 1
    
    copyNotificationPlist="cp -Rf \"${SOURCEDIRECTORY}/../GVFS.Notifications/VFSForGit.Mac/org.vfsforgit.usernotification.plist\" \"${STAGINGDIR}/${AGENTPLISTDESTINATION}/.\""
    eval $copyNotificationPlist || exit 1
    
    copyServicePlist="cp -Rf \"${SOURCEDIRECTORY}/../GVFS.Service/Mac/org.vfsforgit.service.plist\" \"${STAGINGDIR}/${AGENTPLISTDESTINATION}/.\""
    eval $copyServicePlist || exit 1
    
    currentDirectory=`pwd`
    cd "${STAGINGDIR}/usr/local/bin"
    linkCommand="ln -sf ../vfsforgit/gvfs gvfs"
    eval $linkCommand
    cd $currentDirectory
}

function CreateVFSForGitInstaller()
{
    pkgBuildCommand="/usr/bin/pkgbuild --identifier $INSTALLERPACKAGEID --component-plist \"${COMPONENTSPLISTPATH}\" --scripts \"${SCRIPTSPATH}\" --root \"${STAGINGDIR}\" \"${PACKAGESTAGINGDIR}/$INSTALLERPACKAGENAME.pkg\""
    eval $pkgBuildCommand || exit 1
}

function UpdateDistributionFile()
{
    VFSFORGIT_PKG_VERSION=$PACKAGEVERSION
    VFSFORGIT_PKG_NAME="$INSTALLERPACKAGENAME.pkg"
    GIT_PKG_NAME=$1
    GIT_PKG_VERSION=$2
        
    /usr/bin/sed -e "s|VFSFORGIT_VERSION_PLACHOLDER|$VFSFORGIT_PKG_VERSION|g" "$SCRIPTSPATH/Distribution.xml" > "${BUILDOUTPUTDIR}/$DIST_FILE_NAME"
    /usr/bin/sed -i.bak "s|VFSFORGIT_PKG_NAME_PLACEHOLDER|$VFSFORGIT_PKG_NAME|g" "${BUILDOUTPUTDIR}/$DIST_FILE_NAME"
    
    if [ ! -z "$GIT_PKG_NAME" ] && [ ! -z "$GIT_PKG_VERSION" ]; then
        GIT_CHOICE_OUTLINE_ELEMENT_TEXT="<line choice=\"com.git.pkg\"/>"
        GIT_CHOICE_ID_ELEMENT_TEXT="<choice id=\"com.git.pkg\" visible=\"false\"> <pkg-ref id=\"com.git.pkg\"/> </choice>"
        GIT_PKG_REF_ELEMENT_TEXT="<pkg-ref id=\"com.git.pkg\" version=\"$GIT_PKG_VERSION\" onConclusion=\"none\">$GIT_PKG_NAME</pkg-ref>"
    else
        GIT_CHOICE_OUTLINE_ELEMENT_TEXT=""
        GIT_CHOICE_ID_ELEMENT_TEXT=""
        GIT_PKG_REF_ELEMENT_TEXT=""
    fi
    
    /usr/bin/sed -i.bak "s|GIT_CHOICE_OUTLINE_PLACEHOLDER|$GIT_CHOICE_OUTLINE_ELEMENT_TEXT|g" "${BUILDOUTPUTDIR}/$DIST_FILE_NAME"
    /usr/bin/sed -i.bak "s|GIT_CHOICE_ID_PLACEHOLDER|$GIT_CHOICE_ID_ELEMENT_TEXT|g" "${BUILDOUTPUTDIR}/$DIST_FILE_NAME"
    /usr/bin/sed -i.bak "s|GIT_PKG_REF_PLACEHOLDER|$GIT_PKG_REF_ELEMENT_TEXT|g" "${BUILDOUTPUTDIR}/$DIST_FILE_NAME"
    
    /bin/rm -f "${BUILDOUTPUTDIR}/$DIST_FILE_NAME.bak"
}

function CreateVFSForGitDistribution()
{
    # Update distribution file(removes Git info from template.)
    UpdateDistributionFile "" ""
    
    buildVFSForGitDistCmd="/usr/bin/productbuild --distribution \"${BUILDOUTPUTDIR}/Distribution.updated.xml\" --package-path \"$PACKAGESTAGINGDIR\" \"${BUILDOUTPUTDIR}/$INSTALLERPACKAGENAME.pkg\""
    echo $buildVFSForGitDistCmd
    eval $buildVFSForGitDistCmd || exit 1
    
    /bin/rm -f "${BUILDOUTPUTDIR}/$DIST_FILE_NAME"
}

function CreateMetaDistribution()
{
    GITVERSION="$($VFS_SCRIPTDIR/GetGitVersionNumber.sh)"
    GITDMGPATH="$(find $VFS_PACKAGESDIR/gitformac.gvfs.installer/$GITVERSION -type f -name *.dmg)" || exit 1
    GITDMGNAME="${GITDMGPATH##*/}"
    GITINSTALLERNAME="${GITDMGNAME%.dmg}"
    GITVERSIONSTRING=`echo $GITINSTALLERNAME | cut -d"-" -f2`
    GITPKGNAME="$GITINSTALLERNAME.pkg"
    
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
    
    copyGitPkgCmd="/bin/cp -Rf \"${GITINSTALLERPATH}\" \"${PACKAGESTAGINGDIR}/.\""
    eval $copyGitPkgCmd

    UpdateDistributionFile "$GITPKGNAME" "$GITVERSIONSTRING"

    copyGitPkgInstallerCmd="/bin/cp \"${GITINSTALLERPATH}\" \"${BUILDOUTPUTDIR}/\""
    echo $copyGitPkgInstallerCmd
    eval $copyGitPkgInstallerCmd || exit 1
    
    METAPACKAGENAME="$INSTALLERPACKAGENAME-Git.$GITVERSION.pkg"
    buildMetapkgCmd="/usr/bin/productbuild --distribution \"${BUILDOUTPUTDIR}/Distribution.updated.xml\" --package-path \"$PACKAGESTAGINGDIR\" \"${BUILDOUTPUTDIR}/$METAPACKAGENAME\""
    echo $buildMetapkgCmd
    eval $buildMetapkgCmd || exit 1
    
    /bin/rm -f "${BUILDOUTPUTDIR}/$DIST_FILE_NAME"
    
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
    CreateVFSForGitInstaller
    CreateVFSForGitDistribution
    CreateMetaDistribution
}

Run
