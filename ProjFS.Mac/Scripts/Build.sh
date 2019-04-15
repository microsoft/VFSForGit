#!/bin/bash

CONFIGURATION=$1
if [ -z $CONFIGURATION ]; then
  CONFIGURATION=Debug
fi

SCRIPTDIR=$(dirname ${BASH_SOURCE[0]})
SRCDIR=$SCRIPTDIR/../..
ROOTDIR=$SRCDIR/..
PACKAGES=$ROOTDIR/packages
COVERAGEDIR=$ROOTDIR/BuildOutput/ProjFS.Mac/Coverage

PROJFS=$SRCDIR/ProjFS.Mac

xcodebuild -configuration $CONFIGURATION -project $PROJFS/PrjFS.xcodeproj  -scheme 'Build All' -derivedDataPath $ROOTDIR/BuildOutput/ProjFS.Mac/Native build || exit 1

if !(gem list --local | grep xcpretty); then
  echo "Attempting to run 'sudo gem install xcpretty'.  This may ask you for your password to gain admin privileges"
  sudo gem install xcpretty
fi

# Run Tests and put output into a xml file
set -o pipefail
xcodebuild -configuration $CONFIGURATION -enableCodeCoverage YES -project $PROJFS/PrjFS.xcodeproj -derivedDataPath $COVERAGEDIR -scheme 'Build All' test | xcpretty -r junit --output $PROJFS/TestResultJunit.xml || exit 1
set +o pipefail

while read -rd $'\0' file; do
  COVERAGE_FILE="$file"
done < <(find $COVERAGEDIR -name "*xccovreport" -print0)

if [[ $COVERAGE_FILE == "" ]]; then
  echo "Error: No coverage file found"
  exit 1
fi

# xcperfect will display pretty output for code coverage.  The terminal output isn't supported by our build system yet, but a bug has been filed.  Once support is added we'll switch to the prettier format.
#if !(gem list --local | grep xcperfect); then
#  echo "Attempting to run 'sudo gem install xcperfect'.  This may ask you for your password to gain admin privileges"
#  sudo gem install xcperfect
#fi
#xcrun xccov view "$COVERAGE_FILE" --json | TERM=xterm-256color xcperfect

printf "\n\nCode Coverage Report\n"
xcrun xccov view "$COVERAGE_FILE" | tee $PROJFS/CoverageResult.txt 

# Fail on any line that doesn't show %100 coverage and isn't on the exclusion list or hpp/cpp
while read line; do
  if [[ $line != *"100.00%"* ]] && 
     [[ $line == *"%"* ]] && 
	 [[ $line != *"AllArrayElementsInitialized"* ]] &&              #Function is used for compile time checks only
	 [[ $line != *"KauthHandler_Init"* ]] && 
	 [[ $line != *"KauthHandler_Cleanup"* ]] && 
	 [[ $line != *"HandleVnodeOperation"* ]] &&                      #SHOULD ADD COVERAGE
	 [[ $line != *"HandleFileOpOperation"* ]] &&                     #SHOULD ADD COVERAGE
	 [[ $line != *"TryGetVirtualizationRoot"* ]] &&                  #SHOULD ADD COVERAGE
	 [[ $line != *"WaitForListenerCompletion"* ]] && 
	 [[ $line != *"KextLog_"* ]] && 
	 [[ $line != *"Definition"* ]] && 
	 [[ $line != *"PerfTracer"* ]] && 
	 [[ $line != *"VirtualizationRoot_GetActiveProvider"* ]] &&      #SHOULD ADD COVERAGE
	 [[ $line != *"VirtualizationRoots_Init"* ]] && 
	 [[ $line != *"VirtualizationRoots_Cleanup"* ]] && 
	 [[ $line != *"VnodeCache_Init"* ]] && 
	 [[ $line != *"VnodeCache_Cleanup"* ]] && 
	 [[ $line != *"VnodeCache_ExportHealthData"* ]] &&               # IOKit related functions are not unit tested
	 [[ $line != *"FindOrDetectRootAtVnode"* ]] &&                   #SHOULD ADD COVERAGE
	 [[ $line != *"FindUnusedIndexOrGrow_Locked"* ]] &&              #SHOULD ADD COVERAGE
	 [[ $line != *"FindRootAtVnode_Locked"* ]] &&                    #SHOULD ADD COVERAGE
	 [[ $line != *"DirectoryContainsPath"* ]] &&                     #TODO: Why does this get reported as not having coverage when it does?
	 [[ $line != *"ActiveProvider_"* ]] && 
	 [[ $line != *"GetRelativePath"* ]] && 
	 [[ $line != *"VirtualizationRoot_GetRootRelativePath"* ]] && 
	 [[ $line != *"MockCalls"* ]] && 
	 [[ $line != *"VnodeCacheEntriesWrapper"* ]] &&
	 [[ $line != *"PerfTracing_"* ]] && 
	 [[ $line != *"proc_"* ]] && 
	 [[ $line != *"ParentPathString"* ]] && 
	 [[ $line != *"SetAndRegisterPath"* ]] && 
	 [[ $line != *"vn_"* ]] &&
	 [[ $line != *"vfs_"* ]] && 
	 [[ $line != *"vnode_lookup"* ]] && 
	 [[ $line != *"RetainIOCount"* ]] && 
	 [[ $line != *"ProviderMessaging_"* ]] && 
	 [[ $line != *"RWLock_DropExclusiveToShared"* ]] && 
	 [[ $line != *".xctest"* ]] && 
	 [[ $line != *".cpp"* ]] && 
	 [[ $line != *".a"* ]] && 
	 [[ $line != *".hpp"* ]]; then
       echo "Error: not at 100% Code Coverage $line"
       exit 1
  fi
done < $PROJFS/CoverageResult.txt 

# If we're building the Profiling(Release) configuration, remove Profiling() for building .NET code
if [ "$CONFIGURATION" == "Profiling(Release)" ]; then
  CONFIGURATION=Release
fi

dotnet restore $PROJFS/PrjFSLib.Mac.Managed/PrjFSLib.Mac.Managed.csproj /p:Configuration=$CONFIGURATION /p:Platform=x64 --packages $PACKAGES || exit 1
dotnet build $PROJFS/PrjFSLib.Mac.Managed/PrjFSLib.Mac.Managed.csproj /p:Configuration=$CONFIGURATION /p:Platform=x64 || exit 1
