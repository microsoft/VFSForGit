#!/bin/bash

# Install Homebrew if not installed
which -s brew
if [[ $? != 0 ]] ; then
    ruby -e "$(curl -fsSL https://raw.githubusercontent.com/Homebrew/install/master/install)"
fi

# Install Java if not installed
which -s java
if [[ $? != 0 ]] ; then
    brew cask install java
fi
brew cask info java

# Install Git Credential Manager if not installed
which -s git-credential-manager
if [[ $? != 0 ]] ; then
    brew install git-credential-manager
fi

git-credential-manager install

# Determine what Java version we have (and assume it's 10.0.1 if Java crashes like it does on build agents)
JAVAVERSION=`java --version | egrep -o '[[:digit:]]{1,3}.[[:digit:]].[[:digit:]] ' | xargs`
if [[ -z $JAVAVERSION ]]; then
    JAVAVERSION="10.0.1"
fi

# Work around https://github.com/Microsoft/Git-Credential-Manager-for-Mac-and-Linux/issues/71
git config --global credential.helper "!/Library/Java/JavaVirtualMachines/jdk-$JAVAVERSION.jdk/Contents/Home/bin/java -Ddebug=false --add-modules java.xml.bind -Djava.net.useSystemProxies=true -jar /usr/local/Cellar/git-credential-manager/2.0.3/libexec/git-credential-manager-2.0.3.jar"

# If we're running on an agent where the PAT environment variable is set and a URL is passed into the script, add it to the keychain.
PAT=$SYSTEM_ACCESSTOKEN
PATURL=$1
if [[ ! -z $PAT && ! -z $PATURL ]] ; then
    security add-generic-password -a "Personal Access Token" -s "gcm4ml:git:$PATURL" -D Credential -w $PAT
fi
