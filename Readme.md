# GVFS

## What is GVFS?

GVFS stands for Git Virtual File System. GVFS virtualizes the file system beneath your git repo so that git and all tools
see what appears to be a normal repo, but GVFS only downloads objects as they are needed. GVFS also manages the files that git will consider,
to ensure that git operations like status, checkout, etc., can be as quick as possible because they will only consider the files
that the user has accessed, not all files in the repo.

## Installing GVFS

* GVFS requires Windows 10 Anniversary Update (Windows 10 version 1607) or later
* Install the latest GVFS and Git for Windows from https://github.com/Microsoft/GVFS/releases

## Building GVFS

If you'd like to build your own GVFS installer:
* Install Visual Studio 2017 Community Edition or higher (https://www.visualstudio.com/downloads/). 
  * Include the following workloads:
    * .NET desktop development
    * Desktop development with C++
    * .NET Core cross-platform development
  * Include the following additional components:
    * .NET Core runtime
    * .NET Framework 3.5 development tools
    * C++/CLI support
    * VC++ 2015.3 v140 toolset
    * Windows 10 SDK (10.0.10240.0)
* Create a folder to clone into, e.g. `C:\Repos\GVFS`
* Clone this repo into the `src` subfolder, e.g. `C:\Repos\GVFS\src`
* Run `\src\Scripts\BuildGVFSForWindows.bat`
* You can also build in Visual Studio by opening `src\GVFS.sln` (do not upgrade any projects) and building. However, the very first 
build will fail, and the second and subsequent builds will succeed. This is because the build requires a prebuild code generation step.
For details, see the build script in the previous step.

The installer can now be found at `C:\Repos\GVFS\BuildOutput\GVFS.Installer\bin\x64\[Debug|Release]\SetupGVFS.<version>.exe`

## Trying out GVFS

* GVFS will work with any git service that supports the GVFS [protocol](Protocol.md). For now, that means you'll need to create a repo in 
Visual Studio Team Services (https://www.visualstudio.com/team-services/), and push some contents to it. There are two constraints:
  * Your repo must not enable any clean/smudge filters
  * Your repo must have a `.gitattributes` file in the root that includes the line `* -text`
* `gvfs clone <URL of repo you just created>`
* `cd <root>\src`
* Run git commands as you normally would
* `gvfs unmount` when done

# Licenses

The GVFS source code in this repo is available under the MIT license. See [License.md](License.md).

GVFS relies on the PrjFlt filter driver, formerly known as the GvFlt filter driver, available as a prerelease NuGet package.
