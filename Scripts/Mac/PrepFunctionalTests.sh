#!/bin/bash

. "$(dirname ${BASH_SOURCE[0]})/InitializeEnvironment.sh"

# Ensure the kext isn't loaded before installing Git
$VFS_SRCDIR/ProjFS.Mac/Scripts/UnloadPrjFSKext.sh

# Install GVFS-aware Git (that was published by the build script)
GITPUBLISH=$VFS_OUTPUTDIR/Git
if [[ ! -d $GITPUBLISH ]]; then
    echo "GVFS-aware Git package not found. Run BuildGVFSForMac.sh and try again"
    exit 1
fi
hdiutil attach $GITPUBLISH/*.dmg || exit 1
GITPKG="$(find /Volumes/Git* -type f -name *.pkg)" || exit 1
sudo installer -pkg "$GITPKG" -target / || exit 1
hdiutil detach /Volumes/Git*

# Install Homebrew if not installed
which -s brew
if [[ $? != 0 ]] ; then
    ruby -e "$(curl -fsSL https://raw.githubusercontent.com/Homebrew/install/master/install)"
fi

# Install Java if not installed
which -s java
if [[ $? != 0 ]] ; then
    brew cask install java || exit 1
fi
brew cask info java

# Install Git Credential Manager if not installed
which -s git-credential-manager
if [[ $? != 0 ]] ; then
    brew install git-credential-manager || exit 1
fi

git-credential-manager install

$VFS_SCRIPTDIR/InstallSharedDataQueueStallWorkaround.sh || exit 1

# If we're running on an agent where the PAT environment variable is set and a URL is passed into the script, add it to the keychain.
PATURL=$1
PAT=$2
if [[ ! -z $PAT && ! -z $PATURL ]] ; then
    security delete-generic-password -s "gcm4ml:git:$PATURL"
    security add-generic-password -a "Personal Access Token" -s "gcm4ml:git:$PATURL" -D Credential -w $PAT || exit 1
fi
