# GVFS

## What is GVFS?

GVFS stands for Git Virtual File System. GVFS virtualizes the file system beneath your git repo so that git and all tools
see what appears to be a normal repo, but GVFS only downloads objects as they are needed. GVFS also manages git's sparse-checkout
to ensure that git operations like status, checkout, etc., can be as quick as possible because they will only consider the files
that the user has accessed, not all files in the repo.

## Installing GVFS

* GVFS requires Windows 10 Creators Update (Windows 10 version 1703) or later
* Install the latest GVFS-enabled Git for Windows from https://github.com/Microsoft/git/releases
  * This build behaves the same as Git for Windows except if the config value `core.gvfs` is set to `true`.
* Install the latest GVFS from https://github.com/Microsoft/GVFS/releases

## Building GVFS

If you'd like to build your own GVFS installer:
* Install Visual Studio 2017 Community Edition or higher (https://www.visualstudio.com/downloads/). Include the ".Net desktop development" and 
"Desktop development with C++" workloads, as well as the following additional components:
  * .Net Framework 3.5 development tools
  * C++/CLI support
  * VC++ 2015.3 v140 toolset
  * Windows 10 SDK (10.0.10240.0)
* Create a folder to clone into, e.g. `C:\Repos\GVFS`
* Clone this repo into the `src` subfolder, e.g. `C:\Repos\GVFS\src`
* Open `src\GVFS.sln` in Visual Studio. Do not upgrade any projects.
* Build `GVFS.sln`

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

GVFS relies on the PrjFlt filter driver, formerly known as the GvFlt filter driver, available as a prerelease NuGet package with its own [license](GvFlt_EULA.md).
