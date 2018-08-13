#!/bin/bash

SCRIPTDIR=$(dirname ${BASH_SOURCE[0]})

# Install GVFS-aware Git (that was downloaded by the build script)
GVFSPROPS=$SCRIPTDIR/../../GVFS/GVFS.Build/GVFS.props
GITVERSION="$(cat $GVFSPROPS | grep GitPackageVersion | grep -Eo '[0-9.]{1,}')"
ROOTDIR=$SCRIPTDIR/../../..
GITDIR=$ROOTDIR/packages/gitformac.gvfs.installer/$GITVERSION/tools
if [[ ! -d $GITDIR ]]; then
    echo "GVFS-aware Git package not found. Run BuildGVFSForMac.sh and try again"
    exit 1
fi
hdiutil attach $GITDIR/*.dmg || exit 1
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

# If our Java version is 9+ (the formatting of 'java -version' changed in Java 9), work around
# https://github.com/Microsoft/Git-Credential-Manager-for-Mac-and-Linux/issues/71
JAVAVERSION="$(java -version 2>&1 | egrep -o '"[[:digit:]]+.[[:digit:]]+.[[:digit:]]+"' | xargs)"
if [[ ! -z $JAVAVERSION ]]; then
    git config --global credential.helper "!/usr/bin/java -Ddebug=false --add-modules java.xml.bind -Djava.net.useSystemProxies=true -jar /usr/local/Cellar/git-credential-manager/2.0.3/libexec/git-credential-manager-2.0.3.jar" || exit 1
fi

# If we're running on an agent where the PAT environment variable is set and a URL is passed into the script, add it to the keychain.
PAT=$SYSTEM_ACCESSTOKEN
PATURL=$1
if [[ ! -z $PAT && ! -z $PATURL ]] ; then
    security add-generic-password -a "Personal Access Token" -s "gcm4ml:git:$PATURL" -D Credential -w $PAT || exit 1
fi
