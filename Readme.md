# RGFS

## What is RGFS?

RGFS stands for Git Virtual File System. RGFS virtualizes the file system beneath your git repo so that git and all tools
see what appears to be a normal repo, but RGFS only downloads objects as they are needed. RGFS also manages git's sparse-checkout
to ensure that git operations like status, checkout, etc., can be as quick as possible because they will only consider the files
that the user has accessed, not all files in the repo.

## Installing RGFS

* RGFS requires Windows 10 Creators Update (Windows 10 version 1703) or later
* Install the latest RGFS-enabled Git for Windows from https://github.com/Microsoft/git/releases
  * This build behaves the same as Git for Windows except if the config value `core.rgfs` is set to `true`.
* Install the latest RGFS from https://github.com/Microsoft/RGFS/releases

## Building RGFS

If you'd like to build your own RGFS installer:
* Install Visual Studio 2017 Community Edition or higher (https://www.visualstudio.com/downloads/). Include the ".Net desktop development" and 
"Desktop development with C++" workloads, as well as the following additional components:
  * .Net Framework 3.5 development tools
  * C++/CLI support
  * VC++ 2015.3 v140 toolset
  * Windows 10 SDK (10.0.10240.0)
* Install InnoSetup 5.5.9 or later (http://www.jrsoftware.org/isdl.php) to its default location (or you'll have to change the path in `RGFS.csproj` post-build step to match)
* Create a folder to clone into, e.g. `C:\Repos\RGFS`
* Clone this repo into the `src` subfolder, e.g. `C:\Repos\RGFS\src`
* Open `src\RGFS.sln` in Visual Studio. Do not upgrade any projects.
* Build `RGFS.sln`

The installer can now be found at `C:\Repos\RGFS\BuildOutput\RGFS\bin\x64\[Debug|Release]\Setup\SetupRGFS.exe

## Trying out RGFS

* RGFS will work with any git service that supports the RGFS [protocol](Protocol.md). For now, that means you'll need to create a repo in 
Visual Studio Team Services (https://www.visualstudio.com/team-services/), and push some contents to it. There are two constraints:
  * Your repo must not enable any clean/smudge filters
  * Your repo must have a `.gitattributes` file in the root that includes the line `* -text`
* `rgfs clone <URL of repo you just created>`
* `cd <root>\src`
* Run git commands as you normally would
* `rgfs unmount` when done

# Licenses

The RGFS source code in this repo is available under the MIT license. See [License.md](License.md).

RGFS relies on the GvFlt filter driver, available as a prerelease NuGet package with its own [license](GvFlt_EULA.md).
