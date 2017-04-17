# GVFS

## What is GVFS?

GVFS stands for Git Virtual File System. GVFS virtualizes the file system beneath your git repo so that git and all tools
see what appears to be a normal repo, but GVFS only downloads objects as they are needed. GVFS also manages git's sparse-checkout
to ensure that git operations like status, checkout, etc., can be as quick as possible because they will only consider the files
that the user has accessed, not all files in the repo.

GVFS is still in progress, but it is available here for anyone to try out. Feel free to send us feedback, bug reports, suggestions, and pull requests!

## Building GVFS

* Install Visual Studio 2017 Community Edition or higher (https://www.visualstudio.com/downloads/). Include the ".Net desktop development" and 
"Desktop development with C++" workloads, as well as the following additional components:
  * .Net Framework 3.5 development tools
  * C++/CLI support
  * VC++ 2015.3 v140 toolset
  * Windows 10 SDK (10.0.10240.0)
* Install InnoSetup 5.5.9 or later (http://www.jrsoftware.org/isdl.php) to its default location (or you'll have to change the path in `GVFS.csproj` post-build step to match)
* Create a folder to clone into, e.g. `C:\Repos\GVFS`
* Clone this repo into the `src` subfolder, e.g. `C:\Repos\GVFS\src`
* Open `src\GVFS.sln` in Visual Studio. Do not upgrade any projects.
* Build `GVFS.sln`

## Testing GVFS

* GVFS requires Windows 10 Anniversary Update or later
* Enable test signed drivers
  * IMPORTANT: do not do this on a production machine. This is for evaluation only.
  * First, suspend BitLocker, if it is currently enabled
    * Go to Control Panel > System and Security > BitLocker Drive Encryption
    * For your OS drive, select "Suspend Protection" (This only suspends BitLocker until the next reboot. It does not disable BitLocker protection.)
  * In an elevated command prompt, type `bcdedit -set TESTSIGNING ON`
  * Reboot to apply the change, and this will also re-enable BitLocker 
* Install GVFS-enabled Git for Windows (2.12.2.gvfs.2 or later) from https://github.com/Microsoft/git/releases/tag/gvfs.preview
  * This build behaves the same as Git for Windows except if the config value `core.gvfs` is set to `true`.
* Install GVFS from your build output
  * If you built it as described above, the installer can be found at `C:\Repos\GVFS\BuildOutput\GVFS\bin\x64\[Debug|Release]\Setup\SetupGVFS.exe`
* GVFS will work with any git service that supports the GVFS [protocol](Protocol.md). For now, that means you'll need to create a repo in 
Team Services (https://www.visualstudio.com/team-services/), and push some contents to it. There are two constraints:
  * Your repo must not enable any clean/smudge filters
  * Your repo must have a `.gitattributes` file in the root that includes the line `* -text`
* `gvfs clone <URL of repo you just created>`
* `cd <root>\src`
* Run git commands as you normally would
* `gvfs unmount` when done

# Licenses

The GVFS source code in this repo is available under the MIT license. See [License.md](License.md).

GVFS relies on the GvFlt filter driver, available as a prerelease NuGet package with its own [license](GvFlt_EULA.docx).
