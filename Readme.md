# VFS for Git

## Windows

|Branch|Unit Tests|Functional Tests|Large Repo Perf|Large Repo Build|
|:--:|:--:|:--:|:--:|:--:|
|**master**|[![Build status](https://dev.azure.com/gvfs/ci/_apis/build/status/CI%20-%20Windows?branchName=master)](https://dev.azure.com/gvfs/ci/_build/latest?definitionId=7&branchName=master)|[![Build status](https://dev.azure.com/gvfs/ci/_apis/build/status/CI%20-%20Windows%20-%20Full%20Functional%20Tests?branchName=master)](https://dev.azure.com/gvfs/ci/_build/latest?definitionId=6&branchName=master)|[![Build status](https://dev.azure.com/mseng/AzureDevOps/_apis/build/status/GVFS/GitHub%20VFSForGit%20Large%20Repo%20Perf%20Tests?branchName=master)](https://dev.azure.com/mseng/AzureDevOps/_build/latest?definitionId=7179&branchName=master)|[![Build status](https://dev.azure.com/mseng/AzureDevOps/_apis/build/status/GVFS/GitHub%20VFSForGit%20Large%20Repo%20Build?branchName=master)](https://dev.azure.com/mseng/AzureDevOps/_build/latest?definitionId=7180&branchName=master)|
|**shipped**|[![Build status](https://dev.azure.com/gvfs/ci/_apis/build/status/CI%20-%20Windows?branchName=releases%2Fshipped)](https://dev.azure.com/gvfs/ci/_build/latest?definitionId=7&branchName=releases%2Fshipped)|[![Build status](https://dev.azure.com/gvfs/ci/_apis/build/status/CI%20-%20Windows%20-%20Full%20Functional%20Tests?branchName=releases%2Fshipped)](https://dev.azure.com/gvfs/ci/_build/latest?definitionId=6&branchName=releases%2Fshipped)|[![Build status](https://dev.azure.com/mseng/AzureDevOps/_apis/build/status/GVFS/GitHub%20VFSForGit%20Large%20Repo%20Perf%20Tests?branchName=releases%2Fshipped)](https://dev.azure.com/mseng/AzureDevOps/_build/latest?definitionId=7179&branchName=releases%2Fshipped)|[![Build status](https://dev.azure.com/mseng/AzureDevOps/_apis/build/status/GVFS/GitHub%20VFSForGit%20Large%20Repo%20Build?branchName=releases%2Fshipped)](https://dev.azure.com/mseng/AzureDevOps/_build/latest?definitionId=7180&branchName=releases%2Fshipped)|

## Mac
|Branch|Unit Tests|Functional Tests|
|:--:|:--:|:--:|
|**master**|[![Build status](https://dev.azure.com/gvfs/ci/_apis/build/status/CI%20-%20Mac?branchName=master)](https://dev.azure.com/gvfs/ci/_build/latest?definitionId=15&branchName=master)|[![Build status](https://dev.azure.com/mseng/AzureDevOps/_apis/build/status/GVFS/CI%20-%20Mac%20-%20Functional%20Tests?branchName=master)](https://dev.azure.com/mseng/AzureDevOps/_build/latest?definitionId=7376&branchName=master)|
|**shipped**|[![Build status](https://dev.azure.com/gvfs/ci/_apis/build/status/CI%20-%20Mac?branchName=releases%2Fshipped)](https://dev.azure.com/gvfs/ci/_build/latest?definitionId=15&branchName=releases%2Fshipped)|[![Build status](https://dev.azure.com/mseng/AzureDevOps/_apis/build/status/GVFS/CI%20-%20Mac%20-%20Functional%20Tests?branchName=releases%2Fshipped)](https://dev.azure.com/mseng/AzureDevOps/_build/latest?definitionId=7376&branchName=releases%2Fshipped)|

## What is VFS for Git?

VFS stands for Virtual File System. VFS for Git virtualizes the file system beneath your git repo so that git and all tools
see what appears to be a normal repo, but VFS for Git only downloads objects as they are needed. VFS for Git also manages the files that git will consider,
to ensure that git operations like status, checkout, etc., can be as quick as possible because they will only consider the files
that the user has accessed, not all files in the repo.

## New name

This project was formerly known as GVFS (Git Virtual File System). It is undergoing a rename to VFS for Git. While the rename is in progress, the code, protocol,
built executables, and releases may still refer to the old GVFS name. See https://github.com/Microsoft/VFSForGit/projects/4 for the latest status of the rename effort.

## Installing VFS for Git

* VFS for Git requires Windows 10 Anniversary Update (Windows 10 version 1607) or later
* Run the latest GVFS and Git for Windows installers from https://github.com/Microsoft/VFSForGit/releases

## Building VFS for Git on Windows

If you'd like to build your own VFS for Git Windows installer:
* Install Visual Studio 2017 Community Edition or higher (https://www.visualstudio.com/downloads/).
  * Include the following workloads:
    * .NET desktop development
    * Desktop development with C++
    * .NET Core cross-platform development
  * Include the following additional components:
    * .NET Core runtime
    * C++/CLI support
    * Windows 10 SDK (10.0.10240.0)
* Install the .NET Core 2.1 SDK (https://www.microsoft.com/net/download/dotnet-core/2.1)
* Create a folder to clone into, e.g. `C:\Repos\VFSForGit`
* Clone this repo into the `src` subfolder, e.g. `C:\Repos\VFSForGit\src`
* Run `\src\Scripts\BuildGVFSForWindows.bat`
* You can also build in Visual Studio by opening `src\GVFS.sln` (do not upgrade any projects) and building. However, the very first
build will fail, and the second and subsequent builds will succeed. This is because the build requires a prebuild code generation step.
For details, see the build script in the previous step.

The installer can now be found at `C:\Repos\VFSForGit\BuildOutput\GVFS.Installer\bin\x64\[Debug|Release]\SetupGVFS.<version>.exe`

## Building VFS for Git on Mac

VFS for macOS is still in progress. You can build it, but this will not create a macOS VFS installer the same way the current Windows build will.

* Ensure you have Xcode installed and have accepted the terms of use and launched Xcode at least once.
* Install [Visual Studio for Mac ](https://visualstudio.microsoft.com/vs/mac). (This will also install the `dotnet` CLI).
* If you still do not have the `dotnet` cli `>= v2.1.300` installed [manually install it](https://www.microsoft.com/net/download/dotnet-core/2.1)
* [Install Homebrew](https://brew.sh/)
* Install Java with
  ```
  brew cask install java
  ```

* Install the Git credential manager for Mac [by following their Homebrew instructions](https://github.com/Microsoft/Git-Credential-Manager-for-Mac-and-Linux/blob/master/Install.md#installing-on-mac-using-homebrew-or-on-linux-using-linuxbrew-recommended)

* If you are not currently an Apple Developer you will need to become one so that you can either use Microsoft certs or create your own certs for signing purposes. (If you are a Microsoft employee you must use your alias@microsoft.com when creating your Apple account.)

* Create a `VFS` directory and Clone the VFS repo into a directory called `src` inside it:
  ```
  mkdir VFS && cd VFS && git clone https://github.com/Microsoft/VFSForGit.git src && cd src
  ```

* Disable "System Integrity Protection" (for loading unsigned kexts) by booting into recovery mode (`[Win/⌘] + R` while booting).
  Once booted into recovery mode open Utilities -> Terminal to launch a termnial. Enter:
  ```
  csrutil disable
  ```
  to disable SIP.

* Now `cd` back into your VFS src directory and run

  ```
  Scripts/Mac/BuildGVFSForMac.sh [Release]
  ```

  **Troubleshooting**

  If you get
  ```
  xcodebuild: error: SDK "macosx10.13" cannot be located.
  ```
  You may have the "XCode Command Line Tools" installed (helpfully by Mac OS) instead of full XCode.
  Make sure
  ```
  xcode-select -p
  ```

  shows `/Applications/Xcode.app/Contents/Developer`. If it does not, install Xcode and then launch it (you can close it afterwards.)

* Now setup the git credential manager by running

  ```
  Scripts/Mac/PrepFunctionalTests.sh
  ```

* Now you have to load the ProjFS Kext.

  ```
  ProjFS.Mac/Scripts/LoadPrjFSKext.sh [Release]
  ```

* Now you can put your built gvfs program on your path. A simple way to do that is by adding `Path/to/VFS/Publish` to your path.

  Check you have it by running

  ```
  command -v gvfs
  ```

  You should see a path to the gvfs executable.

* Try cloning! Now that you have `gvfs` ready you can try cloning a VFS for Git enabled repository!

  ```
  gvfs clone URL_TO_REPOSITORY [--cache-url] --local-cache-path ~/.gvfsCache
  ```

  Note you may have a cache server URL to use. Note the current use of `--local-cache-path`. That argument prevent gvfs from running into a permissions error trying to put the cache at the root of your Mac's hard-drive.


## Trying out VFS for Git

* VFS for Git will work with any git service that supports the GVFS [protocol](Protocol.md). For example, you can create a repo in
Visual Studio Team Services (https://www.visualstudio.com/team-services/), and push some contents to it. There are two constraints:
  * Your repo must not enable any clean/smudge filters
  * Your repo must have a `.gitattributes` file in the root that includes the line `* -text`
* `gvfs clone <URL of repo you just created>`
* `cd <root>\src`
* Run git commands as you normally would
* `gvfs unmount` when done


# Licenses

The VFS for Git source code in this repo is available under the MIT license. See [License.md](License.md).

VFS for Git relies on the PrjFlt filter driver, formerly known as the GvFlt filter driver, available as a prerelease NuGet package.
