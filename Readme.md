# VFS for Git

**Notice:** With the release of VFS for Git 2.32, VFS for Git is in maintenance mode. Only required updates as a reaction to critical security vulnerabilities will prompt a release.

|Branch|Unit Tests|Functional Tests|Large Repo Perf|Large Repo Build|
|:--:|:--:|:--:|:--:|:--:|
|**master**|[![Build status](https://dev.azure.com/gvfs/ci/_apis/build/status/CI%20-%20Windows?branchName=master)](https://dev.azure.com/gvfs/ci/_build/latest?definitionId=7&branchName=master)|[![Build status](https://dev.azure.com/gvfs/ci/_apis/build/status/CI%20-%20Windows%20-%20Full%20Functional%20Tests?branchName=master)](https://dev.azure.com/gvfs/ci/_build/latest?definitionId=6&branchName=master)|[![Build status](https://dev.azure.com/mseng/AzureDevOps/_apis/build/status/GVFS/GitHub%20VFSForGit%20Large%20Repo%20Perf%20Tests?branchName=master)](https://dev.azure.com/mseng/AzureDevOps/_build/latest?definitionId=7179&branchName=master)|[![Build status](https://dev.azure.com/mseng/AzureDevOps/_apis/build/status/GVFS/GitHub%20VFSForGit%20Large%20Repo%20Build?branchName=master)](https://dev.azure.com/mseng/AzureDevOps/_build/latest?definitionId=7180&branchName=master)|
|**shipped**|[![Build status](https://dev.azure.com/gvfs/ci/_apis/build/status/CI%20-%20Windows?branchName=releases%2Fshipped)](https://dev.azure.com/gvfs/ci/_build/latest?definitionId=7&branchName=releases%2Fshipped)|[![Build status](https://dev.azure.com/gvfs/ci/_apis/build/status/CI%20-%20Windows%20-%20Full%20Functional%20Tests?branchName=releases%2Fshipped)](https://dev.azure.com/gvfs/ci/_build/latest?definitionId=6&branchName=releases%2Fshipped)|[![Build status](https://dev.azure.com/mseng/AzureDevOps/_apis/build/status/GVFS/GitHub%20VFSForGit%20Large%20Repo%20Perf%20Tests?branchName=releases%2Fshipped)](https://dev.azure.com/mseng/AzureDevOps/_build/latest?definitionId=7179&branchName=releases%2Fshipped)|[![Build status](https://dev.azure.com/mseng/AzureDevOps/_apis/build/status/GVFS/GitHub%20VFSForGit%20Large%20Repo%20Build?branchName=releases%2Fshipped)](https://dev.azure.com/mseng/AzureDevOps/_build/latest?definitionId=7180&branchName=releases%2Fshipped)|

## What is VFS for Git?

VFS stands for Virtual File System. VFS for Git virtualizes the file system
beneath your Git repository so that Git and all tools see what appears to be a
regular working directory, but VFS for Git only downloads objects as they
are needed. VFS for Git also manages the files that Git will consider, to
ensure that Git operations such as `status`, `checkout`, etc., can be as quick
as possible because they will only consider the files that the user has
accessed, not all files in the repository.

Note: for new deployments, we strongly recommend you consider
[Scalar](https://github.com/microsoft/scalar) instead of VFS for Git. By
combining the lessons from operating VFS for Git at scale with new developments
in Git, Scalar offers a clearer path forward for all large monorepos.

## Installing VFS for Git

VFS for Git requires Windows 10 Anniversary Update (Windows 10 version 1607) or later.

To install, use [`winget`](https://github.com/microsoft/winget-cli) to install the
[`microsoft/git` fork of Git](https://github.com/microsoft/git) and VFS for Git
using:

```
winget install --id Microsoft.Git
winget install --id Microsoft.VFSforGit
```

You will need to continue using the `microsoft/git` version of Git, and it
will notify you when new versions are available.


## Building VFS for Git

If you'd like to build your own VFS for Git Windows installer:
* Install Visual Studio 2019 Community Edition or higher (https://www.visualstudio.com/downloads/).
  * Include the following workloads:
    * .NET desktop development
    * Desktop development with C++
    * .NET Core cross-platform development
  * Include the following additional components:
    * .NET Core runtime
    * MSVC v141 VS 2017 C++ build tools. It is under the "Desktop Development with C++" heading.
* Install the Windows 10 SDK (10.0.16299.91) via the archived SDK page: https://developer.microsoft.com/en-us/windows/downloads/sdk-archive
* Install the .NET Core 2.1 SDK (https://www.microsoft.com/net/download/dotnet-core/2.1)
* Install [`nuget.exe`](https://www.nuget.org/downloads)
* Create a folder to clone into, e.g. `C:\Repos\VFSForGit`
* Clone this repo into the `src` subfolder, e.g. `C:\Repos\VFSForGit\src`
* Run `\src\Scripts\BuildGVFSForWindows.bat`
* You can also build in Visual Studio by opening `src\GVFS.sln` (do not upgrade any projects) and building. However, the very first
build will fail, and the second and subsequent builds will succeed. This is because the build requires a prebuild code generation step.
For details, see the build script in the previous step.

Visual Studio 2019 will [automatically prompt you to install these dependencies](https://devblogs.microsoft.com/setup/configure-visual-studio-across-your-organization-with-vsconfig/) when you open the solution. The .vsconfig file that is present in the root of the repository specifies all required components _except_ the Windows 10 SDK (10.0.16299.91) as this component is no longer shipped with VS2019 - **you'll still need to install that separately**.

The installer can now be found at `C:\Repos\VFSForGit\BuildOutput\GVFS.Installer.Windows\bin\x64\[Debug|Release]\SetupGVFS.<version>.exe`

## Trying out VFS for Git

* VFS for Git requires a Git service that supports the
  [GVFS protocol](Protocol.md). For example, you can create a repo in
  [Azure DevOps](https://azure.microsoft.com/services/devops/), and push
  some contents to it. There are two constraints:
  * Your repo must not enable any clean/smudge filters
  * Your repo must have a `.gitattributes` file in the root that includes the line `* -text`
* `gvfs clone <URL of repo you just created>`
  * Please choose the **Clone with HTTPS** option in the `Clone Repository` dialog in Azure Repos, not **Clone with SSH**.
* `cd <root>\src`
* Run Git commands as you normally would
* `gvfs unmount` when done

## Note on naming

This project was formerly known as GVFS (Git Virtual File System). You may occasionally
see collateral, including code and protocol names, which refer to the previous name.

# Licenses

The VFS for Git source code in this repo is available under the MIT license.
See [License.md](License.md).

VFS for Git relies on the PrjFlt filter driver, formerly known as the GvFlt
filter driver, available as a prerelease NuGet package.
