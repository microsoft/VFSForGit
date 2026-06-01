# VFS for Git

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
* Install Visual Studio 2022 Community Edition or higher (https://www.visualstudio.com/downloads/).
  * Include the following workloads:
    * .NET desktop development
    * Desktop development with C++
  * Include the following additional components:
    * Windows 10 or 11 SDK (10.0+)
* Install the [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0)
* Install [`vcpkg`](https://learn.microsoft.com/vcpkg/get-started/get-started) (or use the copy bundled with Visual Studio's C++ workload)
* Create a folder to clone into, e.g. `C:\Repos\VFSForGit`
* Clone this repo into the `src` subfolder, e.g. `C:\Repos\VFSForGit\src`
* Run `src\scripts\Build.bat` (defaults to a Debug build)
* You can also build in Visual Studio by opening `src\GVFS.sln` (do not upgrade any projects) and building. However, the very first
build will fail, and the second and subsequent builds will succeed. This is because the build requires a prebuild code generation step.
For details, see the build script in the previous step.

Visual Studio 2022 will [automatically prompt you to install these dependencies](https://devblogs.microsoft.com/setup/configure-visual-studio-across-your-organization-with-vsconfig/) when you open the solution. The .vsconfig file that is present in the root of the repository specifies all required components.

The installer can now be found at `C:\Repos\VFSForGit\out\GVFS.Installers\bin\[Debug|Release]\win-x64\SetupGVFS.<version>.exe`

AI coding assistants working in this repo: see [AGENTS.md](AGENTS.md) for project-specific build/test guidance.

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

VFS for Git also includes or links to third-party native libraries at build
time. See [THIRD-PARTY-NOTICES.md](THIRD-PARTY-NOTICES.md) for details on
these dependencies and their licenses.
