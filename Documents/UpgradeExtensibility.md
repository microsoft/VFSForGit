VFSForGit Upgrade Extensibility Design Document
===============================

Scope
-----

Enable groups to extend the Upgrade verb to install / update extra
components besides just VFSForGit and Git.

Motivation
----------

VFSForGit can be deployed as a component in a larger engineering
system. Some engineering systems could include components that are coupled
with specific versions of VFSForGit and should be updated with with
VFSForGit. There could be other interactions specific to the
environment that need to be accounted for when updating VFSForGit. As
these requirements are specific to the individual environments
VFSForGit is being deployed into, we need a way for groups to include
extra components and logic in the VFSForGit upgrade.

Requirements
------------

* Organizations can include extra components / logic in VFSForGit
  Upgrade

* Upgrade feed needs to support advertising current available version

* Deliver upgrades for VFSForGit and associated components. Associated
  components include the version of Git tied to a VFSForGit version,
  along with any additional components that organizations might wish
  to update along with VFSForGit.
  
* Configure settings related to VFSForGit that organizations might
  want to set.

Non-Goals
---------

* Initial configuration of the alternate feed URL

* Being a general purpose solution for delivering upgrades for any
  product an organization wishes to maintain.


Considerations
----

* Authentication: The NuGet feed might require authentication.

* Validation: How do we validate that the upgrade is installing trusted components

Description
------

VFSForGit upgrade logic will support being upgraded from sources other
than GitHub releases, which is the only upgrade mechanism supported
today.

We will tweak the high level logic to support upgrades from different
sources. To enable the above goals, we will implement an upgrader that
is driven by NuGet feeds and packages. The NuGet package will be
structed so that arbitrary components can be deployed and upgrade
steps run.

### Configuring the Upgrade mechanism

The GitHub upgrader currently uses `gvfs.config` to store upgrade
related configuration data, including the upgrade ring to use.

NuGet upgrader will also store its configuration related data in this
file. If there is data for both the GitHub upgrader and NuGet
upgrader, then the NuGet upgrader will be preferred.

** Question: Maybe we should explicitly specify the upgrader in the
             config to remove potential ambiguity **

The NuGet upgrader requires the following configuration:

* upgrade.feedurl : The NuGet feed to download upgrade packages from

* upgrade.feedpackagename : The name of the NuGet package to use

* upgrade.feedcrendentialurl : The URL to use to generate a credential
  that can be used to access the NuGet feed.

** Question: who can access this file **

### NuGet Upgrade Package Contents

The format of the NuGet packages used with the NuGet upgrader are described as follows:

```
/
  /content
    /Installers
	  /{Platform}
	    /{Installerfolder-2}
		/{InstallerFolder-2}
	/install-manifest.json
```

The actual actions taken during install are described in the install-manifest.json file.

### Install-manifest.json

The Install-manifest.json file prescribes the steps that are run
during an upgrade. It contains sections for different platforms to
enable cross platform distributions.

A Sample JSON file that would run 2 upgrade actions is:

```
{
  "Version" : "1",
  "Platforms" : {
    "Windows": {
    "InstallActions": [
      {
 	    "Name" : "Git",
 	    "Version" : "2.19.0.1.34",
 	    "RelativePath" : "Installers\\Windows\\G4W\\Git-2.19.0.gvfs.1.34.gc7fb556-64-bit.exe",
 	    "Args" : "/VERYSILENT /CLOSEAPPLICATIONS"
      },
      {
 	    "Name" : "PreGitInstaller",
 	    "Version" : "0.0.0.1",
 	    "RelativePath" : "Installers\\Windows\\GSD\\PreGitInstallerSetup.exe"
      },
      ]
    }
  }
}
```

The install steps will be run in the order listed.

### Design : Orchestrator

At a high level, the upgrade logic can be broken into 2 components:

* UpgradeOrchestrator: Responsible for the high level orchestration of
  the upgrade process. It is not tied to the details of where the
  upgrade package is coming from. It will rely on IProductUpgrader
  implementations to handle the details of interacting with individual
  package sources.

* IProductUpgrader implementation: These components will know how to
  communicate with upgrade package sources (e.g. GitHub releases,
  NuGet Feeds) to query for the latest version, download packages, and
  run the upgrade.

### NuGetUpgrader Components

#### NuGetUpgrader

Implements IProductUpgrader and exposes high level API interacting
with NuGet feed containing Upgrade packages, downloading and unpacking
NuGet packages, and installing the contained components.

#### LocalUpgraderServices

Responsible for the local operations that Upgraders need to run. This
can include creating temporary directories and running the actual
installers.

#### NuGetFeedWrapper

Responsible for interacting with remote NuGet feeds. This includes
querying a NuGet feed for available packages and downloading NuGet
packages.

#### ReleaseManifest

Responsible for reading the data file that describes the steps the
upgrader should run.

### Checking for new upgrades

Now that we have to handle different feed sources, the installers will
need to follow a convention for us to read the version from, or we
might want to put the version infromation in a well known file. The
advantage of storing the version information in a file is that we can
update this file by querying the feed endpoints, and not necessarily
waiting to download the actual installer.

### TBD:

* Cross platform

### Alternative designs

#### Installer Alternative 1

Instead of the package containing 3 different installers, it could
contain a single installer that would be responsible for upgrading
Git, GVFS, and any other components. This would be simpler from the
VFSForGit Upgrade logic, as it would only need to run a single
installer, but the installer generation would be more complex, as it
would need to include logic for all updated packaged - including the
git and VFSForGit installers. As we already have the logic to run the
git and VFSForGit installers, we decided against this option because
it adds more complexity to the installer generation.

#### Installer Alternative 2

Git and GVFS would be upgraded from the public feed as normal, and
only the extra components would be installed from the specified
feed. We decided against this because the components to be distributed
can be coupled, and having two independent feeds could make it
difficult to keep these in sync.
